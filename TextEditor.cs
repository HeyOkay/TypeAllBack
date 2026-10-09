namespace LayoutPreviewPrototype;

// Native Edit/RichEdit and UI Automation share the same checked session contract.
internal static class TextEditor
{
    private static readonly UiaTextEditor Automation = new();
    public static string LastCaptureFailure => Automation.LastCaptureFailure;
    public static void Start() => Automation.Start();
    public static void Stop() => Automation.Dispose();
    public static event Action? SnapshotChanged { add => Automation.SnapshotChanged += value; remove => Automation.SnapshotChanged -= value; }
    public static void NotifyInput() => Automation.NotifyInput();
    // Only cached snapshots and cheap focus checks; no native text reads or COM.
    public static bool TryReadForHook(out EditorSnapshot snapshot) => Automation.TryReadCached(out snapshot);

    public static bool TryRead(out EditorSnapshot snapshot) => Automation.TryReadCached(out snapshot);

    public static bool SameEditor(EditorSnapshot a, EditorSnapshot b) => NotepadEditor.SameEditor(a, b);

    public static bool TryGetCaret(EditorSnapshot snapshot, out Native.RECT caret)
    {
        // Never call COM on the WPF/keyboard-hook thread. Native controls are
        // enriched with matching geometry captured by the background UIA worker.
        caret = snapshot.Caret;
        return caret.Bottom > caret.Top;
    }

    public static Task<bool> TrySwitchLayoutAsync(EditorSnapshot expected, InputSession session, IntPtr layout) =>
        Automation.TrySwitchLayoutAsync(expected, session, layout);

    public static Task<bool> TryReplaceAsync(EditorSnapshot expected, InputSession session, IntPtr layout, Func<bool> stillValid) =>
        Automation.TryReplaceAsync(expected, session, layout, stillValid);
}
