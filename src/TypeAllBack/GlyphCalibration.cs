namespace LayoutPreviewPrototype;

// How a field font that WPF cannot render (Discord's gg sans) actually looks: where its
// baseline sits inside the caret rectangle and how heavy its strokes are relative to its
// x-height. Measured from a strip of the user's own typed text just left of the caret, at
// most a few times per font ever (results are remembered in FontScaleMemory), on the UIA
// worker after a snapshot was published and only while no preview is on screen.
// Plain SRCCOPY without CAPTUREBLT; the pixels are discarded after the measurement.
internal static class GlyphCalibration
{
    private const int SamplesPerFont = 3, IntervalMs = 1500;
    private static long _lastAt;
    public static event Action? Changed;

    // Only substituted families: an available font is already rendered exactly. The caret
    // height is part of the key: the same reported family in a field with another line
    // height (VK vs Google settings, both "Roboto") is another measurement, not a mix.
    internal static string? Key(EditorSnapshot s)
    {
        string family = s.FontFamilyName.Trim();
        int height = s.Caret.Bottom - s.Caret.Top;
        if (s.AutomationIdentity.Length == 0 || family.Length == 0 || height is < 8 or > 200 ||
            s.FontSource.StartsWith("UIA omnibox", StringComparison.Ordinal)) return null;
        if (!PreviewFontResolver.Resolve(family, s.FontWeightValue, s.FontItalic).Substituted) return null;
        return $"{family.ToLowerInvariant()}|{s.FontWeightValue}|{s.FontItalic}|{height}";
    }

    // Ratios of the caret height (baseline, x-height) and of the x-height (stem).
    public static bool TryGet(EditorSnapshot s, out double baselineRatio, out double stemToXHeight, out double xHeightRatio)
    {
        baselineRatio = stemToXHeight = xHeightRatio = 0;
        if (Key(s) is not { } key ||
            !FontScaleMemory.Shared.TryGet("baseline|" + key, out baselineRatio) ||
            !FontScaleMemory.Shared.TryGet("stem|" + key, out stemToXHeight)) return false;
        FontScaleMemory.Shared.TryGet("xheight|" + key, out xHeightRatio);
        return true;
    }

    // UIA worker only.
    public static void Refresh(EditorSnapshot s, bool previewVisible)
    {
        if (previewVisible || Key(s) is not { } key) return;
        if (FontScaleMemory.Shared.SampleCount("xheight|" + key) >= SamplesPerFont) return;
        long now = Environment.TickCount64;
        if (now - _lastAt < IntervalMs) return;
        int height = s.Caret.Bottom - s.Caret.Top;
        if (height is < 8 or > 200 || s.SelectionStart != s.SelectionEnd) return;
        // The strip (two line heights) must hold only letters of the current word.
        int end = Math.Clamp(s.SelectionEnd, 0, s.Text.Length), letters = 0;
        while (letters < end && char.IsLetter(s.Text[end - 1 - letters])) letters++;
        if (letters < 4) return;
        uint background = FieldColors.Get(s);
        if (background == 0) return;
        _lastAt = now;
        int width = Math.Min(height * 2, 160);
        var pixels = FieldColors.CaptureScreen(s.Caret.Left - width, s.Caret.Top, width, height);
        if (pixels == null || !Measure(pixels, width, height, background, out double baseline, out double stem, out int xHeight)) return;
        FontScaleMemory.Shared.Record("baseline|" + key, baseline / height);
        FontScaleMemory.Shared.Record("stem|" + key, stem);
        FontScaleMemory.Shared.Record("xheight|" + key, (double)xHeight / height);
        Changed?.Invoke();
    }

    // Ink coverage runs along the background→text color line; pixels far from it (a red
    // spell-check underline, a selection) are not text. The dense x-height band gives the
    // baseline (its lower edge) and the x-height; the median antialiased mass of ink runs
    // inside the band gives the stem width.
    internal static bool Measure(int[] pixels, int width, int height, uint background,
        out double baseline, out double stemToXHeight, out int xHeightPixels)
    {
        baseline = stemToXHeight = 0;
        xHeightPixels = 0;
        if (width <= 0 || height <= 0 || pixels.Length != width * height) return false;
        static double Lum(int r, int g, int b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
        int br = (int)(background >> 16 & 0xFF), bg = (int)(background >> 8 & 0xFF), bb = (int)(background & 0xFF);
        double backLum = Lum(br, bg, bb);
        int text = pixels[0];
        double best = -1;
        foreach (int p in pixels)
        {
            double contrast = Math.Abs(Lum(p >> 16 & 0xFF, p >> 8 & 0xFF, p & 0xFF) - backLum);
            if (contrast > best) { best = contrast; text = p; }
        }
        int fr = text >> 16 & 0xFF, fg = text >> 8 & 0xFF, fb = text & 0xFF;
        double span = Lum(fr, fg, fb) - backLum;
        if (Math.Abs(span) < 40) return false;

        var coverage = new double[pixels.Length];
        var rows = new double[height];
        for (int i = 0; i < pixels.Length; i++)
        {
            int p = pixels[i], r = p >> 16 & 0xFF, g = p >> 8 & 0xFF, b = p & 0xFF;
            double c = Math.Clamp((Lum(r, g, b) - backLum) / span, 0, 1);
            double er = r - (br + c * (fr - br)), eg = g - (bg + c * (fg - bg)), eb = b - (bb + c * (fb - bb));
            if (Math.Sqrt(er * er + eg * eg + eb * eb) > 90) c = 0;
            coverage[i] = c;
            rows[i / width] += c;
        }
        double max = rows.Max();
        if (max < 2) return false;
        int top = -1, bottom = -1;
        for (int y = 0; y < height; y++)
            if (rows[y] >= max * 0.5) { if (top < 0) top = y; bottom = y; }
        int xHeight = bottom - top + 1;
        if (top < 0 || xHeight < 4 || xHeight > height * 0.8) return false;

        var masses = new List<double>();
        for (int y = top + xHeight / 4; y <= bottom - xHeight / 4; y++)
        {
            double mass = 0, peak = 0;
            for (int x = 0; x <= width; x++)
            {
                double c = x < width ? coverage[y * width + x] : 0;
                if (c > 0.15) { mass += c; peak = Math.Max(peak, c); continue; }
                if (mass > 0 && peak > 0.6 && mass < xHeight) masses.Add(mass);
                mass = peak = 0;
            }
        }
        if (masses.Count < 3) return false;
        masses.Sort();
        baseline = bottom + 1;
        xHeightPixels = xHeight;
        stemToXHeight = masses[masses.Count / 2] / xHeight;
        return stemToXHeight is > 0.05 and < 0.6;
    }
}
