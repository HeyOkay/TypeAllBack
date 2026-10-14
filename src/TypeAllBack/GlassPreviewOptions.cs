using Microsoft.Win32;
using System.Windows;

namespace LayoutPreviewPrototype;

// Experimental glass style of the preview: a translucent pill in the field's color with an
// edge light and a short even shadow, all drawn by the preview window itself. A separate
// acrylic window was tried and dropped: Windows 11 gives such a popup its own downward
// shadow, which showed as a strip under short hints.
internal static class GlassPreviewOptions
{
    private static int _enabled;
    public static bool Enabled => Volatile.Read(ref _enabled) != 0;
    public static event Action<bool>? Changed;
    public static void SetEnabled(bool enabled)
    {
        int value = enabled ? 1 : 0;
        if (Interlocked.Exchange(ref _enabled, value) != value) Changed?.Invoke(enabled);
    }

    // Respect "Transparency effects" off and high contrast: then the solid style is used.
    public static bool SystemAllows()
    {
        if (SystemParameters.HighContrast) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            return key?.GetValue("EnableTransparency") is not int value || value != 0;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        { return true; }
    }
}
