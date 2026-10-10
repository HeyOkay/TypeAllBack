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

    private static Face Find(string name, int weight, bool italic)
    {
        if (Faces.Count > 256) Faces.Clear();
        return Faces.GetOrAdd((name, weight, italic), key =>
        {
            if (!Families.Value.TryGetValue(key.Item1, out string? installed)) return new("", null, false);
            try
            {
                var family = new FontFamily(installed);
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
