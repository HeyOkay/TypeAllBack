namespace LayoutPreviewPrototype;

internal static class PreviewMetrics
{
    // Omnibox attributes may be inflated; do not let "metadata present" bypass
    // the physical-glyph limit. Native and other glyph-local formatting stay exact.
    public static bool HasReliableFormatting(string source) => source.Length > 0 && !source.StartsWith("UIA omnibox", StringComparison.Ordinal);
    // Chromium's ordinary desktop omnibox desired font size is 14 logical pixels.
    // This is an explicit fallback, not measured font metadata; touch/custom UI
    // settings may differ. Do not infer em size from the padded caret/line box.
    public const double OmniboxDefaultSizeDip = 14;
    public static bool NeedsOmniboxFallback(string source, double reportedSizeDip) =>
        source.StartsWith("UIA omnibox", StringComparison.Ordinal) &&
        (!double.IsFinite(reportedSizeDip) || reportedSizeDip <= 0);

    public static double ResolveFontSize(double reportedSizeDip, int caretHeightPixels, uint dpi,
        double previousSizeDip, bool firstAppearance, bool reliableFormatting = false, string fontSource = "")
    {
        double lineHeightDip = Math.Max(0, caretHeightPixels) * 96.0 / (dpi == 0 ? 96 : dpi);
        bool validLineHeight = lineHeightDip is >= 8 and <= 160;
        bool validPrevious = double.IsFinite(previousSizeDip) && previousSizeDip >= 8;
        bool validReported = double.IsFinite(reportedSizeDip) && reportedSizeDip > 0;
        double lineLimit = validLineHeight ? Math.Clamp(lineHeightDip * 0.8, 8, 96) : 16;
        if (NeedsOmniboxFallback(fontSource, reportedSizeDip))
            return validLineHeight ? Math.Min(OmniboxDefaultSizeDip, lineLimit) : OmniboxDefaultSizeDip;

        // A provider can briefly return a paragraph-sized rectangle without a
        // corresponding font change. Keep the established size in that case.
        if (!firstAppearance && validPrevious &&
            (!validLineHeight || lineHeightDip > previousSizeDip * 2.2) &&
            (!validReported || reportedSizeDip <= previousSizeDip * 1.5))
            return Math.Clamp(validReported ? Math.Min(previousSizeDip, reportedSizeDip) : previousSizeDip, 8, 96);

        if (validReported)
        {
            if (reliableFormatting && (!validLineHeight || reportedSizeDip <= lineHeightDip * 1.6))
                return Math.Clamp(reportedSizeDip, 1, 256);
            return Math.Clamp(Math.Min(reportedSizeDip, lineLimit), 8, 96);
        }
        if (!firstAppearance && validPrevious)
            return Math.Clamp(previousSizeDip, 8, 96);
        return Math.Clamp(lineLimit, 8, 40);
    }

    // Wait only before the first visible preview; typing and session tracking continue.
    public static bool ShouldWaitForFont(string source, bool hasAppeared, long now, ref long waitStarted)
    {
        if (hasAppeared || !source.Contains("; range=pending", StringComparison.Ordinal)) return false;
        if (waitStarted < 0) waitStarted = now;
        return now - waitStarted < 1100;
    }

    public static double OverlayScaleRatio(uint sourceDpi, double overlayScale) =>
        (sourceDpi == 0 ? 1 : sourceDpi / 96.0) /
        (double.IsFinite(overlayScale) && overlayScale > 0 ? overlayScale : 1);
}
