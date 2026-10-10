using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace LayoutPreviewPrototype;

internal sealed class UpdateWindow : Window
{
    private readonly CancellationTokenSource _cancel = new();
    private bool _finished;
    internal UpdateWindow(UpdateChecker checker, UpdatePackage package)
    {
        Title = "Обновление TypeAllBack"; Width = 390; Height = 190; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(20) };
        var text = new TextBlock { Text = $"Загрузка версии {package.Version.ToString(3)}…", Margin = new Thickness(0, 0, 0, 14) };
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Height = 5 };
        var cancel = new Button { Content = "Отмена", HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(16, 5, 16, 5), Margin = new Thickness(0, 20, 0, 0) };
        panel.Children.Add(text); panel.Children.Add(bar); panel.Children.Add(cancel); Content = panel;
        Closed += (_, _) => _cancel.Dispose();
        cancel.Click += (_, _) => _cancel.Cancel();
        Closing += (_, e) => { if (!_finished) { _cancel.Cancel(); e.Cancel = true; } };
        Loaded += async (_, _) =>
        {
            try
            {
                var progress = new Progress<DownloadProgress>(p => { bar.Value = 100.0 * p.Bytes / p.Total; text.Text = $"Загрузка: {p.Bytes / 1048576.0:F1} из {p.Total / 1048576.0:F1} МБ"; });
                string file = await checker.DownloadAsync(package, progress, _cancel.Token);
                _cancel.Token.ThrowIfCancellationRequested();
                text.Text = "Пакет проверен. Установка и перезапуск…"; cancel.IsEnabled = false;
                StartInstaller(file, package.Sha256);
                _finished = true; Application.Current.Shutdown();
            }
            catch (OperationCanceledException) { _finished = true; Close(); }
            catch (Exception e) when (e is IOException or System.Net.Http.HttpRequestException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            { _finished = true; MessageBox.Show("Не удалось загрузить или установить обновление. Текущая версия продолжает работать.", "TypeAllBack", MessageBoxButton.OK, MessageBoxImage.Warning); Close(); }
        };
    }
    internal static void StartInstaller(string file, string checksum)
    {
        string directory = Path.GetDirectoryName(file)!;
        string script = Path.Combine(directory, "InstallUpdate.ps1");
        using (var source = typeof(UpdateWindow).Assembly.GetManifestResourceStream("TypeAllBack.InstallUpdate.ps1")!)
        using (var target = File.Create(script)) source.CopyTo(target);
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TypeAllBack", "TypeAllBack.exe");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-InstallerPath", file, "-TargetExecutable", executable, "-ParentProcessId", Environment.ProcessId.ToString(), "-LogPath", Path.Combine(directory, "install.log"), "-ExpectedSha256", checksum }) start.ArgumentList.Add(argument);
        using var helper = Process.Start(start) ?? throw new IOException("Update helper could not start");
    }
}
