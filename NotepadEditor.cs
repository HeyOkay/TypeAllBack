using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace LayoutPreviewPrototype;
internal sealed record EditorSnapshot(IntPtr Foreground, IntPtr Editor, uint Thread, IntPtr Layout,
    string Text, int SelectionStart, int SelectionEnd, Native.RECT Caret, uint Dpi);
// Bounded Win32 adapter. Unknown controls, selections, passwords and IME fail closed.
internal static class NotepadEditor
{
    private const uint WM_GETTEXT = 0x000D, WM_GETTEXTLENGTH = 0x000E;
    private const uint EM_GETSEL = 0x00B0, EM_SETSEL = 0x00B1, EM_REPLACESEL = 0x00C2;
    public static bool TryRead(out EditorSnapshot snapshot)
    {
        snapshot = null!;
        var foreground = Native.GetForegroundWindow();
        uint thread = Native.GetWindowThreadProcessId(foreground, out uint pid);
        if (thread == 0) return false;
        try { using var process = Process.GetProcessById((int)pid); if (!process.ProcessName.Equals("Notepad", StringComparison.OrdinalIgnoreCase)) return false; }
        catch (ArgumentException) { return false; }
        var gui = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(thread, ref gui) || gui.hwndFocus == IntPtr.Zero || gui.hwndCaret == IntPtr.Zero || (gui.flags & 0x1E) != 0) return false;
        var cls = new StringBuilder(256); Native.GetClassName(gui.hwndFocus, cls, cls.Capacity);
        if (cls.ToString() is not ("Edit" or "RichEditD2DPT" or "RICHEDIT50W")) return false;
        if ((Native.GetWindowLong(gui.hwndFocus, -16) & (0x20 | 0x800)) != 0) return false;
        var ime = Native.ImmGetContext(gui.hwndFocus);
        if (ime != IntPtr.Zero)
        {
            bool composing = Native.ImmGetCompositionString(ime, 8, IntPtr.Zero, 0) > 0;
            Native.ImmReleaseContext(gui.hwndFocus, ime); if (composing) return false;
        }
        if (!TryMessage(gui.hwndFocus, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, out var lengthResult)) return false;
        int length = (int)lengthResult; if (length < 0 || length > 1_000_000) return false;
        var buffer = Marshal.AllocHGlobal((length + 1) * 2); var selection = Marshal.AllocHGlobal(8);
        try
        {
            if (!TryMessage(gui.hwndFocus, WM_GETTEXT, (IntPtr)(length + 1), buffer, out var copied) || (int)copied != length) return false;
            string text = Marshal.PtrToStringUni(buffer, length)!;
            Marshal.WriteInt32(selection, -1); Marshal.WriteInt32(selection, 4, -1);
            if (!TryMessage(gui.hwndFocus, EM_GETSEL, selection, selection + 4, out _)) return false;
            int start = Marshal.ReadInt32(selection), end = Marshal.ReadInt32(selection, 4);
            if (start < 0 || end < start || end > length) return false;
            var top = new Native.POINT { X = gui.rcCaret.Left, Y = gui.rcCaret.Top };
            var bottom = new Native.POINT { X = gui.rcCaret.Right, Y = gui.rcCaret.Bottom };
            if (!Native.ClientToScreen(gui.hwndCaret, ref top) || !Native.ClientToScreen(gui.hwndCaret, ref bottom) || bottom.Y <= top.Y) return false;
            snapshot = new(foreground, gui.hwndFocus, thread, Native.GetKeyboardLayout(thread), text, start, end,
                new Native.RECT { Left = top.X, Top = top.Y, Right = bottom.X, Bottom = bottom.Y }, Native.GetDpiForWindow(gui.hwndFocus));
            return Native.GetForegroundWindow() == foreground;
        }
        finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(selection); }
    }
    public static bool TryReplace(EditorSnapshot expected, InputSession session, IntPtr targetLayout)
    {
        if (!TryRead(out var current) || !SameEditor(expected, current) || !session.Matches(current.Text, current.SelectionStart, current.SelectionEnd)) return false;
        if (!TryMessage(current.Editor, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, targetLayout, out _) || Native.GetKeyboardLayout(current.Thread) != targetLayout) return false;
        if (!TryRead(out current) || !SameEditor(expected, current) || !session.Matches(current.Text, current.SelectionStart, current.SelectionEnd)) return false;
        string replacement = session.Replacement; var text = Marshal.StringToHGlobalUni(replacement);
        try
        {
            if (!TryMessage(current.Editor, EM_SETSEL, (IntPtr)session.Start, (IntPtr)session.End, out _)) return false;
            if (!TryRead(out var selected) || !SameEditor(current, selected) || selected.Text != current.Text || selected.SelectionStart != session.Start || selected.SelectionEnd != session.End) return false;
            if (!TryMessage(current.Editor, EM_REPLACESEL, (IntPtr)1, text, out _)) return false;
            string expectedText = current.Text.Remove(session.Start, session.Source.Length).Insert(session.Start, replacement);
            return TryRead(out var result) && SameEditor(current, result) && result.Text == expectedText && result.SelectionStart == session.Start + replacement.Length && result.SelectionEnd == result.SelectionStart;
        }
        finally { Marshal.FreeHGlobal(text); }
    }
    public static bool SameEditor(EditorSnapshot a, EditorSnapshot b) => a.Foreground == b.Foreground && a.Editor == b.Editor && a.Thread == b.Thread;
    private static bool TryMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, out UIntPtr result) =>
        Native.SendMessageTimeout(hwnd, message, wParam, lParam, 0x0002 | 0x0020, 50, out result) != IntPtr.Zero;
}