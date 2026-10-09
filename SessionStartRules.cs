namespace LayoutPreviewPrototype;

internal enum SessionStartRuleId { DirectTyping, SelectionReplacement, DeferredStartup }

internal sealed class SessionStartRule(SessionStartRuleId id, string title, string description)
{
    public SessionStartRuleId Id { get; } = id;
    public string Title { get; } = title;
    public string Description { get; } = description;
    public bool Enabled { get; internal set; } = true;
}

// UI-thread-owned policy. Only gates starting a new session; validation of edits stays mandatory.
internal sealed class SessionStartRules
{
    private readonly SessionStartRule[] _rules =
    [
        new(SessionStartRuleId.DirectTyping, "Ввод при готовом поле",
            "Начать с первого подходящего символа при отсутствии сессии и наличии снимка поля без выделения."),
        new(SessionStartRuleId.SelectionReplacement, "Ввод вместо выделенного текста",
            "Начать с символа, заменяющего выделенный текст. Продолжение уже отслеживаемого автодополнения не создаёт новую сессию."),
        new(SessionStartRuleId.DeferredStartup, "Ввод до готовности поля",
            "Сохранить первые клавиши, пока снимка поля нет; начать только после подтверждения всего захваченного текста.")
    ];
    public IReadOnlyList<SessionStartRule> Items { get; }
    public event Action<SessionStartRule>? Changed;
    public SessionStartRules() { Items = Array.AsReadOnly(_rules); }
    public SessionStartRule Get(SessionStartRuleId id) => _rules.Single(rule => rule.Id == id);
    public bool Allows(SessionStartRuleId id) => Get(id).Enabled;
    public void SetEnabled(SessionStartRuleId id, bool enabled)
    {
        var rule = Get(id);
        if (rule.Enabled == enabled) return;
        rule.Enabled = enabled;
        Changed?.Invoke(rule);
    }
}
