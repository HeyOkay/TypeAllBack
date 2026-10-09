using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LayoutPreviewPrototype;

public partial class PreviewWindow : Window
{
    public event EventHandler? AcceptRequested;
    internal event Action<string>? ThemeChanged;
    internal event Action<string>? FontMetricsChanged;
    private readonly WindowsPreviewTheme _theme;
    internal string ThemeDescription { get; private set; } = "Тема ещё не определена";

    private void ApplyTheme(PreviewThemePalette palette)
    {
        static SolidColorBrush Brush(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
        PreviewBorder.Background = Brush(palette.Background);
        PreviewBorder.BorderBrush = Brush(palette.Border);
        PreviewText.Foreground = Brush(palette.Text);
        ThemeDescription = palette.Mode switch
        {
            PreviewThemeMode.Light => "Светлая тема Windows",
            PreviewThemeMode.Dark => "Тёмная тема Windows",
            _ => "Контрастная тема Windows"
        };
        ThemeChanged?.Invoke(ThemeDescription);
    }

    private IntPtr ThemeMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message is 0x001A or 0x0015 or 0x031A) _theme.QueueRefresh(); // settings/colors/theme changed
        return IntPtr.Zero;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    // Reserve transparent room to the LEFT of the visible preview. Accept is animated
    // inside one stationary native window, avoiding Window.Left animation/jank.
    private const double LeftReserveDip = 170;
    private const double VerticalReserveDip = 4;
    private const double RestingOpacity = 1.0;
    private const int EnterDurationMs = 150;
    private const int ResizeDurationMs = 120;
    private const int AcceptDurationMs = 180;

    private bool _acceptAnimationRunning;
    private IntPtr _hwnd;
    private double _layoutScale = 1;
    private bool _dpiRefreshQueued;
    private string _lastFontDiagnostic = "";
    private sealed record RenderRequest(string Text, Native.RECT Caret, uint Dpi, double FontSize,
        string Family, int Weight, bool Italic, string Source);
    private RenderRequest? _lastRender;

    public PreviewWindow()
    {
        InitializeComponent();
        _theme = new WindowsPreviewTheme(Dispatcher, ApplyTheme);
        Closed += (_, _) => _theme.Dispose();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(ThemeMessage);
            _theme.RefreshNow();
            var ex = Native.GetWindowLong(_hwnd, GWL_EXSTYLE);
            Native.SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    internal void ShowPreview(string text, Native.RECT caretPx, uint targetDpi, bool animateFirstAppearance,
        double fontSizeDip = 0, string fontFamilyName = "", int fontWeight = 400, bool fontItalic = false, string fontSource = "")
    {
        if (_acceptAnimationRunning)
            return;
        using var measurement = PerformanceDiagnostics.Measure("preview-render");
        _lastRender = new(text, caretPx, targetDpi, fontSizeDip, fontFamilyName, fontWeight, fontItalic, fontSource);
        new WindowInteropHelper(this).EnsureHandle();
        double overlayScale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        _layoutScale = PreviewMetrics.OverlayScaleRatio(targetDpi, overlayScale);

        if (!string.IsNullOrWhiteSpace(fontFamilyName))
        {
            try { PreviewText.FontFamily = new FontFamily(fontFamilyName); }
            catch (Exception error) when (error is ArgumentException or FormatException)
            { PreviewText.FontFamily = SystemFonts.MessageFontFamily; }
        }
        else if (animateFirstAppearance) PreviewText.FontFamily = SystemFonts.MessageFontFamily;
        if (animateFirstAppearance || fontSource.Length > 0)
        {
            PreviewText.FontWeight = FontWeight.FromOpenTypeWeight(fontWeight is >= 100 and <= 900 ? fontWeight : 400);
            PreviewText.FontStyle = fontItalic ? FontStyles.Italic : FontStyles.Normal;
        }
        PreviewBorder.Padding = new Thickness(4 * _layoutScale, 2 * _layoutScale, 4 * _layoutScale, 3 * _layoutScale);
        PreviewBorder.BorderThickness = new Thickness(_layoutScale);
        PreviewBorder.CornerRadius = new CornerRadius(6 * _layoutScale);
        Canvas.SetLeft(PreviewBorder, LeftReserveDip * _layoutScale);
        Canvas.SetTop(PreviewBorder, VerticalReserveDip * _layoutScale);

        PreviewBorder.BeginAnimation(WidthProperty, null);
        PreviewBorder.Width = double.NaN;
        double sourceSize = PreviewMetrics.ResolveFontSize(fontSizeDip, caretPx.Bottom - caretPx.Top,
            targetDpi, PreviewText.FontSize / _layoutScale, animateFirstAppearance, PreviewMetrics.HasReliableFormatting(fontSource), fontSource);
        PreviewText.FontSize = sourceSize * _layoutScale;
        PreviewText.Text = text.TrimEnd();
        // Measure the actual Display-mode TextBlock. FormattedText used different
        // glyph metrics and could leave an apparent extra blank at the right edge.
        PreviewText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double targetWidth = Math.Ceiling(PreviewText.DesiredSize.Width + PreviewBorder.Padding.Left +
            PreviewBorder.Padding.Right + PreviewBorder.BorderThickness.Left + PreviewBorder.BorderThickness.Right);
        double targetHeight = Math.Ceiling(PreviewText.DesiredSize.Height + PreviewBorder.Padding.Top +
            PreviewBorder.Padding.Bottom + PreviewBorder.BorderThickness.Top + PreviewBorder.BorderThickness.Bottom);
        double oldWidth = PreviewBorder.ActualWidth > 0 ? PreviewBorder.ActualWidth : targetWidth;

        PreviewBorder.Width = targetWidth;
        Height = Math.Max(40 * _layoutScale, targetHeight + VerticalReserveDip * 2 * _layoutScale);
        Width = LeftReserveDip * _layoutScale + targetWidth + 8 * _layoutScale;
        RootCanvas.Width = Width;
        RootCanvas.Height = Height;

        if (!IsVisible)
            Show();

        // caretPx is Win32 PHYSICAL pixels. Position the WPF HWND with SetWindowPos in
        // physical pixels too, so Windows scaling cannot multiply the coordinates again.
        double scale = Math.Max(1.0, targetDpi / 96.0);
        int reservePx = (int)Math.Round(LeftReserveDip * scale);
        int verticalReservePx = (int)Math.Round(VerticalReserveDip * scale);
        int previewHeightPx = (int)Math.Round(targetHeight * overlayScale);
        int caretHeightPx = Math.Max(1, caretPx.Bottom - caretPx.Top);
        int gapPx = Math.Max(1, (int)Math.Round(2 * scale));

        int visibleLeftPx = caretPx.Right + gapPx;
        int visibleTopPx = caretPx.Top + (caretHeightPx - previewHeightPx) / 2;
        int windowLeftPx = visibleLeftPx - reservePx;
        int windowTopPx = visibleTopPx - verticalReservePx;

        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, windowLeftPx, windowTopPx, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        string fontDiagnostic = $"source={fontSource} sizeBasis={(PreviewMetrics.NeedsOmniboxFallback(fontSource, fontSizeDip) ? "Chromium-desktop-default-14dip" : "font-metadata-or-geometry")} reportedFamily={fontFamilyName} renderedFamily={PreviewText.FontFamily.Source} reportedDip={fontSizeDip:F2} resolvedDip={sourceSize:F2} renderedDip={PreviewText.FontSize:F2} sourceDpi={targetDpi} overlayScale={overlayScale:F2} caretPixels={caretHeightPx} textPixels={PreviewText.DesiredSize.Height * overlayScale:F2} weight={fontWeight} italic={fontItalic}";
        if (animateFirstAppearance || fontDiagnostic != _lastFontDiagnostic)
        {
            Trace.WriteLine("Font metrics: " + fontDiagnostic);
            FontMetricsChanged?.Invoke(fontDiagnostic);
            _lastFontDiagnostic = fontDiagnostic;
        }

        if (!SystemParameters.ClientAreaAnimation) { StopVisualAnimations(); PreviewBorder.Opacity = RestingOpacity; PreviewScale.ScaleX = PreviewScale.ScaleY = 1; PreviewTranslate.X = 0; }
        else if (animateFirstAppearance)
        {
            StopVisualAnimations();
            PreviewBorder.Opacity = RestingOpacity;
            PreviewText.Opacity = 0;
            PreviewScale.ScaleX = PreviewScale.ScaleY = 0.96;
            PreviewTranslate.X = 0;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            // Keep the background opaque throughout the entrance so following
            // document text is covered; only the inactive label fades into view.
            Animate(PreviewText, UIElement.OpacityProperty, 0, 1, EnterDurationMs, ease);
            Animate(PreviewScale, ScaleTransform.ScaleXProperty, 0.96, 1.0, EnterDurationMs, ease);
            Animate(PreviewScale, ScaleTransform.ScaleYProperty, 0.96, 1.0, EnterDurationMs, ease);
        }
        else
        {
            PreviewBorder.BeginAnimation(OpacityProperty, null);
            PreviewBorder.Opacity = RestingOpacity;
            PreviewText.BeginAnimation(OpacityProperty, null);
            PreviewText.Opacity = 1;
            PreviewScale.ScaleX = PreviewScale.ScaleY = 1;
            PreviewTranslate.X = 0;

            if (SystemParameters.ClientAreaAnimation && Math.Abs(targetWidth - oldWidth) > 0.5)
            {
                var widthAnim = new DoubleAnimation(oldWidth, targetWidth, TimeSpan.FromMilliseconds(ResizeDurationMs))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                };
                Timeline.SetDesiredFrameRate(widthAnim, 60);
                PreviewBorder.BeginAnimation(WidthProperty, widthAnim);
            }
        }
    }

    public async Task PlayAcceptAnimationAsync()
    {
        if (!SystemParameters.ClientAreaAnimation || !IsVisible || _acceptAnimationRunning)
            return;

        _acceptAnimationRunning = true;
        StopVisualAnimations();
        PreviewBorder.Opacity = RestingOpacity;
        PreviewTranslate.X = 0;

        // Move the visual inside a stationary HWND. This is much smoother than animating
        // Window.Left, especially for AllowsTransparency windows.
        double travel = Math.Max(30 * _layoutScale,
            Math.Min(PreviewBorder.ActualWidth + 8 * _layoutScale, (LeftReserveDip - 8) * _layoutScale));
        var done = new TaskCompletionSource<bool>();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var move = new DoubleAnimation(0, -travel, TimeSpan.FromMilliseconds(AcceptDurationMs))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        Timeline.SetDesiredFrameRate(move, 60);
        move.Completed += (_, _) => done.TrySetResult(true);

        // Keep the preview intact briefly, then make the dissolve unmistakable and finish
        // it at the midpoint, matching the HTML visual contract.
        var fade = new DoubleAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(AcceptDurationMs),
            FillBehavior = FillBehavior.HoldEnd
        };
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.00)));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.30)));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromPercent(0.50),
            new CubicEase { EasingMode = EasingMode.EaseIn }));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.00)));
        Timeline.SetDesiredFrameRate(fade, 60);

        PreviewTranslate.BeginAnimation(TranslateTransform.XProperty, move);
        PreviewBorder.BeginAnimation(OpacityProperty, fade);
        await done.Task;

        PreviewTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        PreviewBorder.BeginAnimation(OpacityProperty, null);
        PreviewTranslate.X = 0;
        PreviewBorder.Opacity = 0;
        _acceptAnimationRunning = false;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_lastRender == null || _dpiRefreshQueued || _acceptAnimationRunning) return;
        _dpiRefreshQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _dpiRefreshQueued = false;
            var render = _lastRender;
            if (IsVisible && render != null && !_acceptAnimationRunning)
                ShowPreview(render.Text, render.Caret, render.Dpi, false, render.FontSize,
                    render.Family, render.Weight, render.Italic, render.Source);
        }, System.Windows.Threading.DispatcherPriority.Send);
    }

    private static void Animate(FrameworkElement target, DependencyProperty property, double from, double to, int durationMs, IEasingFunction ease)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        Timeline.SetDesiredFrameRate(animation, 60);
        target.BeginAnimation(property, animation);
    }

    private static void Animate(Animatable target, DependencyProperty property, double from, double to, int durationMs, IEasingFunction ease)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd
        };
        Timeline.SetDesiredFrameRate(animation, 60);
        target.BeginAnimation(property, animation);
    }

    private void StopVisualAnimations()
    {
        PreviewText.BeginAnimation(OpacityProperty, null);
        PreviewText.Opacity = 1;
        PreviewBorder.BeginAnimation(OpacityProperty, null);
        PreviewBorder.BeginAnimation(WidthProperty, null);
        PreviewScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        PreviewScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        PreviewTranslate.BeginAnimation(TranslateTransform.XProperty, null);
    }

    private void PreviewBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        AcceptRequested?.Invoke(this, EventArgs.Empty);
    }

    public void HidePreview()
    {
        if (IsVisible && !_acceptAnimationRunning)
        {
            StopVisualAnimations();
            PreviewBorder.Opacity = 0;
            Hide();
        }
    }
}
