using System.ComponentModel;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LayoutPreviewPrototype;

internal enum PreviewThemeMode { Light, Dark, HighContrast }
internal sealed record PreviewThemePalette(PreviewThemeMode Mode, Color Background, Color Text, Color Border)
{
    public static PreviewThemePalette Select(bool light, bool highContrast, Color window, Color windowText) =>
        highContrast ? new(PreviewThemeMode.HighContrast, window, windowText, windowText) :
        light ? new(PreviewThemeMode.Light, Color.FromRgb(250, 250, 250), Color.FromRgb(97, 97, 97), Color.FromRgb(168, 168, 168)) :
        new(PreviewThemeMode.Dark, Color.FromRgb(51, 51, 51), Color.FromArgb(207, 154, 154, 154), Color.FromArgb(102, 154, 154, 154));

    public static bool UseLightTheme(object? appTheme, object? systemTheme) =>
        appTheme is int app && app is 0 or 1 ? app == 1 :
        systemTheme is int system && system is 0 or 1 ? system == 1 : true;
}

// Preference broadcasts are coalesced onto WPF. No polling or registry reads per key.
internal sealed class WindowsPreviewTheme : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action<PreviewThemePalette> _apply;
    private int _queued, _disposed;
    public PreviewThemePalette? Current { get; private set; }

    public WindowsPreviewTheme(Dispatcher dispatcher, Action<PreviewThemePalette> apply)
    {
        _dispatcher = dispatcher; _apply = apply;
        SystemEvents.UserPreferenceChanged += PreferenceChanged;
        SystemParameters.StaticPropertyChanged += SystemPropertyChanged;
        RefreshNow();
    }

    public void QueueRefresh()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _queued, 1) != 0) return;
        if (_dispatcher.HasShutdownStarted) { Interlocked.Exchange(ref _queued, 0); return; }
        _dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _queued, 0);
            if (Volatile.Read(ref _disposed) == 0) RefreshNow();
        }, DispatcherPriority.Background);
    }

    public void RefreshNow()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        bool light = Current?.Mode != PreviewThemeMode.Dark;
        try
        {
            using var settings = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            light = PreviewThemePalette.UseLightTheme(settings?.GetValue("AppsUseLightTheme"), settings?.GetValue("SystemUsesLightTheme"));
        }
        catch (Exception error) when (error is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        { /* Retain the previous mode if preferences temporarily cannot be read. */ }
        var palette = PreviewThemePalette.Select(light, SystemParameters.HighContrast, SystemColors.WindowColor, SystemColors.WindowTextColor);
        if (palette == Current) return;
        Current = palette; _apply(palette);
    }

    private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs args) => QueueRefresh();
    private void SystemPropertyChanged(object? sender, PropertyChangedEventArgs args) => QueueRefresh();
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        SystemEvents.UserPreferenceChanged -= PreferenceChanged;
        SystemParameters.StaticPropertyChanged -= SystemPropertyChanged;
    }
}
