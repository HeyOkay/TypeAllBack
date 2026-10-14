using System.IO;
using System.Text.Json;

namespace LayoutPreviewPrototype;

// Remembers the preview size for field fonts WPF cannot render (for example the web
// font of an Electron app). A measured scale then applies at once in every field and
// session using that font. Keys describe fonts and field classes, never typed text.
internal sealed class FontScaleMemory
{
    internal sealed class Entry
    {
        public double Value { get; set; }
        public int Samples { get; set; }
    }

    private const int MaxEntries = 128;
    private readonly string? _path;
    private readonly object _gate = new();
    private Dictionary<string, Entry>? _entries;

    internal FontScaleMemory(string? path) => _path = path;

    internal static FontScaleMemory Shared { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypeAllBack", "font-metrics.json"));

    public bool TryGet(string key, out double value)
    {
        lock (_gate)
        {
            value = Entries().TryGetValue(key, out var entry) ? entry.Value : 0;
            return value > 0;
        }
    }

    public int SampleCount(string key)
    {
        lock (_gate) return Entries().TryGetValue(key, out var entry) ? entry.Samples : 0;
    }

    // Running average over the last few measurements: one outlier cannot replace a learned size.
    public double Record(string key, double value)
    {
        if (!double.IsFinite(value) || value <= 0) return 0;
        lock (_gate)
        {
            var entries = Entries();
            if (!entries.TryGetValue(key, out var entry))
            {
                if (entries.Count >= MaxEntries) entries.Clear();
                entry = new Entry { Value = value, Samples = 1 };
                entries[key] = entry;
            }
            else
            {
                int weight = Math.Min(Math.Max(entry.Samples, 1), 4);
                entry.Value = (entry.Value * weight + value) / (weight + 1);
                entry.Samples = Math.Min(entry.Samples + 1, 1000);
            }
            Save(entries);
            return entry.Value;
        }
    }

    private Dictionary<string, Entry> Entries()
    {
        if (_entries != null) return _entries;
        _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        if (_path == null) return _entries;
        try
        {
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path));
            if (loaded != null)
                foreach (var pair in loaded)
                    if (pair.Value != null && double.IsFinite(pair.Value.Value) && pair.Value.Value > 0 && _entries.Count < MaxEntries)
                        _entries[pair.Key] = pair.Value;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        return _entries;
    }

    // Runs on the UIA worker only, and only when a measurement was recorded.
    private void Save(Dictionary<string, Entry> entries)
    {
        if (_path == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(_path + ".tmp", _path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Trace.WriteLine("Font metrics save failed: " + e.GetType().Name); }
    }
}
