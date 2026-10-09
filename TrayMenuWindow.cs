using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace LayoutPreviewPrototype;

internal sealed class TrayMenuWindow : Window
{
    private readonly StackPanel _items = new() { Margin = new Thickness(8) };
    private readonly WindowsPreviewTheme _theme;
    private readonly List<(ToggleButton Button, TextBlock Mark, Func<bool> Read)> _toggles = [];
    private readonly List<TextBlock> _labels = [];
    private IntPtr _hwnd;
    private bool _dark;
    internal TrayMenuWindow()
    {
        Title = "TypeAllBack"; Width = 292; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(-1), CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false });
        var root = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = _items };
        Content = root;
        Resources[typeof(ButtonBase)] = System.Windows.Markup.XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ButtonBase">
            <Setter Property="MinHeight" Value="38"/><Setter Property="Padding" Value="12,7"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ButtonBase">
              <Border x:Name="Plate" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" CornerRadius="5" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center"/>
              </Border><ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Plate" Property="Background" Value="#187F7F7F"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Plate" Property="Background" Value="#247F7F7F"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        var heading = new TextBlock { Text = "TypeAllBack", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(12, 9, 12, 10) };
        _labels.Add(heading); _items.Children.Add(heading);
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            if (PresentationSource.FromVisual(this) is HwndSource source) source.CompositionTarget.BackgroundColor = Colors.Transparent;
            ApplyBackdrop();
        };
        Deactivated += (_, _) => Hide();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Hide(); e.Handled = true; } };
        _theme = new WindowsPreviewTheme(Dispatcher, palette =>
        {
            _dark = palette.Mode == PreviewThemeMode.Dark;
            Foreground = palette.Mode == PreviewThemeMode.HighContrast ? SystemColors.WindowTextBrush : _dark ? Brushes.WhiteSmoke : new SolidColorBrush(Color.FromRgb(35, 35, 35));
            root.BorderBrush = palette.Mode == PreviewThemeMode.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(_dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(35, 0, 0, 0));
            foreach (var label in _labels) label.Foreground = Foreground;
            ApplyBackdrop();
        });
        Closed += (_, _) => _theme.Dispose();
    }
    internal void AddToggle(string label, Func<bool> read, Action<bool> set)
    {
        var button = new ToggleButton { Style = (Style)Resources[typeof(ButtonBase)] };
        var mark = new TextBlock { Text = "✓", Width = 24, FontSize = 14 };
        button.Content = Row(label, mark);
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => { set(button.IsChecked == true); Refresh(); };
        _toggles.Add((button, mark, read)); _items.Children.Add(button);
    }
    internal void AddCommand(string label, Action action)
    {
        var button = new Button { Style = (Style)Resources[typeof(ButtonBase)], Content = Row(label, new TextBlock { Width = 24 }) };
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        button.Click += (_, _) => { Hide(); action(); };
        _items.Children.Add(button);
    }
    private UIElement Row(string label, TextBlock mark)
    {
        var row = new DockPanel(); row.Children.Add(mark);
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Foreground };
        _labels.Add(text); row.Children.Add(text); return row;
    }
    internal void Separator() => _items.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)), Margin = new Thickness(12, 6, 12, 6) });
    private void Refresh()
    {
        foreach (var (button, mark, read) in _toggles) { button.IsChecked = read(); mark.Visibility = button.IsChecked == true ? Visibility.Visible : Visibility.Hidden; }
    }
    internal void ShowAtCursor()
    {
        if (IsVisible) { Hide(); return; }
        Refresh(); _theme.RefreshNow();
        GetCursorPos(out var point);
        var monitor = MonitorFromPoint(point, 2); var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() }; GetMonitorInfo(monitor, ref info);
        // Position in physical pixels. WPF handles the monitor's DPI transition.
        new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(_hwnd, IntPtr.Zero, point.X, point.Y, 0, 0, 0x0015);
        Show(); UpdateLayout();
        GetWindowRect(_hwnd, out var rect);
        int x = Math.Max(info.Work.Left, Math.Min(point.X, info.Work.Right - (rect.Right - rect.Left)));
        int y = Math.Max(info.Work.Top, Math.Min(point.Y - (rect.Bottom - rect.Top), info.Work.Bottom - (rect.Bottom - rect.Top)));
        SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, 0x0015);
        SetForegroundWindow(_hwnd); Activate();
        if (_items.Children.OfType<ButtonBase>().FirstOrDefault() is { } first) first.Focus();
    }
    private void ApplyBackdrop()
    {
        if (_hwnd == IntPtr.Zero) return;
        bool available = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) && !SystemParameters.HighContrast;
        int dark = _dark ? 1 : 0, rounded = 2, mica = available ? 2 : 1;
        DwmSetWindowAttribute(_hwnd, 20, ref dark, 4); DwmSetWindowAttribute(_hwnd, 33, ref rounded, 4);
        int result = DwmSetWindowAttribute(_hwnd, 38, ref mica, 4);
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (available && result == 0 && DwmExtendFrameIntoClientArea(_hwnd, ref margins) == 0) Background = Brushes.Transparent;
        else Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : new SolidColorBrush(_dark ? Color.FromRgb(32, 32, 32) : Color.FromRgb(248, 248, 248));
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { internal int Left, Right, Top, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
