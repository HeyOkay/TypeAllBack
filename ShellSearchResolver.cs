using System.Diagnostics;
using System.Windows.Automation;

namespace LayoutPreviewPrototype;

internal sealed class ShellDiscoveryBudget
{
    private string _context = "";
    private int _attempts;
    private long _lastAttempt;
    public bool TryBegin(string context, long now)
    {
        if (_context != context) { _context = context; _attempts = 0; }
        if (_attempts >= 2 || (_attempts > 0 && now - _lastAttempt < 300)) return false;
        _attempts++; _lastAttempt = now; return true;
    }
}

internal static class ShellSearchResolver
{
    internal static bool IsSearchProcess(string name) => name is "SearchHost" or "StartMenuExperienceHost";

    // A one-time bounded discovery for the shell's focus container. Never call
    // this on ordinary editors or on each keystroke/poll. Ambiguity fails closed.
    public static bool TryFind(AutomationElement root, int processId,
        out AutomationElement element, out TextPattern pattern)
    {
        element = null!; pattern = null!;
        using var measurement = PerformanceDiagnostics.Measure("shell-discovery");
        long started = Stopwatch.GetTimestamp();
        var pending = new Stack<(AutomationElement Element, int Depth)>();
        pending.Push((root, 0));
        int visits = 0, candidates = 0;
        while (pending.Count > 0)
        {
            if (++visits > 64 || Stopwatch.GetElapsedTime(started).TotalMilliseconds > 100) return false;
            var (candidate, depth) = pending.Pop();
            var properties = candidate.Current;
            if (properties.ProcessId != processId) continue;
            if (properties.ControlType == ControlType.Edit && properties.IsEnabled &&
                !properties.IsPassword && properties.IsKeyboardFocusable &&
                candidate.TryGetCurrentPattern(TextPattern.Pattern, out var raw))
            {
                var text = (TextPattern)raw;
                var readOnly = text.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                bool? valueReadOnly = candidate.TryGetCurrentPattern(ValuePattern.Pattern, out var rawValue)
                    ? ((ValuePattern)rawValue).Current.IsReadOnly : null;
                if (UiaTextEditor.IsWritableText(readOnly is bool b ? b : null, valueReadOnly))
                {
                    if (++candidates > 1) return false;
                    element = candidate; pattern = text;
                }
            }
            if (depth > 0)
            {
                var sibling = TreeWalker.ControlViewWalker.GetNextSibling(candidate);
                if (sibling != null) pending.Push((sibling, depth));
            }
            if (depth < 5)
            {
                var child = TreeWalker.ControlViewWalker.GetFirstChild(candidate);
                if (child != null) pending.Push((child, depth + 1));
            }
            else if (TreeWalker.ControlViewWalker.GetFirstChild(candidate) != null)
                return false; // Unvisited branches could contain another input.
        }
        return candidates == 1 && Stopwatch.GetElapsedTime(started).TotalMilliseconds <= 100;
    }
}
