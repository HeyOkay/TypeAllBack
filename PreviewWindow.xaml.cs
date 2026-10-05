using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LayoutPreviewPrototype;

public partial class PreviewWindow : Window
{
    public event EventHandler? AcceptRequested;

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

    public PreviewWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var ex = Native.GetWindowLong(_hwnd, GWL_EXSTYLE);
            Native.SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };
    }

    internal void ShowPreview(string text, Native.RECT caretPx, uint targetDpi, bool animateFirstAppearance)
    {
        if (_acceptAnimationRunning)
            return;

        PreviewText.Text = text;
        PreviewBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double targetWidth = Math.Ceiling(PreviewBorder.DesiredSize.Width);
        double targetHeight = Math.Ceiling(PreviewBorder.DesiredSize.Height);
        double oldWidth = PreviewBorder.ActualWidth > 0 ? PreviewBorder.ActualWidth : targetWidth;

        PreviewBorder.Width = targetWidth;
        Height = Math.Max(40, targetHeight + VerticalReserveDip * 2);
        Width = LeftReserveDip + targetWidth + 8;
        RootCanvas.Width = Width;
        RootCanvas.Height = Height;

        if (!IsVisible)
            Show();

        // caretPx is Win32 PHYSICAL pixels. Position the WPF HWND with SetWindowPos in
        // physical pixels too, so Windows scaling cannot multiply the coordinates again.
        double scale = Math.Max(1.0, targetDpi / 96.0);
        int reservePx = (int)Math.Round(LeftReserveDip * scale);
        int verticalReservePx = (int)Math.Round(VerticalReserveDip * scale);
        int previewHeightPx = (int)Math.Round(targetHeight * scale);
        int caretHeightPx = Math.Max(1, caretPx.Bottom - caretPx.Top);
        int gapPx = Math.Max(5, (int)Math.Round(6 * scale));

        int visibleLeftPx = caretPx.Right + gapPx;
        int visibleTopPx = caretPx.Top + (caretHeightPx - previewHeightPx) / 2;
        int windowLeftPx = visibleLeftPx - reservePx;
        int windowTopPx = visibleTopPx - verticalReservePx;

        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, windowLeftPx, windowTopPx, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        if (animateFirstAppearance)
        {
            StopVisualAnimations();
            PreviewBorder.Opacity = 0;
            PreviewScale.ScaleX = PreviewScale.ScaleY = 0.96;
            PreviewTranslate.X = 0;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Animate(PreviewBorder, UIElement.OpacityProperty, 0, RestingOpacity, EnterDurationMs, ease);
            Animate(PreviewScale, ScaleTransform.ScaleXProperty, 0.96, 1.0, EnterDurationMs, ease);
            Animate(PreviewScale, ScaleTransform.ScaleYProperty, 0.96, 1.0, EnterDurationMs, ease);
        }
        else
        {
            PreviewBorder.BeginAnimation(OpacityProperty, null);
            PreviewBorder.Opacity = RestingOpacity;
            PreviewScale.ScaleX = PreviewScale.ScaleY = 1;
            PreviewTranslate.X = 0;

            if (Math.Abs(targetWidth - oldWidth) > 0.5)
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
        if (!IsVisible || _acceptAnimationRunning)
            return;

        _acceptAnimationRunning = true;
        StopVisualAnimations();
        PreviewBorder.Opacity = RestingOpacity;
        PreviewTranslate.X = 0;

        // Move the visual inside a stationary HWND. This is much smoother than animating
        // Window.Left, especially for AllowsTransparency windows.
        double travel = Math.Max(30, Math.Min(PreviewBorder.ActualWidth + 8, LeftReserveDip - 8));
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

    public static int AcceptMidpointDelayMs => AcceptDurationMs / 2;

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
