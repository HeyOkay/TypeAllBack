namespace LayoutPreviewPrototype;

// In-memory session text only. Never route these entries through Trace/disk.
internal sealed class SessionInputLog(SessionDiagnostics diagnostics)
{
    private long _lastSequence;
    private string _lastSymbol = "—";
    private string? _hintSource, _hint;
    private string? _lastConfirmed;
    public void Start()
    { _lastSequence = 0; _lastSymbol = "—"; _hintSource = _hint = _lastConfirmed = null; }

    public void Captured(long sequence, string symbol, string expected, string confirmed)
    {
        _lastSequence = sequence; _lastSymbol = symbol;
        diagnostics.Event("Ввод", $"t{sequence} «{symbol}» → {Visible(expected)} [{HintFor(expected)}]; " +
            (expected == confirmed ? "текст подтверждён" : "ожидается подтверждение редактором"));
    }

    public void Confirmed(string text)
    {
        if (_lastConfirmed == text) return;
        _lastConfirmed = text;
        diagnostics.Event("Подтверждение", $"текст через t{_lastSequence}: {Visible(text)} [{HintFor(text)}]");
    }

    public void Calculated(string source, string hint)
    {
        if (_hintSource == source && _hint == hint) return;
        _hintSource = source; _hint = hint;
        diagnostics.Event("Подсказка", $"после t{_lastSequence}: {Visible(source)} [{HintFor(source)}]");
    }

    private string HintFor(string source) => _hintSource != source ? "ещё не рассчитана для этого текста" :
        string.IsNullOrEmpty(_hint) ? "нет подсказки" : Visible(_hint);

    public string EndDescription(SessionDiagnosticFrame? frame) => frame == null ? "" :
        $"последний отслеженный ввод: t{_lastSequence} «{_lastSymbol}»; подтверждено: {Visible(frame.Confirmed)}; " +
        $"ожидалось: {Visible(frame.Expected)} [{HintFor(frame.Expected)}]";

    public static string Visible(string text) => text.Replace("\r\n", "↵").Replace("\r", "↵")
        .Replace("\n", "↵").Replace("\t", "⇥").Replace(" ", "␠");

    public static string Describe(PhysicalKey key, KeyboardLayout layout) => key.VirtualKey switch
    {
        Native.VK_BACK => "⌫ Backspace", Native.VK_RETURN => "↵ Enter", Native.VK_TAB => "⇥ Tab",
        Native.VK_DELETE => "Delete", Native.VK_LEFT => "←", Native.VK_RIGHT => "→",
        Native.VK_UP => "↑", Native.VK_DOWN => "↓", Native.VK_ESCAPE => "Escape",
        Native.VK_HOME => "Home", Native.VK_END => "End", Native.VK_PRIOR => "PageUp", Native.VK_NEXT => "PageDown",
        _ => KeyboardConversion.TryConvert(key, layout, out var c) ? Visible(c.ToString()) : $"VK {key.VirtualKey:X2}"
    };

    public static bool IncludedInInputFilter(string kind) => kind is "Ввод" or "Подтверждение" or "Подсказка" or
        "Начало" or "Окончание" or "Ожидание редактора" or "Редактор доступен" or "Начало уточнено" or "Контекст изменился";
}
