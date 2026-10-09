using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LayoutPreviewPrototype;

internal readonly record struct CapturedKeyboardInput(Native.KBDLLHOOKSTRUCT Data, int Message,
    bool Shift, bool CapsLock, bool Control, bool Alt, bool Win, EditorSnapshot? Snapshot,
    long Sequence, long CapturedAt, bool AcceptedRight, double HookCaptureMs = 0);
internal sealed record KeyboardAcceptContext(EditorSnapshot Editor, long Sequence);

// Never wait for WPF, read document text, call COM, or render on this thread.
internal sealed class KeyboardHookPump(Action<CapturedKeyboardInput> deliver, bool testInput) : IDisposable
{
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private Native.LowLevelKeyboardProc? _proc;
    private uint _threadId;
    private IntPtr _hook;
    private Exception? _error;
    private long _sequence;
    private int _disposeState;
    private KeyboardAcceptContext? _acceptContext;
    public long LastSequence => Interlocked.Read(ref _sequence);
    public bool IsRunning => _thread?.IsAlive == true;
    public uint ThreadId => Volatile.Read(ref _threadId);
    public KeyboardAcceptContext? AcceptContext { get => Volatile.Read(ref _acceptContext); set => Volatile.Write(ref _acceptContext, value); }
    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "LayoutPreview keyboard" };
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("Keyboard hook startup timed out");
        if (_error != null) throw new InvalidOperationException("Keyboard hook startup failed", _error);
    }
    private void Run()
    {
        try
        {
            _threadId = Native.GetCurrentThreadId();
            Native.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            _proc = Callback;
            using var process = Process.GetCurrentProcess(); using var module = process.MainModule!;
            _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(module.ModuleName), 0);
            if (_hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            _ready.Set();
            while (Native.GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        }
        catch (Exception error) { _error = error; _ready.Set(); }
        finally { if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook); }
    }
    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return Native.CallNextHookEx(_hook, code, wParam, lParam);
        long captureStarted = Stopwatch.GetTimestamp();
        var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
        if ((data.flags & Native.LLKHF_INJECTED) != 0 && data.dwExtraInfo == Native.LayoutPreviewInput)
            return Native.CallNextHookEx(_hook, code, wParam, lParam);
        int message = wParam.ToInt32();
        bool down = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
        bool relevantDown = down && data.vkCode is not (0x10 or 0xA0 or 0xA1 or 0x11 or 0xA2 or 0xA3 or 0x12 or 0xA4 or 0xA5 or 0x5B or 0x5C);
        bool shift = Native.IsKeyDown(Native.VK_SHIFT), control = Native.IsKeyDown(Native.VK_CONTROL);
        bool alt = Native.IsKeyDown(Native.VK_MENU), win = Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN);
        // Native deletion must not depend on cross-process GUI focus checks.
        // The controller tracks Backspace against its own confirmed session.
        var snapshot = SnapshotForKey(data.vkCode, ReadSnapshot);
        if (relevantDown) TextEditor.NotifyInput();
        long beforeSequence = LastSequence;
        bool accepted = relevantDown && data.vkCode == Native.VK_RIGHT && !shift && !control && !alt && !win &&
            ((data.flags & Native.LLKHF_INJECTED) == 0 || testInput) && CanAccept(AcceptContext, snapshot, beforeSequence);
        long sequence = relevantDown ? Interlocked.Increment(ref _sequence) : beforeSequence;
        if (relevantDown) Interlocked.Exchange(ref _acceptContext, null);
        try
        {
            deliver(new(data, message, shift, (Native.GetKeyState(0x14) & 1) != 0, control, alt, win,
                snapshot, sequence, Environment.TickCount64, accepted,
                Stopwatch.GetElapsedTime(captureStarted).TotalMilliseconds));
        }
        catch (InvalidOperationException) { accepted = false; } // Dispatcher is shutting down: pass through.
        return accepted ? (IntPtr)1 : Native.CallNextHookEx(_hook, code, wParam, lParam);
    }
    private static EditorSnapshot? ReadSnapshot() => TextEditor.TryReadForHook(out var snapshot) ? snapshot : null;
    internal static EditorSnapshot? SnapshotForKey(uint key, Func<EditorSnapshot?> read) =>
        key == Native.VK_BACK ? null : read();

    internal static bool CanAccept(KeyboardAcceptContext? context, EditorSnapshot? current, long sequence) =>
        context != null && current != null && context.Sequence == sequence &&
        TextEditor.SameEditor(context.Editor, current) && context.Editor.Layout == current.Layout &&
        context.Editor.Text == current.Text && current.SelectionStart == current.SelectionEnd &&
        current.SelectionStart == context.Editor.SelectionStart && current.SelectionEnd == context.Editor.SelectionEnd;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        AcceptContext = null;
        if (_threadId != 0) Native.PostThreadMessage(_threadId, 0x0012, UIntPtr.Zero, IntPtr.Zero);
        _thread?.Join(500);
    }
}
