using System.IO;
using System.Text.Json;

namespace LayoutPreviewPrototype;

internal sealed class TraySettings
{
    public bool Enabled { get; set; } = true;
    public bool AutoEnter { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;
    public string UpdateFeedUrl { get; set; } = "";
    internal static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypeAllBack", "settings.json");
    internal static TraySettings Load()
    {
        try { return JsonSerializer.Deserialize<TraySettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    internal void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { System.Diagnostics.Trace.WriteLine("Settings save failed: " + e.GetType().Name); }
    }
}
