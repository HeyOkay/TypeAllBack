using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Text;

namespace LayoutPreviewPrototype;

internal sealed record PreviewFontChoice(string Requested, string Used, string Reason, bool Composite)
{
    public bool Substituted => Reason.Length > 0;
}

internal static class PreviewFontResolver
{
    // Both conversion directions use this repertoire. Cache typefaces, never typed text.
    private const string Repertoire = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyzАБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя";
    private sealed record Face(string Family, GlyphTypeface? Glyph, bool Composite);
    private static readonly Lazy<Dictionary<string, string>> Families = new(() =>
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in Fonts.SystemFontFamilies)
        {
            names.TryAdd(family.Source, family.Source);
            foreach (string alias in family.FamilyNames.Values) names.TryAdd(alias, family.Source);
        }
        return names;
    });
    private static readonly ConcurrentDictionary<(string, int, bool), Face> Faces = new();

    // Common open web fonts shipped with the app (Fonts/, OFL 1.1, see Fonts/README.md),
    // so a field in Roboto, Open Sans, Inter... gets its own font instead of Segoe UI.
    // An installed font of the same name always wins. Fonts with a Reserved Font Name
    // that had to be instanced from a variable font carry a neutral internal name.
    private static readonly Dictionary<string, string> BundledAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["IBM Plex Sans"] = "TAB Sans A", ["Raleway"] = "TAB Sans B",
        ["Source Sans 3"] = "TAB Sans C", ["Source Sans Pro"] = "TAB Sans C",
        ["Source Code Pro"] = "TAB Mono C",
        // CSS names sites give the same fonts.
        ["Inter var"] = "Inter", ["InterVariable"] = "Inter", ["Inter Variable"] = "Inter",
        ["Open Sans Variable"] = "Open Sans", ["Roboto Variable"] = "Roboto",
    };
    private static readonly Lazy<Dictionary<string, FontFamily>> Bundled = new(() =>
    {
        var map = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var baseUri = new Uri("pack://application:,,,/TypeAllBack;component/");
            foreach (var family in Fonts.GetFontFamilies(baseUri, "./Fonts/"))
                foreach (string name in family.FamilyNames.Values)
                    map.TryAdd(name, family);
        }
        // Without the app's resources (unit tests, a stripped build) Segoe UI stays the fallback.
        catch (Exception e) when (e is UriFormatException or System.IO.IOException or NotSupportedException or InvalidOperationException or ArgumentException) { }
        return map;
    });

    // The family the preview renders a resolved name with: an installed one, else a shipped one.
    public static FontFamily Family(string used)
    {
        if (!Families.Value.ContainsKey(used) && Bundled.Value.TryGetValue(used, out var bundled)) return bundled;
        return new FontFamily(used);
    }

    internal static bool IsBundled(string family) =>
        Bundled.Value.ContainsKey(BundledAliases.TryGetValue(family.Trim(), out string? alias) ? alias : family.Trim());

    private static Face Find(string name, int weight, bool italic)
    {
        if (Faces.Count > 256) Faces.Clear();
        return Faces.GetOrAdd((name, weight, italic), key =>
        {
            string? installed = null;
            FontFamily? family = null;
            if (Families.Value.TryGetValue(key.Item1, out string? system)) { installed = system; }
            else
            {
                string name = BundledAliases.TryGetValue(key.Item1, out string? alias) ? alias : key.Item1;
                if (Bundled.Value.TryGetValue(name, out var shipped)) { installed = name; family = shipped; }
            }
            if (installed == null) return new("", null, false);
            try
            {
                family ??= new FontFamily(installed);
                var typeface = new Typeface(family, key.Item3 ? FontStyles.Italic : FontStyles.Normal,
                    FontWeight.FromOpenTypeWeight(key.Item2 is >= 100 and <= 900 ? key.Item2 : 400), FontStretches.Normal);
                if (typeface.TryGetGlyphTypeface(out var glyph)) return new(installed, glyph, false);
                // A registered composite family legitimately has no single font file.
                if (family.FamilyMaps.Count > 0) return new(installed, null, true);
            }
            catch (Exception e) when (e is ArgumentException or FormatException or System.IO.IOException or NotSupportedException) { }
            return new("", null, false);
        });
    }

    private static bool Covers(Face face, string text)
    {
        if (face.Composite) return true; // WPF resolves its character maps per run.
        if (face.Glyph == null) return false;
        foreach (var rune in (Repertoire + text).EnumerateRunes())
            if (!Rune.IsWhiteSpace(rune) && !Rune.IsControl(rune) &&
                (!face.Glyph.CharacterToGlyphMap.TryGetValue(rune.Value, out ushort glyph) || glyph == 0)) return false;
        return true;
    }

    public static PreviewFontChoice Resolve(string requested, int weight, bool italic, string text = "")
    {
        var face = Find(requested.Trim(), weight, italic);
        if (Covers(face, text)) return new(requested, face.Family, "", face.Composite);
        string reason = requested.Length == 0 ? "family-not-reported" : face.Family.Length == 0 ? "family-unavailable" : "glyphs-unavailable";
        var fallback = Find("Segoe UI", weight, italic);
        if (Covers(fallback, text)) return new(requested, fallback.Family, reason, fallback.Composite);
        // Preserve composite fallback for symbols not covered by one physical font.
        return new(requested, "Global User Interface", reason + "; composite-fallback", true);
    }
}
