using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Automation.Text;
namespace LayoutPreviewPrototype;
internal sealed record EditorSnapshot(IntPtr Foreground, IntPtr Editor, uint Thread, IntPtr Layout,
    string Text, int SelectionStart, int SelectionEnd, Native.RECT Caret, uint Dpi,
    string AutomationIdentity = "", long CapturedAt = 0, double FontSizeDip = 0, string FontFamilyName = "",
    int FontWeightValue = 400, bool FontItalic = false, string FontSource = "", long Revision = 0,
    System.Windows.Media.Color? FieldBackground = null, System.Windows.Media.Color? FieldForeground = null,
    string AppearanceSource = "");
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
        var gui = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(thread, ref gui) || gui.hwndFocus == IntPtr.Zero || (gui.flags & Native.GUI_INMOVESIZE) != 0) return false;
        var cls = new StringBuilder(256); Native.GetClassName(gui.hwndFocus, cls, cls.Capacity);
        if (!IsSupportedClass(cls.ToString())) return false;
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
            string text = NormalizeText(Marshal.PtrToStringUni(buffer, length)!, cls.ToString());
            Marshal.WriteInt32(selection, -1); Marshal.WriteInt32(selection, 4, -1);
            if (!TryMessage(gui.hwndFocus, EM_GETSEL, selection, selection + 4, out _)) return false;
            int start = Marshal.ReadInt32(selection), end = Marshal.ReadInt32(selection, 4);
            if (start < 0 || end < start || end > text.Length) return false;
            var top = new Native.POINT { X = gui.rcCaret.Left, Y = gui.rcCaret.Top };
            var bottom = new Native.POINT { X = gui.rcCaret.Right, Y = gui.rcCaret.Bottom };
            if (gui.hwndCaret != IntPtr.Zero) { Native.ClientToScreen(gui.hwndCaret, ref top); Native.ClientToScreen(gui.hwndCaret, ref bottom); }
            uint editorThread = Native.GetWindowThreadProcessId(gui.hwndFocus, IntPtr.Zero);
            snapshot = new(foreground, gui.hwndFocus, editorThread, Native.GetKeyboardLayout(editorThread), text, start, end,
                new Native.RECT { Left = top.X, Top = top.Y, Right = bottom.X, Bottom = bottom.Y }, Native.GetDpiForWindow(gui.hwndFocus));
            return Native.GetForegroundWindow() == foreground;
        }
        finally { Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(selection); }
    }
    // Modern Notepad renders a virtual caret. MSAA exposes its exact screen rectangle
    // even when GUITHREADINFO.rcCaret is empty. Never query COM inside the keyboard hook.
    public static bool TryGetCaret(EditorSnapshot snapshot, out Native.RECT caret)
    {
        caret = snapshot.Caret;
        if (caret.Bottom > caret.Top) return true;
        try
        {
            var element = AutomationElement.FromHandle(snapshot.Editor);
            if (element == null || !element.Current.HasKeyboardFocus || element.Current.IsPassword ||
                !element.TryGetCurrentPattern(TextPattern.Pattern, out var raw)) return false;
            var ranges = ((TextPattern)raw).GetSelection();
            if (ranges.Length != 1 || ranges[0].GetText(1).Length != 0) return false;
            var range = ranges[0]; var bounds = range.GetBoundingRectangles(); bool previous = false;
            if (bounds.Length == 0)
            {
                range = ranges[0].Clone();
                int moved = range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1);
                if (moved == 0 || range.GetText(2).Contains('\n') || range.GetText(2).Contains('\r'))
                {
                    range = ranges[0].Clone();
                    if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1) == 0 ||
                        range.GetText(2).Contains('\n') || range.GetText(2).Contains('\r')) return false;
                    previous = true;
                }
                bounds = range.GetBoundingRectangles();
            }
            if (bounds.Length == 0 || bounds[0].Height <= 0) return false;
            int x = (int)Math.Round(bounds[0].Left + (previous ? bounds[0].Width : 0)), y = (int)Math.Round(bounds[0].Top);
            caret = new Native.RECT { Left=x, Right=x+1, Top=y, Bottom=y+(int)Math.Ceiling(bounds[0].Height) };
            return Native.GetForegroundWindow() == snapshot.Foreground;
        }
        catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or COMException) { return false; }
    }
    public static async Task<bool> TrySwitchLayoutAsync(EditorSnapshot expected, InputSession session, IntPtr layout)
    {
        if (!TryRead(out var current) || !SameEditor(expected,current) || !session.Matches(current.Text,current.SelectionStart,current.SelectionEnd)) return false;
        Trace.WriteLine($"Layout requested={layout.ToInt64():X} actual={Native.GetKeyboardLayout(current.Thread).ToInt64():X} thread={current.Thread}");
        if (Native.GetKeyboardLayout(current.Thread) == layout) return true;
        TryMessage(current.Foreground, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout, out _);
        if (Native.GetKeyboardLayout(current.Thread) != layout)
            TryMessage(current.Editor, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout, out _);
        for (int i=0; i<15; i++)
        {
            if (!TryRead(out current) || !SameEditor(expected,current) || !session.Matches(current.Text,current.SelectionStart,current.SelectionEnd)) return false;
            Trace.WriteLine($"Layout requested={layout.ToInt64():X} actual={Native.GetKeyboardLayout(current.Thread).ToInt64():X} thread={current.Thread}");
        if (Native.GetKeyboardLayout(current.Thread) == layout) return true;
            await Task.Delay(10);
        }
        return false;
    }
    public static bool TryReplace(EditorSnapshot expected, InputSession session, IntPtr targetLayout, Func<bool>? stillValid = null)
    {
        bool Reject(string reason) { Trace.WriteLine("Accept check: Native — " + reason); return false; }
        Trace.WriteLine("Accept check: Native — получен запрос замены");
        bool Valid() => stillValid?.Invoke() != false;
        if (!Valid()) return Reject("Запрос отменён до чтения поля");
        if (!TryRead(out var current) || !SameEditor(expected, current) || !session.Matches(current.Text, current.SelectionStart, current.SelectionEnd)) return Reject("Первое чтение не подтвердило поле, весь текст или каретку");
        if (Native.GetKeyboardLayout(current.Thread) != targetLayout) return Reject("Целевая раскладка не подтверждена");
        if (!TryRead(out current) || !SameEditor(expected, current) || !session.Matches(current.Text, current.SelectionStart, current.SelectionEnd) || !Valid()) return Reject("Перед выделением не совпали поле, документ, каретка или запрос отменён");
        string replacement = session.Replacement; var text = Marshal.StringToHGlobalUni(replacement);
        try
        {
            Trace.WriteLine("Replace verified context");
            if (!TryMessage(current.Editor, EM_SETSEL, (IntPtr)session.Start, (IntPtr)session.End, out _)) { Trace.WriteLine("SetSel failed"); return Reject("Редактор не подтвердил команду выделения"); }
            if (!TryRead(out var selected) || !SameEditor(current, selected) || selected.Text != current.Text ||
                selected.SelectionStart != session.Start || selected.SelectionEnd != session.End ||
                selected.Layout != targetLayout || !Valid()) return Reject("Выделение всей сессии не подтверждено или запрос отменён");
            Trace.WriteLine("Replace verified selection");
            if (!TryMessage(current.Editor, EM_REPLACESEL, (IntPtr)1, text, out _)) { Trace.WriteLine("ReplaceSel failed"); return Reject("Редактор не подтвердил команду замены; результат требует проверки"); }
            string expectedText = current.Text.Remove(session.Start, session.Source.Length).Insert(session.Start, replacement);
            bool verified = TryRead(out var result) && SameEditor(current, result) && result.Text == expectedText && result.SelectionStart == session.Start + replacement.Length && result.SelectionEnd == result.SelectionStart;
            Trace.WriteLine("Accept check: Native — " + (verified ? "Весь документ и каретка после замены подтверждены" : "Результат замены не подтверждён"));
            return verified;
        }
        finally { Marshal.FreeHGlobal(text); }
    }
    // Read formatting without taking ownership of the target's GDI resources.
    public static bool TryReadFont(EditorSnapshot snapshot, out Native.LOGFONT font, out double sizeDip)
    {
        font = default; sizeDip = 0;
        if (!TryMessage(snapshot.Editor, 0x0031, IntPtr.Zero, IntPtr.Zero, out var result)) return false; // WM_GETFONT
        var handle = unchecked((IntPtr)(long)result.ToUInt64());
        if (handle == IntPtr.Zero || Native.GetFontObject(handle, Marshal.SizeOf<Native.LOGFONT>(), out font) == 0 ||
            font.Height >= 0 || string.IsNullOrWhiteSpace(font.FaceName)) return false;
        // Negative LOGFONT height is the character/em height in pixels. Positive
        // height is a cell height and is deliberately left to the UIA fallback.
        // This HFONT belongs to the target application: never delete or modify it.
        sizeDip = -font.Height * 96.0 / (snapshot.Dpi == 0 ? 96 : snapshot.Dpi);
        return double.IsFinite(sizeDip) && sizeDip > 0;
    }

    // RichEdit character positions count paragraph marks once; WM_GETTEXT expands them
    // to CRLF. All session ranges use the control's canonical UTF-16 character positions.
    internal static bool IsPlainEditClass(string controlClass) =>
        controlClass.Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
        controlClass.StartsWith("WindowsForms10.EDIT.", StringComparison.OrdinalIgnoreCase);
    internal static bool IsSupportedClass(string controlClass) => IsPlainEditClass(controlClass) ||
        controlClass.Equals("RichEdit", StringComparison.OrdinalIgnoreCase) ||
        controlClass.Equals("RichEdit20A", StringComparison.OrdinalIgnoreCase) ||
        controlClass.Equals("RichEdit20W", StringComparison.OrdinalIgnoreCase) ||
        controlClass.Equals("RichEdit50W", StringComparison.OrdinalIgnoreCase) ||
        controlClass.Equals("RichEditD2DPT", StringComparison.OrdinalIgnoreCase) ||
        controlClass.StartsWith("WindowsForms10.RichEdit20W.", StringComparison.OrdinalIgnoreCase) ||
        controlClass.StartsWith("WindowsForms10.RichEdit50W.", StringComparison.OrdinalIgnoreCase);
    internal static string NormalizeText(string text, string controlClass) => IsPlainEditClass(controlClass)
        ? text : text.Replace("\r\n", "\r").Replace('\n', '\r');
    public static bool SameEditor(EditorSnapshot a, EditorSnapshot b) => a.Foreground == b.Foreground && a.Editor == b.Editor && a.Thread == b.Thread && a.AutomationIdentity == b.AutomationIdentity;
    private static bool TryMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, out UIntPtr result) =>
        Native.SendMessageTimeout(hwnd, message, wParam, lParam, 0x0002 | 0x0020, 50, out result) != IntPtr.Zero;
}
