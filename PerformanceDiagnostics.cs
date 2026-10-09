using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace LayoutPreviewPrototype;

// Opt-in aggregate durations only. Never retain field contents, titles or keys.
internal static class PerformanceDiagnostics
{
    private sealed class Counter { public long Count, Over16, Over50; public double Total, Max; }
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Counter> Counters = [];
    private static string? _path;
    private static long _lastFlush;
    public static bool Enabled => _path != null;
    public static void Configure(string path) { _path = Path.GetFullPath(path); _lastFlush = Environment.TickCount64; }
    public static void Observe(string stage, double milliseconds)
    {
        if (!Enabled) return;
        lock (Gate)
        {
            if (!Counters.TryGetValue(stage, out var counter)) Counters[stage] = counter = new();
            counter.Count++; counter.Total += milliseconds; counter.Max = Math.Max(counter.Max, milliseconds);
            if (milliseconds > 16) counter.Over16++;
            if (milliseconds > 50) counter.Over50++;
        }
    }
    public static Scope Measure(string stage) => new(stage, Enabled ? Stopwatch.GetTimestamp() : 0);
    public readonly struct Scope(string stage, long started) : IDisposable
    {
        public void Dispose() { if (started != 0) Observe(stage, Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    }
    // Called only by the worker; no disk writes on the keyboard or WPF thread.
    public static void Flush()
    {
        if (_path == null || Environment.TickCount64 - _lastFlush < 10000) return;
        _lastFlush = Environment.TickCount64;
        List<string> lines = [];
        lock (Gate)
        {
            foreach (var (stage, c) in Counters)
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{DateTime.UtcNow:O} stage={stage} count={c.Count} meanMs={c.Total / c.Count:F3} maxMs={c.Max:F3} over16={c.Over16} over50={c.Over50}"));
            Counters.Clear();
        }
        try { File.AppendAllLines(_path, lines); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _path = null; }
    }
}
