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
    private static int _debugVisible;
    private static double _lastCpuMs;
    public static void SetDebugVisible(bool visible) => Volatile.Write(ref _debugVisible, visible ? 1 : 0);
    public static bool Enabled => _path != null;
    public static void Configure(string path)
    {
        _path = Path.GetFullPath(path); _lastFlush = Environment.TickCount64;
        using var process = Process.GetCurrentProcess();
        _lastCpuMs = process.TotalProcessorTime.TotalMilliseconds;
    }
    public static void Observe(string stage, double milliseconds)
    {
        if (!Enabled) return;
        stage = (Volatile.Read(ref _debugVisible) != 0 ? "visible:" : "hidden:") + stage;
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
        long now = Environment.TickCount64;
        long interval = now - _lastFlush;
        _lastFlush = now;
        List<string> lines = [];
        lock (Gate)
        {
            foreach (var (stage, c) in Counters)
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{DateTime.UtcNow:O} stage={stage} count={c.Count} meanMs={c.Total / c.Count:F3} maxMs={c.Max:F3} over16={c.Over16} over50={c.Over50}"));
            Counters.Clear();
        }
        // Process and GC data are observed only by the worker, without forcing GC.
        using (var process = Process.GetCurrentProcess())
        {
            double cpuMs = process.TotalProcessorTime.TotalMilliseconds;
            double deltaCpuMs = Math.Max(0, cpuMs - _lastCpuMs); _lastCpuMs = cpuMs;
            var memory = GC.GetGCMemoryInfo();
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{DateTime.UtcNow:O} stage=process debugVisible={Volatile.Read(ref _debugVisible)} intervalMs={interval} cpuMs={deltaCpuMs:F3} cpuOneCorePercent={100 * deltaCpuMs / Math.Max(1, interval):F3} logicalProcessors={Environment.ProcessorCount} workingSetMiB={process.WorkingSet64 / 1048576.0:F3} privateMiB={process.PrivateMemorySize64 / 1048576.0:F3} managedMiB={GC.GetTotalMemory(false) / 1048576.0:F3} heapMiB={memory.HeapSizeBytes / 1048576.0:F3} allocatedMiB={GC.GetTotalAllocatedBytes(false) / 1048576.0:F3} gc0={GC.CollectionCount(0)} gc1={GC.CollectionCount(1)} gc2={GC.CollectionCount(2)}"));
        }
        try { File.AppendAllLines(_path, lines); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _path = null; }
    }
}
