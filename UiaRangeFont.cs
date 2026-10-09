using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Media;

namespace LayoutPreviewPrototype;

internal static class RangeFontExperiment
{
    private static int _enabled = 1, _revision;
    public static event Action<bool>? Changed;
    public static event Action<string>? Diagnostic;
    public static void Report(string value) => Diagnostic?.Invoke(value);
    public static bool Enabled => Volatile.Read(ref _enabled) != 0;
    public static int Revision => Volatile.Read(ref _revision);
    public static void SetEnabled(bool value)
    {
        if (Interlocked.Exchange(ref _enabled, value ? 1 : 0) != (value ? 1 : 0))
        { Interlocked.Increment(ref _revision); Changed?.Invoke(value); }
    }
}

internal sealed class UiaRangeFont
{
    private string _context = "";
    private int _revision = -1, _attempts;
    private long _lastAttempt;
    private double _stable, _lastMs;
    private bool _checkedReported, _correctedReported;
    public string Status { get; private set; } = "Оценка ещё не выполнялась";
    private string? _lastStatus;
    public event Action<string>? StatusChanged;

    // Reported attributes remain the baseline; long ranges may validate their size.
    internal static bool HasReportedFont(EditorSnapshot snapshot) =>
        double.IsFinite(snapshot.FontSizeDip) && snapshot.FontSizeDip is > 0 and <= 256 &&
        !string.IsNullOrWhiteSpace(snapshot.FontFamilyName) &&
        !PreviewFontResolver.Resolve(snapshot.FontFamilyName, snapshot.FontWeightValue, snapshot.FontItalic).Substituted &&
        PreviewMetrics.HasReliableFormatting(snapshot.FontSource) &&
        !snapshot.FontSource.StartsWith("UIA range-width estimate", StringComparison.Ordinal);

    // One early probe when the field first has two measurable characters;
    // subsequent failed attempts retain the 500 ms cooldown and six-attempt cap.
    internal bool NeedsInitialProbe(EditorSnapshot snapshot) => RangeFontExperiment.Enabled &&
        !_checkedReported && _attempts == 0 && _stable == 0 && snapshot.SelectionStart == snapshot.SelectionEnd &&
        snapshot.SelectionEnd >= (HasReportedFont(snapshot) ? 5 : 2) && snapshot.AutomationIdentity.Length > 0 &&
        !snapshot.FontSource.StartsWith("UIA omnibox", StringComparison.Ordinal);

    public EditorSnapshot Enrich(EditorSnapshot snapshot, TextPatternRange caret, int generation)
    {
        if (!RangeFontExperiment.Enabled) return snapshot;
        int sampleEnd = Math.Clamp(snapshot.SelectionEnd, 0, snapshot.Text.Length);
        string sample = snapshot.Text.Substring(Math.Max(0, sampleEnd - 8), Math.Min(8, sampleEnd));
        var fontChoice = PreviewFontResolver.Resolve(snapshot.FontFamilyName, snapshot.FontWeightValue, snapshot.FontItalic, sample);
        bool reported = HasReportedFont(snapshot) && !fontChoice.Substituted;
        // Retain the user's accepted omnibox fallback and exact native contract.
        if (snapshot.FontSource.StartsWith("UIA omnibox", StringComparison.Ordinal) || snapshot.AutomationIdentity.Length == 0) return snapshot;
        string context = $"{generation}:{snapshot.Foreground}:{snapshot.Editor}:{snapshot.AutomationIdentity}:{snapshot.Dpi}:" +
            $"{snapshot.Caret.Bottom-snapshot.Caret.Top}:{snapshot.FontFamilyName}:{snapshot.FontWeightValue}:{snapshot.FontItalic}:{snapshot.FontSizeDip}:{fontChoice.Used}:{fontChoice.Reason}";
        if (context != _context || _revision != RangeFontExperiment.Revision)
        {
            _context = context; _revision = RangeFontExperiment.Revision;
            _attempts = 0; _stable = 0; _lastAttempt = 0;
            _checkedReported = _correctedReported = false;
        }
        if (_stable > 0) return Apply(snapshot);
        if (reported && _checkedReported) return snapshot;
        long now = Environment.TickCount64;
        if (_attempts >= 6 || now - _lastAttempt < 500 || snapshot.SelectionStart != snapshot.SelectionEnd || snapshot.SelectionEnd < (reported ? 5 : 2))
            return snapshot;
        _lastAttempt = now;
        long started = Stopwatch.GetTimestamp();
        try
        {
            // Compare nested ranges in the same capture, rather than showing a
            // fallback and waiting for another temporal confirmation.
            _attempts++;
            var longer = Read(caret, Math.Min(8, snapshot.SelectionEnd));
            if (longer == null || longer.Value.Text.Length < (reported ? 5 : 2)) { SetStatus("Короткий видимый диапазон недоступен"); return snapshot; }
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds > 25)
            { SetStatus("Диапазон медленный; дополнительные запросы пропущены"); _attempts = 6; return snapshot; }
            var shorter = Read(caret, longer.Value.Text.Length == 2 ? 1 : 2);
            if (shorter == null) { SetStatus("Короткий контрольный диапазон недоступен"); return snapshot; }
            fontChoice = PreviewFontResolver.Resolve(snapshot.FontFamilyName, snapshot.FontWeightValue, snapshot.FontItalic, longer.Value.Text);
            double size = Estimate(longer.Value.Text, longer.Value.Bounds, shorter.Value.Text, shorter.Value.Bounds,
                snapshot.Dpi, fontChoice.Used, snapshot.FontWeightValue, snapshot.FontItalic);
            if (size == 0) { SetStatus("Границы диапазонов не подтверждают размер; сохранён fallback"); return snapshot; }
            if (reported)
            {
                if (Math.Abs(size - snapshot.FontSizeDip) < snapshot.FontSizeDip * 0.10)
                {
                    _checkedReported = true;
                    SetStatus($"reportedDip={snapshot.FontSizeDip:F2}; rangeDip={size:F2}; исходные метрики сохранены");
                    return snapshot;
                }
                // Require an additional suffix before replacing complete attributes.
                if (Stopwatch.GetElapsedTime(started).TotalMilliseconds > 25)
                { _attempts = 6; SetStatus("Проверка метрик медленная; исходный шрифт сохранён"); return snapshot; }
                var middle = Read(caret, 3);
                double check = middle == null ? 0 : Estimate(longer.Value.Text, longer.Value.Bounds,
                    middle.Value.Text, middle.Value.Bounds, snapshot.Dpi, fontChoice.Used,
                    snapshot.FontWeightValue, snapshot.FontItalic);
                if (!ShouldCorrectReported(snapshot.FontSizeDip, size, check))
                { SetStatus($"reportedDip={snapshot.FontSizeDip:F2}; rangeDip={size:F2}; checkDip={check:F2}; расхождение не подтверждено"); return snapshot; }
                size = (size + check) / 2;
                _correctedReported = true;
            }
            _stable = size;
            SetStatus($"reportedDip={snapshot.FontSizeDip:F2}; rangeDip={size:F2}; согласованы {(reported ? 3 : 2)} диапазона; family={(snapshot.FontFamilyName.Length == 0 ? "Segoe UI fallback" : snapshot.FontFamilyName)}");
            return Apply(snapshot);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException or ElementNotAvailableException)
        { SetStatus("Диапазон недоступен: " + e.GetType().Name); return snapshot; }
        finally
        {
            _lastMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            PerformanceDiagnostics.Observe("range-font", _lastMs);
            string report = $"поле={snapshot.Editor.ToInt64():X}; requestedFamily={fontChoice.Requested}; usedFamily={fontChoice.Used}; fallbackReason={fontChoice.Reason}; {Status}; attemptMs={_lastMs:F2}; попыток={_attempts}/6; cached={_stable > 0}";
            if (_lastStatus != report) { _lastStatus = report; StatusChanged?.Invoke(report); }
        }
    }

    internal static bool ShouldCorrectReported(double reported, double estimate, double check) =>
        double.IsFinite(reported) && reported > 0 && double.IsFinite(estimate) && estimate is >= 8 and <= 96 &&
        double.IsFinite(check) && check is >= 8 and <= 96 &&
        Math.Abs(estimate - check) <= estimate * 0.05 &&
        Math.Abs(estimate - reported) >= reported * 0.10 &&
        Math.Abs(check - reported) >= reported * 0.10 &&
        (estimate - reported) * (check - reported) > 0;

    private void SetStatus(string value) => Status = value;
    private EditorSnapshot Apply(EditorSnapshot snapshot) => snapshot with
    {
        FontSizeDip = _stable,
        FontSource = "UIA range-width estimate" + (_correctedReported ? $" (validated reportedDip={snapshot.FontSizeDip:F2})" : "") +
            (snapshot.FontFamilyName.Length == 0 ? " (family fallback)" : "")
    };

    private static (string Text, Rect Bounds)? Read(TextPatternRange caret, int length)
    {
        var range = caret.Clone();
        range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -length);
        string text = range.GetText(9);
        if (text.Length is < 1 or > 8 || text.Any(c => !char.IsLetterOrDigit(c) && c != ' ')) return null;
        var bounds = range.GetBoundingRectangles();
        return bounds.Length == 1 ? (text, bounds[0]) : null;
    }

    internal static double Width(string text, string family, int weight, bool italic, double size)
    {
        var choice = PreviewFontResolver.Resolve(family, weight, italic, text);
        var typeface = new Typeface(new FontFamily(choice.Used),
            italic ? FontStyles.Italic : FontStyles.Normal,
            FontWeight.FromOpenTypeWeight(weight is >= 100 and <= 900 ? weight : 400), FontStretches.Normal);
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, 1).WidthIncludingTrailingWhitespace;
    }

    internal static double Estimate(string longer, Rect large, string shorter, Rect small, uint dpi, string family, int weight, bool italic)
    {
        if (large.IsEmpty || small.IsEmpty ||
            !double.IsFinite(large.X) || !double.IsFinite(large.Y) || !double.IsFinite(large.Width) || !double.IsFinite(large.Height) ||
            !double.IsFinite(small.X) || !double.IsFinite(small.Y) || !double.IsFinite(small.Width) || !double.IsFinite(small.Height)) return 0;
        if (longer.Length <= shorter.Length || !longer.EndsWith(shorter, StringComparison.Ordinal) ||
            large.Width <= small.Width + 1 || Math.Abs(large.Right-small.Right) > 2 || Math.Abs(large.Top-small.Top) > 2 ||
            large.Height < 8 || Math.Abs(large.Height-small.Height) > 2 || large.Width > 600 || large.Height > 160) return 0;
        double scale = (dpi == 0 ? 96 : dpi) / 96.0;
        double a = large.Width / scale / Width(longer,family,weight,italic,16) * 16;
        double b = small.Width / scale / Width(shorter,family,weight,italic,16) * 16;
        if (!double.IsFinite(a) || !double.IsFinite(b) || a is < 8 or > 96 || Math.Abs(a-b) > a * 0.12 || a > large.Height/scale) return 0;
        return (a+b)/2;
    }
}
