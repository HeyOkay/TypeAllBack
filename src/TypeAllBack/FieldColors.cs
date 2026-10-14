using System.Runtime.InteropServices;

namespace LayoutPreviewPrototype;

// Background of the focused field, so the preview matches the field rather than the
// Windows theme (a light Windows theme over Discord's dark composer looked foreign).
// Sampled on the UIA worker after a snapshot is published, at most once per field per
// 10 s: a strip of a few physical pixels to the right of the caret, plain SRCCOPY without
// CAPTUREBLT, and only while no preview is on screen (the preview stays visible to the
// user's own screenshots and screen sharing). No image is kept.
internal static class FieldColors
{
    private const int RefreshMs = 10000;
    private static readonly object Gate = new();
    private static string _key = "";
    private static long _sampledAt;
    private static uint _color; // 0xFFRRGGBB; 0 = unknown
    private static volatile bool _previewVisible;
    public static event Action? Changed;

    internal static bool PreviewVisible { get => _previewVisible; set => _previewVisible = value; }

    private static string Key(EditorSnapshot s) => $"{s.Foreground}:{s.Editor}:{s.AutomationIdentity}";

    public static uint Get(EditorSnapshot s)
    {
        lock (Gate) return _key == Key(s) ? _color : 0;
    }

    // UIA worker only.
    public static void Refresh(EditorSnapshot s)
    {
        if (s.Caret.Bottom <= s.Caret.Top) return;
        // The strip lies where the preview is drawn; never sample our own window.
        if (_previewVisible) return;
        string key = Key(s);
        long now = Environment.TickCount64;
        lock (Gate)
            if (key == _key && now - _sampledAt < RefreshMs) return;
        uint color = Sample(s.Caret);
        bool changed;
        lock (Gate)
        {
            // A failed sample keeps a known color of the same field.
            if (color == 0 && key == _key) color = _color;
            changed = key != _key || color != _color;
            _key = key; _sampledAt = now; _color = color;
        }
        if (changed) Changed?.Invoke();
    }

    internal static uint Sample(Native.RECT caret)
    {
        int lineHeight = caret.Bottom - caret.Top;
        if (lineHeight is < 4 or > 200) return 0;
        int width = Math.Clamp(lineHeight * 2, 16, 64), height = Math.Max(2, lineHeight / 4);
        int left = caret.Right + Math.Max(2, lineHeight / 3);
        int top = (caret.Top + caret.Bottom) / 2 - height / 2;
        var pixels = CaptureScreen(left, top, width, height);
        return pixels == null ? 0 : Dominant(pixels);
    }

    // The most frequent color must cover most of the strip; text, images or gradients fail.
    internal static uint Dominant(int[] pixels)
    {
        if (pixels.Length == 0) return 0;
        var bins = new Dictionary<int, (int Count, long R, long G, long B)>();
        foreach (int p in pixels)
        {
            int r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
            int bin = (r >> 4) << 8 | (g >> 4) << 4 | b >> 4;
            bins.TryGetValue(bin, out var c);
            bins[bin] = (c.Count + 1, c.R + r, c.G + g, c.B + b);
        }
        var best = bins.Values.MaxBy(c => c.Count);
        if (best.Count * 10 < pixels.Length * 6) return 0;
        return 0xFF000000u | (uint)(best.R / best.Count) << 16 | (uint)(best.G / best.Count) << 8 | (uint)(best.B / best.Count);
    }

    internal static int[]? CaptureScreen(int left, int top, int width, int height)
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        IntPtr memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            memory = CreateCompatibleDC(screen);
            if (memory == IntPtr.Zero) return null;
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) return null;
            previous = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, screen, left, top, 0x00CC0020)) return null; // SRCCOPY only
            var pixels = new int[width * height];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            return pixels;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant, Colors;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dc, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}
