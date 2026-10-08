using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
namespace LayoutPreviewPrototype;
public sealed class LayoutPreviewController : IDisposable
{
    private readonly PreviewWindow _preview = new();
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly bool _testInput = Environment.GetCommandLineArgs().Contains("--test-input");
    private Native.LowLevelKeyboardProc? _proc;
    private IntPtr _hook;
    private InputSession? _session;
    private EditorSnapshot? _editor;
    private IntPtr _targetLayout;
    private bool _visible, _accepting, _disposed;
    private int _generation;
    public void Start()
    {
        _preview.AcceptRequested += (_, _) => AcceptPreview();
        _watch.Tick += (_, _) => UpdatePreview();
        _proc = HookCallback;
        using var process = Process.GetCurrentProcess(); using var module = process.MainModule!;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(module.ModuleName), 0);
        if (_hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || (wParam != (IntPtr)Native.WM_KEYDOWN && wParam != (IntPtr)Native.WM_SYSKEYDOWN)) return Next();
        var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
        if (_accepting) { Reset(); return Next(); }
        if ((data.flags & Native.LLKHF_INJECTED) != 0 && !_testInput) { Reset(); return Next(); }
        int vk = (int)data.vkCode;
        if (vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5) return Next();
        if (vk is Native.VK_LWIN or Native.VK_RWIN) { Reset(); return Next(); }
        bool shift = Native.IsKeyDown(Native.VK_SHIFT);
        if (Native.IsKeyDown(Native.VK_CONTROL) || Native.IsKeyDown(Native.VK_MENU) || Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN)) { Reset(); return Next(); }
        if (!NotepadEditor.TryRead(out var snapshot) || !TryLayout(snapshot.Layout, out var layout)) { Reset(); return Next(); }
        if (_session != null && (_editor == null || !NotepadEditor.SameEditor(_editor, snapshot) || !_session.Matches(snapshot.Text, snapshot.SelectionStart, snapshot.SelectionEnd))) Reset();
        if (vk == Native.VK_RIGHT)
        {
            if (!shift && _visible && _session?.Preview.Length > 0 && _targetLayout != IntPtr.Zero)
            { _accepting = true; Application.Current.Dispatcher.BeginInvoke(AcceptPreview); return (IntPtr)1; }
            Reset(); return Next();
        }
        if (vk == Native.VK_BACK)
        {
            if (_session == null || !_session.Backspace(snapshot.Text)) Reset(); else ScheduleUpdate();
            return Next();
        }
        var key = new PhysicalKey(data.vkCode, shift, (Native.GetKeyState(0x14) & 1) != 0);
        if (!KeyboardConversion.TryConvert(key, layout, out _) || snapshot.SelectionStart != snapshot.SelectionEnd) { Reset(); return Next(); }
        if (_session == null)
        {
            _session = new InputSession(snapshot.Text, snapshot.SelectionEnd, layout); _editor = snapshot;
            _targetLayout = FindLayout(_session.TargetLayout);
            if (_targetLayout == IntPtr.Zero) { Reset(); return Next(); }
            _watch.Start();
        }
        if (_session.Source.Length >= 4096) { Reset(); return Next(); }
        _session.Append(key, layout, snapshot.Text); ScheduleUpdate(); return Next();
        IntPtr Next() => Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }
    private void ScheduleUpdate()
    {
        int generation = _generation;
        Application.Current.Dispatcher.BeginInvoke(() => { if (generation == _generation) UpdatePreview(); }, DispatcherPriority.Background);
    }
    private void UpdatePreview()
    {
        if (_disposed || _accepting || _session == null) return;
        if (!NotepadEditor.TryRead(out var current) || _editor == null || !NotepadEditor.SameEditor(_editor, current) || !_session.Matches(current.Text, current.SelectionStart, current.SelectionEnd)) { Reset(); return; }
        _editor = current; string text = _session.Preview;
        if (text.Length == 0) { _visible = false; _preview.HidePreview(); return; }
        bool first = !_visible; _visible = true;
        _preview.ShowPreview(text, current.Caret, current.Dpi == 0 ? 96 : current.Dpi, first);
    }
    private async void AcceptPreview()
    {
        if (_disposed || !_visible || _session == null || _editor == null || _session.Preview.Length == 0) { _accepting = false; return; }
        _accepting = true; var session = _session; var editor = _editor; int generation = _generation;
        try
        {
            if (!NotepadEditor.TryRead(out var current) || !NotepadEditor.SameEditor(editor, current) || !session.Matches(current.Text, current.SelectionStart, current.SelectionEnd)) return;
            var animation = _preview.PlayAcceptAnimationAsync(); await Task.Delay(PreviewWindow.AcceptMidpointDelayMs);
            if (generation == _generation) NotepadEditor.TryReplace(editor, session, _targetLayout);
            await animation;
        }
        finally { Reset(); _accepting = false; }
    }
    internal static bool TryLayout(IntPtr hkl, out KeyboardLayout layout)
    {
        ushort language = unchecked((ushort)hkl.ToInt64()); layout = language == 0x0419 ? KeyboardLayout.Russian : KeyboardLayout.English;
        return (ulong)hkl.ToInt64() == (language == 0x0419 ? 0x04190419UL : 0x04090409UL);
    }
    private static IntPtr FindLayout(KeyboardLayout desired)
    {
        int count = Native.GetKeyboardLayoutList(0, null); if (count <= 0) return IntPtr.Zero;
        var list = new IntPtr[count]; Native.GetKeyboardLayoutList(count, list);
        return list.FirstOrDefault(hkl => (ulong)hkl.ToInt64() == (desired == KeyboardLayout.Russian ? 0x04190419UL : 0x04090409UL));
    }
    private void Reset()
    {
        _generation++; _session = null; _editor = null; _targetLayout = IntPtr.Zero; _visible = false;
        _watch.Stop(); _preview.HidePreview();
    }
    public void Dispose()
    {
        _disposed = true; Reset();
        if (_hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _preview.Close();
    }
}