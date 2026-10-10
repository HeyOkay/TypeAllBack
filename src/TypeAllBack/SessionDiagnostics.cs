namespace LayoutPreviewPrototype;

// UI-thread-owned, bounded and memory-only. Never send captured text to Trace or disk.
internal sealed record SessionDiagnosticFrame(long Id, string State, string Context, string Layouts,
    int Start, int End, int Words, string Confirmed, string Expected, string Replacement,
    int Pending, bool PreviewVisible, long ProcessedSequence, long CapturedSequence);

internal sealed record SessionDiagnosticEvent(long Number, DateTime Time, long SessionId, string Kind, string Detail)
{
    public override string ToString() => $"{Time:HH:mm:ss.fff}  #{SessionId}  {Kind} — {Detail}";
}

internal sealed class SessionDiagnostics
{
    public const int MaximumEvents = 200;
    private readonly Queue<SessionDiagnosticEvent> _events = new();
    private long _eventNumber;
    public long SessionId { get; private set; }
    public bool Active { get; private set; }
    public string StartReason { get; private set; } = "Сессия ещё не начиналась";
    public string LastEndReason { get; private set; } = "—";
    public SessionDiagnosticFrame? LastSession { get; private set; }
    public IReadOnlyCollection<SessionDiagnosticEvent> Events => _events;
    public long Revision => _eventNumber;

    public void Start(string reason, int offset)
    {
        SessionId++;
        Active = true;
        StartReason = $"{DateTime.Now:HH:mm:ss.fff} — {reason}; начало {offset}";
        Event("Начало", reason + $"; смещение {offset}");
    }

    public void End(string reason, SessionDiagnosticFrame? final, string detail = "")
    {
        if (!Active) return;
        LastSession = final;
        LastEndReason = $"{DateTime.Now:HH:mm:ss.fff} — #{SessionId}: {reason}";
        Event("Окончание", string.IsNullOrEmpty(detail) ? reason : reason + "; " + detail);
        Active = false;
    }

    public void Event(string kind, string detail) => Event(kind, detail, DateTime.Now, SessionId);

    public void Event(string kind, string detail, DateTime time, long sessionId)
    {
        _events.Enqueue(new(++_eventNumber, time, sessionId, kind, detail));
        while (_events.Count > MaximumEvents) _events.Dequeue();
    }

    public void ClearHistory()
    {
        _events.Clear();
        LastSession = null;
        LastEndReason = "История очищена";
        _eventNumber++;
    }

    public static int CountWords(string text)
    {
        int count = 0;
        bool inside = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) inside = false;
            else if (!inside) { inside = true; count++; }
        }
        return count;
    }
}
