namespace LayoutPreviewPrototype;

internal static class EditorContextDiagnostics
{
    public static string Changed(EditorSnapshot? before, EditorSnapshot after)
    {
        if (before == null) return "исходное поле отсутствует";
        List<string> parts = [];
        if (before.Foreground != after.Foreground) parts.Add("активное окно");
        if (before.Editor != after.Editor) parts.Add("HWND поля");
        if (before.Thread != after.Thread) parts.Add("поток поля");
        if (before.AutomationIdentity != after.AutomationIdentity)
            parts.Add((before.AutomationIdentity.Length == 0) != (after.AutomationIdentity.Length == 0)
                ? "backend Native/UIA" : "UIA-идентификатор поля");
        return string.Join(", ", parts);
    }

    public static string Describe(EditorSnapshot? before, EditorSnapshot after) =>
        "изменилось: " + Changed(before, after) + "; было: " + Context(before) + "; стало: " + Context(after);

    private static string Context(EditorSnapshot? value) => value == null ? "—" :
        $"{(value.AutomationIdentity.Length == 0 ? "Native" : "UIA")}, окно={value.Foreground.ToInt64():X}, " +
        $"поле={value.Editor.ToInt64():X}, поток={value.Thread}, UIA={value.AutomationIdentity}, " +
        $"снимок={value.Revision}, выделение={value.SelectionStart}:{value.SelectionEnd}";
}
