using System.Security.Cryptography;
using System.Text;
namespace LayoutPreviewPrototype;
internal readonly record struct QueuedInput(uint VirtualKey, bool Shift, bool CapsLock, KeyboardLayout Layout);
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
    private readonly string _baselineFingerprint;
    private readonly int _baselineLength;
    private string? _cachedPreview;
    public InputSession(string document, int caret, KeyboardLayout layout)
    { Start = caret; SourceLayout = layout; _baselineFingerprint = Fingerprint(document); _baselineLength = document.Length; _expectedFingerprint = _baselineFingerprint; }
    private InputSession(InputSession source)
    {
        Start = source.Start; SourceLayout = source.SourceLayout;
        _baselineFingerprint = source._baselineFingerprint; _baselineLength = source._baselineLength;
        _expectedFingerprint = source._expectedFingerprint;
        foreach (var segment in source._segments)
            _segments.Add(new TypedSegment(segment.Layout, segment.Start)
            { Source = segment.Source, Alternative = segment.Alternative, IsValid = segment.IsValid });
    }
    public InputSession Copy() => new(this);
    public static InputSession FromSelectionReplacement(string document, int selectionStart, int selectionEnd,
        PhysicalKey key, KeyboardLayout layout)
    {
        if (selectionStart < 0 || selectionEnd <= selectionStart || selectionEnd > document.Length)
            throw new ArgumentOutOfRangeException(nameof(selectionEnd));
        // Native typing removes the selection before inserting this key. Predict
        // precisely that edit, retaining all unselected document text as baseline.
        string baseline = document.Remove(selectionStart, selectionEnd - selectionStart);
        var session = new InputSession(baseline, selectionStart, layout);
        session.Append(key, layout, baseline);
        return session;
    }
    public bool TryRebaseUnconfirmedStartup(string document, int selectionStart, int selectionEnd, out InputSession rebased)
    {
        rebased = this;
        // Only the first unconfirmed delivery may replace provider placeholder
        // content. The current document must consist entirely of captured input;
        // no preexisting characters can enter the new session range.
        if (Start != _baselineLength || Source.Length == 0 || document != Source ||
            selectionStart != document.Length || selectionEnd != selectionStart) return false;
        var candidate = new InputSession("", 0, SourceLayout);
        foreach (var segment in _segments)
            candidate._segments.Add(new TypedSegment(segment.Layout, segment.Start - Start)
            { Source = segment.Source, Alternative = segment.Alternative, IsValid = segment.IsValid });
        if (!candidate.Reconcile(document, selectionStart, selectionEnd)) return false;
        rebased = candidate;
        return true;
    }
    public bool MatchesDocument(string document) =>
        Fingerprint(document) == _expectedFingerprint && End <= document.Length &&
        document.AsSpan(Start, Source.Length).SequenceEqual(Source.AsSpan());
    public bool Matches(string document, int start, int end) => start == end && end == End && MatchesDocument(document);
    public bool MatchesWithExternalSelection(string document, int start, int end) =>
        start == End && end > start && end <= document.Length &&
        MatchesDocument(document.Remove(start, end - start));
    public bool TryAppendOverExternalSelection(string document, int start, int end, PhysicalKey key, KeyboardLayout layout)
    {
        // An omnibox can select its generated completion immediately after our
        // typed prefix. Its removal must recover the exact predicted document.
        if (!MatchesWithExternalSelection(document, start, end)) return false;
        Append(key, layout, document.Remove(start, end - start));
        return true;
    }
    public void Append(PhysicalKey key, KeyboardLayout layout, string document)
    {
        _cachedPreview = null;
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
        _cachedPreview = null;
        if (_segments.Count == 0) return false;
        int oldEnd = End; var last = _segments[^1];
        last.Source = last.Source[..^1]; last.Alternative = last.Alternative[..^1];
        if (last.Source.Length == 0) _segments.RemoveAt(_segments.Count - 1);
        _expectedFingerprint = Fingerprint(document.Remove(oldEnd - 1, 1)); return true;
    }

    public void ReplayPending(IReadOnlyList<QueuedInput> inputs)
    {
        _cachedPreview = null;
        foreach (var input in inputs)
        {
            if (input.VirtualKey == 0x08)
            {
                RemoveLastTypedCharacter();
                continue;
            }

            var key = new PhysicalKey(input.VirtualKey, input.Shift, input.CapsLock);
            if (!KeyboardConversion.TryConvert(key, input.Layout, out var source) ||
                !KeyboardConversion.TryConvert(key, input.Layout == KeyboardLayout.English ? KeyboardLayout.Russian : KeyboardLayout.English, out var alternative))
                throw new ArgumentException("Pending input contains an unsupported key", nameof(inputs));
            if (_segments.Count == 0 || _segments[^1].Layout != input.Layout)
                _segments.Add(new TypedSegment(input.Layout, End));
            _segments[^1].Source += source;
            _segments[^1].Alternative += alternative;
        }
    }

    public bool Reconcile(string document, int selectionStart, int selectionEnd)
    {
        if (selectionStart != selectionEnd || selectionEnd != End ||
            document.Length != _baselineLength + Source.Length || Start > document.Length ||
            End > document.Length || !document.AsSpan(Start, Source.Length).SequenceEqual(Source.AsSpan()))
            return false;

        var baseline = document.Remove(Start, Source.Length);
        if (Fingerprint(baseline) != _baselineFingerprint) return false;
        _expectedFingerprint = Fingerprint(document);
        return true;
    }

    private void RemoveLastTypedCharacter()
    {
        if (_segments.Count == 0) return;
        var last = _segments[^1];
        last.Source = last.Source[..^1];
        last.Alternative = last.Alternative[..^1];
        if (last.Source.Length == 0) _segments.RemoveAt(_segments.Count - 1);
    }
    public string Preview => _cachedPreview ??= CalculatePreview();
    private string CalculatePreview()
    {
            var segment = _segments.LastOrDefault(s => s.Layout == SourceLayout && s.Alternative.Any(char.IsLetter));
            if (segment == null) return "";
            // Punctuation is already converted. Only whitespace separates tokens.
            var value = segment.Alternative.TrimEnd(); int i = value.Length - 1;
            while (i >= 0 && !char.IsWhiteSpace(value[i])) i--;
            var word = value[(i + 1)..];
            var sourceSessionText = string.Join(" ", _segments.Where(s => s.Layout == SourceLayout).Select(s => s.Source));
            var alternativeSessionText = string.Join(" ", _segments.Where(s => s.Layout == SourceLayout).Select(s => s.Alternative));
            return word.Any(char.IsLetter) && Replacement != Source &&
                LanguagePlausibility.IsClearlyMorePlausible(sourceSessionText, alternativeSessionText, SourceLayout) ? word : "";
    }
    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.Unicode.GetBytes(value)));
}
