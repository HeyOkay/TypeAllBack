namespace LayoutPreviewPrototype;

// After an accepted hint the next text normally belongs to the accepted layout and starts no
// preview until the caret moves, Enter, Escape or a layout change (AcceptedInputGuard).
// With this option the next typed character starts a new session right away, so the next
// wrong-layout word gets its own hint without leaving the line first.
internal static class NewSessionAfterAcceptOptions
{
    private static int _enabled;
    public static bool Enabled => Volatile.Read(ref _enabled) != 0;
    public static event Action<bool>? Changed;
    public static void SetEnabled(bool enabled)
    {
        int value = enabled ? 1 : 0;
        if (Interlocked.Exchange(ref _enabled, value) != value) Changed?.Invoke(enabled);
    }
}
