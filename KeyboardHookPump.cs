using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LayoutPreviewPrototype;

internal readonly record struct CapturedKeyboardInput(Native.KBDLLHOOKSTRUCT Data, int Message,
    bool Shift, bool CapsLock, bool Control, bool Alt, bool Win, EditorSnapshot? Snapshot,
    long Sequence, long CapturedAt, bool AcceptedRight, double HookCaptureMs = 0, bool AcceptedEnter = false, long InputEpoch = 0);
internal sealed record KeyboardAcceptContext(EditorSnapshot Editor, long Sequence);

// Never wait for WPF, read document text, call COM, or render on this thread.
internal sealed class KeyboardHookPump(Action<CapturedKeyboardInput> deliver, bool testInput) : IDisposable
{
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _thread;
    private Native.LowLevelKeyboardProc? _proc;
    private uint _threadId;
    private IntPtr _hook;
    private IntPtr _focusHook, _foregroundHook;
    private Native.WinEventProc? _focusProc;
    private Exception? _error;
    private long _sequence, _inputEpoch;
    public long LastInputEpoch => Interlocked.Read(ref _inputEpoch);
    private int _disposeState;
    private KeyboardAcceptContext? _acceptContext;
    private readonly EnterReservation _enter = new();
    public void CompleteEnter() => _enter.Complete();
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
            // Native out-of-context events invalidate cached fields without
            // registering a desktop-wide UIA listener (which activates browser AX).
            _focusProc = (_, _, _, _, _, _, _) =>
            {
                AcceptContext = null;
                TextEditor.NotifyFocusChanged();
            };
            _focusHook = Native.SetWinEventHook(Native.EVENT_OBJECT_FOCUS, Native.EVENT_OBJECT_FOCUS,
                IntPtr.Zero, _focusProc, 0, 0, 0x0002); // OUTOFCONTEXT | SKIPOWNPROCESS
            _foregroundHook = Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _focusProc, 0, 0, 0x0002);
            if (_focusHook == IntPtr.Zero || _foregroundHook == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            _ready.Set();
            while (Native.GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
        }
        catch (Exception error) { _error = error; _ready.Set(); }
        finally
        {
            if (_focusHook != IntPtr.Zero) Native.UnhookWinEvent(_focusHook);
            if (_foregroundHook != IntPtr.Zero) Native.UnhookWinEvent(_foregroundHook);
            if (_hook != IntPtr.Zero) Native.UnhookWindowsHookEx(_hook);
        }
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
        if (relevantDown && (!control && !alt && !win || snapshot != null))
            TextEditor.NotifyInput();
        long beforeSequence = LastSequence;
        bool accepted = relevantDown && data.vkCode == Native.VK_RIGHT && !shift && !control && !alt && !win &&
            ((data.flags & Native.LLKHF_INJECTED) == 0 || testInput) && CanAccept(AcceptContext, snapshot, beforeSequence);
        bool enterEligible = relevantDown && AutoEnterOptions.Enabled &&
            ((data.flags & Native.LLKHF_INJECTED) == 0 || testInput) && CanAccept(AcceptContext, snapshot, beforeSequence);
        var enter = _enter.Handle(data.vkCode, down, message is 0x0101 or 0x0105,
            !shift && !control && !alt && !win, enterEligible);
        if (enter == EnterDisposition.Suppress) return (IntPtr)1;
        bool acceptedEnter = enter == EnterDisposition.Intercept;
        long inputEpoch = down ? Interlocked.Increment(ref _inputEpoch) : LastInputEpoch;
        long sequence = relevantDown ? Interlocked.Increment(ref _sequence) : beforeSequence;
        if (relevantDown) Interlocked.Exchange(ref _acceptContext, null);
        try
        {
            deliver(new(data, message, shift, (Native.GetKeyState(0x14) & 1) != 0, control, alt, win,
                snapshot, sequence, Environment.TickCount64, accepted,
                Stopwatch.GetElapsedTime(captureStarted).TotalMilliseconds, acceptedEnter, inputEpoch));
        }
        catch (InvalidOperationException) { accepted = acceptedEnter = false; _enter.Abandon(); } // Dispatcher is shutting down: pass through.
        return accepted || acceptedEnter ? (IntPtr)1 : Native.CallNextHookEx(_hook, code, wParam, lParam);
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
