namespace LayoutPreviewPrototype;

// A continuous read outage, never the age of a remembered document snapshot.
internal sealed class EditorAvailability
{
    public const int GraceMs = 1200;
    private long? _unavailableSince;
    public bool Waiting => _unavailableSince.HasValue;
    public long Duration(long now) => _unavailableSince is { } since ? Math.Max(0, now - since) : 0;
    public bool CanWait(long now)
    {
        _unavailableSince ??= now;
        long elapsed = now - _unavailableSince.Value;
        return elapsed >= 0 && elapsed < GraceMs;
    }
    public void Reset() => _unavailableSince = null;
}
