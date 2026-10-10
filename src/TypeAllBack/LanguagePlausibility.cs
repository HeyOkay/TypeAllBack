using System.Text.Json;

namespace LayoutPreviewPrototype;

// Corpus-derived character Markov models; no words, user history or network access.
internal static class LanguagePlausibility
{
    private static readonly Lazy<CharacterModel> English = new(() => Load("en"));
    private static readonly Lazy<CharacterModel> Russian = new(() => Load("ru"));

    public static bool IsClearlyMorePlausible(string source, string converted, KeyboardLayout sourceLayout)
    {
        var original = Evaluate(source, sourceLayout);
        var alternative = Evaluate(converted, sourceLayout == KeyboardLayout.English ? KeyboardLayout.Russian : KeyboardLayout.English);
        if (original.Characters < 3 || alternative.Characters < 3) return false;
        return alternative.Average >= -3.7 && alternative.Average - original.Average >= 1.0;
    }

    internal static (double Average, int Characters) Evaluate(string text, KeyboardLayout layout)
    {
        CharacterModel model = layout == KeyboardLayout.English ? English.Value : Russian.Value;
        string context = "^^";
        double sum = 0;
        int letters = 0;
        foreach (char raw in text)
        {
            char c = char.ToLowerInvariant(raw);
            // Boundaries reset context; no artificial n-grams between adjacent words.
            if (!char.IsLetter(c)) { context = "^^"; continue; }
            letters++;
            if (!model.Alphabet.Contains(c))
            {
                sum += Math.Log(1e-7);
                context = "^^";
                continue;
            }

            string character = c.ToString();
            double unigram = (model.Counts.GetValueOrDefault(character) + 0.5) /
                (model.Total + 0.5 * model.Alphabet.Length);
            string previous = context[1..];
            double bigram = (model.Counts.GetValueOrDefault(previous + character) + 2 * unigram) /
                (model.Contexts.GetValueOrDefault(previous) + 2.0);
            double trigram = (model.Counts.GetValueOrDefault(context + character) + 4 * bigram) /
                (model.Contexts.GetValueOrDefault(context) + 4.0);
            sum += Math.Log(trigram);
            context = previous + character;
        }
        return (letters == 0 ? double.NegativeInfinity : sum / letters, letters);
    }

    private static CharacterModel Load(string language)
    {
        using var stream = typeof(LanguagePlausibility).Assembly.GetManifestResourceStream($"LayoutPreview.Models.{language}.json")
            ?? throw new InvalidOperationException("Embedded language model is missing");
        return JsonSerializer.Deserialize<CharacterModel>(stream)
            ?? throw new InvalidOperationException("Embedded language model is invalid");
    }

    private sealed class CharacterModel
    {
        public string Alphabet { get; set; } = "";
        public int Total { get; set; }
        public Dictionary<string, int> Counts { get; set; } = [];
        public Dictionary<string, int> Contexts { get; set; } = [];
    }
}
