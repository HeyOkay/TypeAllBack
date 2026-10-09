using System.Text.Json;
using System.IO;

namespace LayoutPreviewPrototype;

internal static class CorpusEvaluation
{
    public static void Run(string directory, string split)
    {
        var report = new List<object>();
        foreach (var (language, layout) in new[] { ("en", KeyboardLayout.English), ("ru", KeyboardLayout.Russian) })
        {
            var keys = Enumerable.Range(0x41, 26).Select(vk => (uint)vk)
                .Concat(new uint[] { 0xBA, 0xBC, 0xBE, 0xC0, 0xDB, 0xDD, 0xDE });
            var mapping = new Dictionary<char, char>();
            var opposite = layout == KeyboardLayout.English ? KeyboardLayout.Russian : KeyboardLayout.English;
            foreach (uint vk in keys)
            {
                KeyboardConversion.TryConvert(new(vk, false, false), layout, out char source);
                KeyboardConversion.TryConvert(new(vk, false, false), opposite, out char target);
                mapping[source] = target;
            }
            var training = Texts(Path.Combine(directory, language + "-train.conllu")).ToHashSet(StringComparer.Ordinal);
            var sentences = Texts(Path.Combine(directory, language + "-" + split + ".conllu"))
                .Where(text => !training.Contains(text)).Distinct(StringComparer.Ordinal).ToArray();
            foreach (int length in new[] { 3, 5, 10, 20, 160 })
            {
                int samples = 0, falsePositives = 0, detected = 0;
                foreach (string sentence in sentences)
                {
                    string clean = new(sentence.ToLowerInvariant().Select(c => char.IsLetter(c) ? c : ' ').ToArray());
                    if (clean.Any(c => char.IsLetter(c) && !mapping.ContainsKey(c))) continue;
                    clean = string.Join(" ", clean.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                    if (clean.Count(char.IsLetter) < 12 || clean.Length < length && length != 160) continue;
                    clean = clean[..Math.Min(length, clean.Length)];
                    string wrong = new(clean.Select(c => mapping.GetValueOrDefault(c, c)).ToArray());
                    samples++;
                    if (LanguagePlausibility.IsClearlyMorePlausible(clean, wrong, layout)) falsePositives++;
                    if (LanguagePlausibility.IsClearlyMorePlausible(wrong, clean, opposite)) detected++;
                }
                report.Add(new { language, prefixCharacters = length, samples, falsePositives, detected,
                    falsePositivePercent = samples == 0 ? 0 : Math.Round(100.0 * falsePositives / samples, 2),
                    detectionPercent = samples == 0 ? 0 : Math.Round(100.0 * detected / samples, 2) });
                if (length == 160 && (samples < 100 || falsePositives > samples * 0.01 || detected < samples * 0.95))
                    throw new InvalidOperationException($"Corpus regression for {language}: insufficient coverage or accuracy");
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IEnumerable<string> Texts(string file) => File.ReadLines(file)
        .Where(line => line.StartsWith("# text = ", StringComparison.Ordinal)).Select(line => line[9..]);
}
