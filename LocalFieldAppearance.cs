using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Media;

namespace LayoutPreviewPrototype;

internal sealed record FieldPixelStyle(Color Background, Color? Foreground, int GlyphHeight);

// A tiny in-memory patch of a verified field, sampled only by the existing MTA
// formatting worker. No screenshots/files, OCR, browser connection or injection.
internal static class LocalFieldAppearance
{
    public static EditorSnapshot Enrich(EditorSnapshot context, TextPatternRange range)
    {
        try { return EnrichCore(context, range); }
        catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or ElementNotAvailableException)
        { return context; }
    }
    private static EditorSnapshot EnrichCore(EditorSnapshot context, TextPatternRange range)
    {
        Color? background = AttributeColor(range, TextPattern.BackgroundColorAttribute);
        Color? foreground = AttributeColor(range, TextPattern.ForegroundColorAttribute);
        if (background == foreground) background = foreground = null;
        string source = background != null ? "UIA colors" : "Windows fallback";
        FieldPixelStyle? pixels = null;
        string glyph = range.GetText(2);
        if (context.SelectionStart == context.SelectionEnd && Native.GetForegroundWindow() == context.Foreground)
        {
            var bounds = range.GetBoundingRectangles();
            if (bounds.Length == 1 && bounds[0].Width is >= 3 and <= 96 && bounds[0].Height is >= 6 and <= 96)
            {
                var rect = bounds[0];
                int left = (int)Math.Ceiling(rect.Left), top = (int)Math.Ceiling(rect.Top);
                int right = (int)Math.Floor(rect.Right);
                // Never capture our preview, which is on the right of the caret.
                if (context.Caret.Bottom > context.Caret.Top) right = Math.Min(right, context.Caret.Left);
                pixels = Sample(left, top, right - left, (int)Math.Floor(rect.Bottom) - top);
                if (Native.GetForegroundWindow() != context.Foreground) return context;
            }
            if (pixels == null && context.Caret.Bottom - context.Caret.Top is >= 6 and <= 96)
            {
                int height = context.Caret.Bottom - context.Caret.Top;
                int width = Math.Clamp(height * 2, 12, 64);
                pixels = Sample(context.Caret.Left - width, context.Caret.Top, width, height);
                if (context.SelectionEnd > 0 && context.SelectionEnd <= context.Text.Length)
                    glyph = context.Text.Substring(context.SelectionEnd - 1, 1);
                if (Native.GetForegroundWindow() != context.Foreground) return context;
            }
        }
        if (pixels != null)
        {
            bool inconsistentColor = background != null && Distance(background.Value, pixels.Background) > 45;
            background = pixels.Background;
            if (inconsistentColor || foreground == null) foreground = pixels.Foreground;
            source = "Local pixels";
        }
        double measured = pixels != null && glyph.Length == 1 && char.IsLetterOrDigit(glyph[0])
            ? EstimateSize(glyph[0], pixels.GlyphHeight, context.Dpi, context.FontFamilyName,
                context.FontWeightValue, context.FontItalic) : 0;
        double size = context.FontSizeDip;
        string fontSource = context.FontSource;
        // Exact Win32 metadata stays authoritative. Other attributes are checked
        // against actual glyph ink, not padded caret/line rectangles.
        if (measured > 0 && !fontSource.StartsWith("Win32", StringComparison.Ordinal) &&
            (size <= 0 || Math.Abs(size - measured) > measured * 0.25))
        { size = measured; fontSource = "Local glyph estimate" + (context.FontFamilyName.Length == 0 ? " (family fallback)" : ""); }
        return context with { FontSizeDip = size, FontSource = fontSource,
            FieldBackground = background, FieldForeground = foreground, AppearanceSource = source };
    }

    private static Color? AttributeColor(TextPatternRange range, AutomationTextAttribute attribute)
    {
        try
        {
            return range.GetAttributeValue(attribute) is int value && value is >= 0 and <= 0xFFFFFF
                ? Color.FromRgb((byte)value, (byte)(value >> 8), (byte)(value >> 16)) : null;
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or ElementNotAvailableException) { return null; }
    }

    internal static double EstimateSize(char glyph, int inkHeight, uint dpi, string family, int weight, bool italic)
    {
        if (inkHeight < 3 || inkHeight > 80 || !char.IsLetterOrDigit(glyph)) return 0;
        try
        {
            var typeface = new Typeface(new FontFamily(family.Length == 0 ? "Segoe UI" : family),
                italic ? FontStyles.Italic : FontStyles.Normal,
                FontWeight.FromOpenTypeWeight(weight is >= 100 and <= 900 ? weight : 400), FontStretches.Normal);
            var text = new FormattedText(glyph.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, 100, Brushes.Black, 1);
            double unitHeight = text.BuildGeometry(new Point()).Bounds.Height;
            double size = inkHeight * 96.0 / (dpi == 0 ? 96 : dpi) * 100 / unitHeight;
            return double.IsFinite(size) && size is >= 8 and <= 96 ? size : 0;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return 0; }
    }

    private static int Distance(Color a, Color b) => Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
    private static Color Pixel(int value) => Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    private static int Bin(Color c) => (c.R >> 4) << 8 | (c.G >> 4) << 4 | c.B >> 4;

    internal static FieldPixelStyle? Analyze(int[] values, int width, int height)
    {
        if (width is < 3 or > 96 || height is < 6 or > 96 || values.Length != width * height) return null;
        var groups = values.Select(Pixel).GroupBy(Bin).OrderByDescending(group => group.Count()).ToArray();
        var dominant = groups[0].ToArray();
        if (dominant.Length < values.Length * 0.65) return null; // gradient/image/selection: uncertain
        var background = Color.FromRgb((byte)dominant.Average(c => c.R), (byte)dominant.Average(c => c.G), (byte)dominant.Average(c => c.B));
        var ink = new List<Color>(); int top = height, bottom = -1;
        // Isolate the last ink run before the caret, not neighboring letters with
        // different ascenders/descenders. Blank inter-letter columns bound the run.
        bool InkColumn(int x)
        {
            for (int y = 1; y < height - 1; y++)
                if (Distance(Pixel(values[y * width + x]), background) >= 48) return true;
            return false;
        }
        int right = width - 2;
        while (right >= 1 && !InkColumn(right)) right--;
        if (right < 1) return new(background, null, 0);
        int left = right;
        while (left > 1 && InkColumn(left - 1)) left--;
        // Exclude rectangle edges/caret; retain only high-contrast glyph interiors.
        for (int y = 1; y < height - 1; y++) for (int x = left; x <= right; x++)
        {
            var color = Pixel(values[y * width + x]);
            if (Distance(color, background) < 48) continue;
            ink.Add(color); top = Math.Min(top, y); bottom = Math.Max(bottom, y);
        }
        int inkHeight = bottom >= top ? bottom - top + 1 : 0;
        if (ink.Count < 3 || inkHeight < 3 || inkHeight > height * 0.85)
            return new(background, null, 0);
        var strongest = ink.GroupBy(Bin).OrderByDescending(group => group.Count()).First().ToArray();
        var foreground = Color.FromRgb((byte)strongest.Average(c => c.R), (byte)strongest.Average(c => c.G), (byte)strongest.Average(c => c.B));
        return new(background, foreground, inkHeight);
    }

    private static FieldPixelStyle? Sample(int left, int top, int width, int height)
    {
        if (width is < 3 or > 96 || height is < 6 or > 96) return null;
        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        if (screen == IntPtr.Zero) return null;
        try
        {
            memory = CreateCompatibleDC(screen); if (memory == IntPtr.Zero) return null;
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out var address, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || address == IntPtr.Zero) return null;
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, screen, left, top, 0x00CC0020 | 0x40000000)) return null;
            var pixels = new int[width * height]; Marshal.Copy(address, pixels, 0, pixels.Length);
            return Analyze(pixels, width, height);
        }
        finally
        {
            if (previous != IntPtr.Zero && memory != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int Xppm, Yppm; public uint Used, Important, Color; }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dc, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
