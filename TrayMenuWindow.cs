using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace LayoutPreviewPrototype;

internal sealed class TrayMenuWindow : Window
{
    private readonly StackPanel _items = new() { Margin = new Thickness(4) };
    private readonly WindowsPreviewTheme _theme;
    private readonly List<(ToggleButton Button, TextBlock Mark, Func<bool> Read)> _toggles = [];
    private readonly List<TextBlock> _labels = [];
    private IntPtr _hwnd;
    private bool _dark;
    private readonly List<Border> _separators = [];
    private readonly FontFamily _icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    internal TrayMenuWindow()
    {
        Title = "TypeAllBack"; MinWidth = 230; MaxWidth = 360; SizeToContent = SizeToContent.WidthAndHeight;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; Background = Brushes.Transparent;
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"); FontSize = 13;
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(-1), CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false });
        var root = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = _items };
        Content = root;
        Resources["MenuHover"] = new SolidColorBrush(Color.FromArgb(26, 255, 255, 255));
        Resources[typeof(ButtonBase)] = System.Windows.Markup.XamlReader.Parse("""
          <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ButtonBase">
            <Setter Property="MinHeight" Value="30"/><Setter Property="Padding" Value="8,6,10,6"/>
            <Setter Property="Margin" Value="0,1"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Background" Value="Transparent"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ButtonBase">
              <Border x:Name="Plate" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" CornerRadius="4" Padding="{TemplateBinding Padding}">
                <ContentPresenter HorizontalAlignment="Stretch" VerticalAlignment="Center"/>
              </Border><ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Plate" Property="Background" Value="{DynamicResource MenuHover}"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Plate" Property="Background" Value="{DynamicResource MenuHover}"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.45"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          """);
        var heading = new StackPanel { Margin = new Thickness(8, 8, 10, 8) };
        var title = new TextBlock { Text = "TypeAllBack", FontWeight = FontWeights.SemiBold, FontSize = 13 };
        var detail = new TextBlock { Text = "Исправление раскладки", FontSize = 12, Margin = new Thickness(0, 2, 0, 0), Opacity = .6 };
        _labels.Add(title); _labels.Add(detail);
        heading.Children.Add(title); heading.Children.Add(detail); _items.Children.Add(heading);
        Separator();
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
            Foreground = palette.Mode == PreviewThemeMode.HighContrast ? SystemColors.WindowTextBrush : _dark ? Brushes.WhiteSmoke : new SolidColorBrush(Color.FromRgb(26, 26, 26));
            root.BorderBrush = palette.Mode == PreviewThemeMode.HighContrast ? SystemColors.WindowTextBrush : new SolidColorBrush(_dark ? Color.FromArgb(51, 255, 255, 255) : Color.FromArgb(38, 0, 0, 0));
            foreach (var label in _labels) label.Foreground = Foreground;
            Resources["MenuHover"] = SystemParameters.HighContrast ? SystemColors.HighlightBrush : new SolidColorBrush(_dark ? Color.FromArgb(26, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0));
            foreach (var line in _separators) line.Background = new SolidColorBrush(_dark ? Color.FromArgb(38, 255, 255, 255) : Color.FromArgb(26, 0, 0, 0));
            ApplyBackdrop();
        });
        Closed += (_, _) => _theme.Dispose();
    }
    internal void AddToggle(string label, Func<bool> read, Action<bool> set)
    {
        var button = new ToggleButton { Style = (Style)Resources[typeof(ButtonBase)] };
        var mark = new TextBlock { Text = "\uE73E", FontFamily = _icons, Width = 24, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Foreground };
        _labels.Add(mark);
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
    internal void Separator()
    {
        var line = new Border { Height = 1, Background = new SolidColorBrush(Color.FromArgb(26, 0, 0, 0)), Margin = new Thickness(8, 4, 8, 4) };
        _separators.Add(line); _items.Children.Add(line);
    }
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
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int gap = (int)Math.Round(8 * scale);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        int x = Math.Max(info.Work.Left, Math.Min(point.X - width / 2, info.Work.Right - width));
        int y = Math.Max(info.Work.Top, Math.Min(point.Y - height - gap, info.Work.Bottom - height - gap));
        SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, 0x0015);
        SetForegroundWindow(_hwnd); Activate();
        if (!SystemParameters.HighContrast && SystemParameters.MenuAnimation)
            _items.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        else _items.BeginAnimation(OpacityProperty, null);
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
