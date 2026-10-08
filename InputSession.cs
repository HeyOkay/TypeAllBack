using System.Security.Cryptography;
using System.Text;
namespace LayoutPreviewPrototype;
internal sealed class TypedSegment(KeyboardLayout layout, int start)
{
    public KeyboardLayout Layout { get; } = layout;
    public int Start { get; } = start;
    public string Source { get; internal set; } = "";
    public string Alternative { get; internal set; } = "";
    public bool IsValid { get; internal set; } = true;
    public string InputSource => "PhysicalKeyboard";
}
// No document history is retained: only session text and a document fingerprint.
internal sealed class InputSession
{
    private readonly List<TypedSegment> _segments = [];
    public IReadOnlyList<TypedSegment> Segments => _segments;
    public int Start { get; }
    public KeyboardLayout SourceLayout { get; }
    public KeyboardLayout TargetLayout => SourceLayout == KeyboardLayout.English ? KeyboardLayout.Russian : KeyboardLayout.English;
    public string Source => string.Concat(_segments.Select(s => s.Source));
    public string Replacement => string.Concat(_segments.Select(s => s.Layout == SourceLayout ? s.Alternative : s.Source));
    public int End => Start + Source.Length;
    private string _expectedFingerprint;
    public InputSession(string document, int caret, KeyboardLayout layout)
    { Start = caret; SourceLayout = layout; _expectedFingerprint = Fingerprint(document); }
    public bool Matches(string document, int start, int end) => start == end && end == End &&
        Fingerprint(document) == _expectedFingerprint && End <= document.Length &&
        document.AsSpan(Start, Source.Length).SequenceEqual(Source.AsSpan());
    public void Append(PhysicalKey key, KeyboardLayout layout, string document)
    {
        if (!KeyboardConversion.TryConvert(key, layout, out var source) ||
            !KeyboardConversion.TryConvert(key, layout == KeyboardLayout.English ? KeyboardLayout.Russian : KeyboardLayout.English, out var alternative))
            throw new ArgumentException("Unsupported physical key");
        int oldEnd = End;
        if (_segments.Count == 0 || _segments[^1].Layout != layout) _segments.Add(new TypedSegment(layout, End));
        _segments[^1].Source += source; _segments[^1].Alternative += alternative;
        _expectedFingerprint = Fingerprint(document.Insert(oldEnd, source.ToString()));
    }
    public bool Backspace(string document)
    {
        if (_segments.Count == 0) return false;
        int oldEnd = End; var last = _segments[^1];
        last.Source = last.Source[..^1]; last.Alternative = last.Alternative[..^1];
        if (last.Source.Length == 0) _segments.RemoveAt(_segments.Count - 1);
        _expectedFingerprint = Fingerprint(document.Remove(oldEnd - 1, 1)); return true;
    }
    public string Preview
    {
        get
        {
            if (_segments.Count == 0 || _segments[^1].Layout != SourceLayout) return "";
            // Punctuation is already converted. Only whitespace separates tokens.
            var value = _segments[^1].Alternative.TrimEnd(); int i = value.Length - 1;
            while (i >= 0 && !char.IsWhiteSpace(value[i])) i--;
            var word = value[(i + 1)..];
            return word.Any(char.IsLetter) && Replacement != Source ? word : "";
        }
    }
    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(value)));
}