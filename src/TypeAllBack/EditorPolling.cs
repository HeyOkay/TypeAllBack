namespace LayoutPreviewPrototype;

internal static class EditorPolling
{
    // Input and focus events wake the worker immediately. These intervals cover
    // late WM_CHAR/provider publication and providers that omit text events.
    internal const int MaximumSnapshotAgeMs = 750;
    public static int Delay(long now, long lastInput, bool supported, double captureMs)
    {
        if (now - lastInput is >= 0 and < 450)
            return captureMs >= 50 ? 80 : 35;
        return supported ? 500 : 1000;
    }
}
