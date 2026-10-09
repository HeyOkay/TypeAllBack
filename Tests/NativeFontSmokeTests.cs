using System.Runtime.InteropServices;

namespace LayoutPreviewPrototype;

internal static class NativeFontSmokeTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("gdi32.dll", EntryPoint = "CreateFontIndirectW", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontIndirect(ref Native.LOGFONT font);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr resource);

    public static void Run()
    {
        var requested = new Native.LOGFONT { Height = -20, Weight = 700, Italic = 1, FaceName = "Segoe UI" };
        IntPtr font = CreateFontIndirect(ref requested), field = IntPtr.Zero;
        try
        {
            if (font == IntPtr.Zero) throw new InvalidOperationException("Test font creation failed");
            // No WS_VISIBLE, focus change, or synthesized keyboard input.
            field = CreateWindowEx(0, "Edit", "", 0, 0, 0, 200, 30,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (field == IntPtr.Zero) throw new InvalidOperationException("Hidden Edit creation failed");
            Native.SendMessage(field, 0x0030, font, IntPtr.Zero); // WM_SETFONT
            uint dpi = Native.GetDpiForWindow(field);
            var snapshot = new EditorSnapshot(field, field, Native.GetWindowThreadProcessId(field, IntPtr.Zero),
                IntPtr.Zero, "", 0, 0, default, dpi);
            if (!NotepadEditor.TryReadFont(snapshot, out var parsed, out double size))
                throw new InvalidOperationException("Actual WM_GETFONT/GetObject font parsing failed");
            if (parsed.FaceName != "Segoe UI" || parsed.Weight != 700 || parsed.Italic != 1 ||
                Math.Abs(size - 20 * 96.0 / (dpi == 0 ? 96 : dpi)) > 0.00001)
                throw new InvalidOperationException("Parsed font family, em size, weight or italic differs");
            Console.WriteLine("PASS: actual hidden Win32 Edit HFONT parsing (family, size, bold, italic; no user input)");
        }
        finally
        {
            if (field != IntPtr.Zero) DestroyWindow(field);
            if (font != IntPtr.Zero) DeleteObject(font);
        }
    }
}
