using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Automation;

namespace LayoutPreviewPrototype;

internal static class ShellDiscoverySmokeTests
{
    public static void Run()
    {
        Exception? failure = null;
        int checks = 0;
        IntPtr originalForeground = Native.GetForegroundWindow();
        var thread = new Thread(() =>
        {
            var application = new Application();
            var first = new TextBox { Text = "controlled fixture", Margin = new Thickness(8) };
            var second = new TextBox { Text = "readonly fixture", IsReadOnly = true, Margin = new Thickness(8) };
            var password = new PasswordBox();
            var panel = new StackPanel(); panel.Children.Add(first); panel.Children.Add(second); panel.Children.Add(password);
            var window = new Window { Width = 300, Height = 160, Content = panel, ShowActivated = false,
                ShowInTaskbar = false, Opacity = 0, WindowStyle = WindowStyle.None };
            void Check(bool condition, string message)
            { checks++; if (!condition) throw new InvalidOperationException(message); }
            window.Loaded += async (_, _) =>
            {
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(window).Handle;
                    bool Find() => ShellSearchResolver.TryFind(AutomationElement.FromHandle(hwnd),
                        Environment.ProcessId, out _, out _);
                    Check(await Task.Run(Find), "One writable field must be discoverable without keyboard focus");
                    second.IsReadOnly = false;
                    Check(!await Task.Run(Find), "Two writable fields must be rejected as ambiguous");
                    first.IsReadOnly = true; second.IsReadOnly = true;
                    Check(!await Task.Run(Find), "Readonly and password inputs must not be adopted");
                    Check(Native.GetForegroundWindow() == originalForeground, "Discovery fixture must not steal foreground focus");
                }
                catch (Exception e) { failure = e; }
                finally { window.Close(); application.Shutdown(); }
            };
            application.Run(window);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15))) throw new TimeoutException("Shell discovery fixture timed out");
        if (failure != null) throw new InvalidOperationException("Shell discovery fixture failed", failure);
        Console.WriteLine($"PASS: {checks} UIA discovery checks in an invisible fixture (no focus or injected input)");
    }
}
