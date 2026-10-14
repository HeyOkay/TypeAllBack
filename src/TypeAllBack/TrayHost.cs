using System.Diagnostics;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LayoutPreviewPrototype;

internal sealed class TrayHost : IDisposable
{
    private readonly TraySettings _settings;
    private readonly TrayMenuWindow _menu = new();
    private readonly TrayNative _icon;
    private readonly UpdateChecker _updates = new();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(24) };
    private bool _disposed, _updating;
    private string Feed => string.IsNullOrWhiteSpace(_settings.UpdateFeedUrl) ? UpdateChecker.DefaultFeed : _settings.UpdateFeedUrl;
    internal TrayHost(TraySettings settings, Action<bool> enabled, Action debug, CorrectionSound sound)
    {
        _settings = settings;
        MigrateStartupExecutable();
        _menu.AddToggle("Утилита включена", () => settings.Enabled, value => { enabled(value); settings.Enabled = value; settings.Save(); });
        _menu.AddToggle("Исправлять перед Enter", () => AutoEnterOptions.Enabled, AutoEnterOptions.SetEnabled);
        _menu.AddToggle("Звук исправления", () => sound.Enabled, value => { sound.SetEnabled(value); settings.Sound = value; settings.Save(); });
        _menu.AddToggle("Новая сессия после принятия", () => NewSessionAfterAcceptOptions.Enabled, NewSessionAfterAcceptOptions.SetEnabled);
        _menu.AddToggle("Стеклянная подсказка", () => GlassPreviewOptions.Enabled, GlassPreviewOptions.SetEnabled);
        _menu.AddCommand("Отладка…", debug);
        _menu.AddCommand("Тестовые поля…", OpenFieldTester);
        _menu.Separator();
        _menu.AddToggle("Запускать с Windows", AutoStartEnabled, SetAutoStart);
        _menu.AddToggle("Проверять обновления", () => settings.CheckUpdates, value => { settings.CheckUpdates = value; settings.Save(); if (value && UpdateChecker.Https(Feed, out _)) _ = CheckUpdates(false); });
        _menu.AddCommand("Проверить обновления сейчас", () => _ = CheckUpdates(true));
        _menu.Separator();
        _menu.AddCommand("Выйти", () => Application.Current.Shutdown());
        _icon = new TrayNative(_menu.ShowAtCursor);
        _updateTimer.Tick += (_, _) => { if (settings.CheckUpdates) _ = CheckUpdates(false); };
        _updateTimer.Start();
        if (settings.CheckUpdates && UpdateChecker.Https(Feed, out _)) _ = CheckUpdates(false);
    }
    private static void OpenFieldTester()
    {
        try
        {
            if (Environment.ProcessPath is { } path)
                Process.Start(new ProcessStartInfo(path, FieldTesterWindow.Argument) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Trace.WriteLine("Field tester start failed: " + e.GetType().Name); }
    }
    private async Task CheckUpdates(bool manual)
    {
        if (_updating || _disposed) return;
        var result = await _updates.CheckAsync(Feed);
        if (_disposed || result == null || (!manual && (! _settings.CheckUpdates || result.Package == null))) return;
        if (result.Package is { } package)
        {
            if (MessageBox.Show(result.Message, "TypeAllBack", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                _updating = true;
                try { new UpdateWindow(_updates, package).ShowDialog(); }
                finally { _updating = false; }
            }
        }
        else MessageBox.Show(result.Message, "TypeAllBack", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private static string StartupCommand => "\"" + Environment.ProcessPath + "\"";
    internal static void CleanupStartup() => SetAutoStart(false);
    private static void MigrateStartupExecutable()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            string? old = run?.GetValue("TypeAllBack") as string;
            string legacy = "\"" + System.IO.Path.Combine(AppContext.BaseDirectory, "LayoutPreviewPrototype.exe") + "\"";
            if (string.Equals(old, legacy, StringComparison.OrdinalIgnoreCase)) run!.SetValue("TypeAllBack", StartupCommand);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or System.IO.IOException) { }
    }
    private static bool AutoStartEnabled()
    {
        try { using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); return run?.GetValue("TypeAllBack") as string == StartupCommand; }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or System.IO.IOException) { return false; }
    }
    private static void SetAutoStart(bool enabled)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled) run.SetValue("TypeAllBack", StartupCommand);
            else if (run.GetValue("TypeAllBack") as string == StartupCommand) run.DeleteValue("TypeAllBack", false);
        }
        catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or System.IO.IOException)
        { MessageBox.Show("Не удалось изменить автозапуск.", "TypeAllBack", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    public void Dispose() { _disposed = true; _updateTimer.Stop(); _updates.Dispose(); _icon.Dispose(); _menu.Close(); }
}
