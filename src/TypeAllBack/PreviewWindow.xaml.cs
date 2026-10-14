using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace LayoutPreviewPrototype;

public partial class PreviewWindow : Window
{
    public event EventHandler? AcceptRequested;
    internal event Action<string>? ThemeChanged;
    internal event Action<string>? FontMetricsChanged;
    private readonly WindowsPreviewTheme _theme;
    internal string ThemeDescription { get; private set; } = "Тема ещё не определена";

    private PreviewThemePalette? _palette;
    private bool _glassActive;

    private void ApplyTheme(PreviewThemePalette palette)
    {
        _palette = palette;
        ApplyStyle();
    }

    private static SolidColorBrush Solid(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }

    private uint _fieldBackground; // 0xFFRRGGBB sampled from the field; 0 = unknown

    private double _glassShadowOpacity;

    // Colors follow the field the user types in; the Windows theme is only the fallback.
    // Classic: a solid pill slightly lighter/darker than the field. Glass (experimental):
    // tinted acrylic behind a translucent pill, a light top-left edge, a faint bottom-right
    // edge and a short even shadow (the approved mockup, variant B).
    private void ApplyStyle()
    {
        var palette = _palette;
        if (palette == null) return;
        bool contrast = palette.Mode == PreviewThemeMode.HighContrast;
        Color? field = !contrast && _fieldBackground != 0
            ? Color.FromRgb((byte)(_fieldBackground >> 16), (byte)(_fieldBackground >> 8), (byte)_fieldBackground) : null;
        bool dark = field is { } f ? Luminance(f) < 0.5 : palette.Mode == PreviewThemeMode.Dark;
        _glassActive = GlassPreviewOptions.Enabled && !contrast && GlassPreviewOptions.SystemAllows();
        string theme = palette.Mode switch
        {
            PreviewThemeMode.Light => "Светлая тема Windows",
            PreviewThemeMode.Dark => "Тёмная тема Windows",
            _ => "Контрастная тема Windows"
        };
        if (field is { } known) theme += $"; цвет поля #{known.R:X2}{known.G:X2}{known.B:X2}";
        Color toward = dark ? Colors.White : Colors.Black;
        // Without a sampled field the Windows-theme background stands in for it.
        Color basis = field ?? palette.Background;
        if (contrast)
        {
            PreviewBorder.Background = Solid(palette.Background);
            PreviewBorder.BorderBrush = Solid(palette.Border);
            PreviewText.Foreground = Solid(palette.Text);
        }
        else if (_glassActive)
        {
            // The mockup's values: a field-colored fill at 72%/80% with CSS-like inset edges
            // painted over it (GlassSurface, built per size in ShowPreview).
            // On light fields the mockup's glass is slightly cool (fill 246/247/249, text
            // 95/99/104 measured); a cool near-black reproduces that tint.
            Color glassToward = dark ? Colors.White : Color.FromRgb(16, 24, 40);
            _glassFill = WithAlpha(Mix(basis, glassToward, dark ? 0.11 : 0.043), (byte)Math.Round((dark ? 0.72 : 0.80) * 255));
            _glassLit = dark ? Color.FromArgb(0x38, 255, 255, 255) : Color.FromArgb(0xE6, 255, 255, 255);
            _glassShade = dark ? Color.FromArgb(0x14, 255, 255, 255) : Color.FromArgb(0x0F, 0, 0, 0);
            PreviewBorder.Background = Solid(_glassFill);
            PreviewBorder.BorderBrush = Brushes.Transparent;
            PreviewText.Foreground = Solid(Mix(basis, glassToward, dark ? 0.70 : 0.66));
            // WPF's blur spreads less near the edge than CSS 0 0 3px; measured against the
            // reference, these opacities give the same darkening next to the pill.
            _glassShadowOpacity = dark ? 0.36 : 0.18;
            theme += "; стекло";
        }
        else
        {
            if (GlassPreviewOptions.Enabled) theme += "; стекло недоступно (прозрачность Windows выключена), обычный вид";
            if (field is { } c)
            {
                PreviewBorder.Background = Solid(Mix(c, toward, dark ? 0.08 : 0.05));
                PreviewBorder.BorderBrush = Solid(Mix(c, toward, dark ? 0.20 : 0.16));
                PreviewText.Foreground = Solid(Mix(c, toward, dark ? 0.62 : 0.58));
            }
            else
            {
                PreviewBorder.Background = Solid(palette.Background);
                PreviewBorder.BorderBrush = Solid(palette.Border);
                PreviewText.Foreground = Solid(palette.Text);
            }
        }
        if (!_glassActive) GlassShadowClip.Visibility = Visibility.Collapsed;
        ThemeDescription = theme;
        ThemeChanged?.Invoke(ThemeDescription);
    }

    // Blurred black outline of the pill, clipped to the outside of its rounded rectangle.
    private void UpdateGlassShadow(double width, double height)
    {
        if (!_glassActive || width <= 0 || height <= 0) { GlassShadowClip.Visibility = Visibility.Collapsed; return; }
        // Measured against the reference (darkening 7/4/2 next to the edge): a wider, lighter
        // WPF blur matches CSS 0 0 3px better than a dense narrow one.
        double blur = 5 * _layoutScale, pad = blur * 2, radius = PreviewBorder.CornerRadius.TopLeft;
        Canvas.SetLeft(GlassShadowClip, Canvas.GetLeft(PreviewBorder) - pad);
        Canvas.SetTop(GlassShadowClip, Canvas.GetTop(PreviewBorder) - pad);
        GlassShadowClip.Width = width + pad * 2;
        GlassShadowClip.Height = height + pad * 2;
        GlassShadow.Margin = new Thickness(pad);
        GlassShadow.CornerRadius = new CornerRadius(radius);
        GlassShadow.Effect = new BlurEffect { Radius = blur, KernelType = KernelType.Gaussian };
        GlassShadow.Opacity = _glassShadowOpacity;
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, width + pad * 2, height + pad * 2)),
            new RectangleGeometry(new Rect(pad, pad, width, height), radius, radius));
        outside.Freeze();
        GlassShadowClip.Clip = outside;
        GlassShadowClip.Visibility = Visibility.Visible;
    }

    // Stroke-to-x-height ratio of each installed weight of the fallback family, from the
    // outline of 'l' (a plain stem). Rendering thickens stems by roughly 0.4 px with
    // antialiasing; the field's ratio was measured from rendered pixels, so the same
    // allowance is added here at the current pixel size before comparing.
    private static readonly Dictionary<(string, bool), (int Weight, double Stem, double XHeight)[]> StemTables = new();
    private int MatchWeight(string family, bool italic, double target, int fallback)
    {
        if (!StemTables.TryGetValue((family, italic), out var table))
        {
            var faces = new List<(int, double, double)>();
            foreach (int w in new[] { 300, 350, 400, 500, 600, 700 })
            {
                var typeface = new Typeface(new FontFamily(family), italic ? FontStyles.Italic : FontStyles.Normal,
                    FontWeight.FromOpenTypeWeight(w), FontStretches.Normal);
                if (!typeface.TryGetGlyphTypeface(out var glyphs) || glyphs.Weight.ToOpenTypeWeight() != w ||
                    glyphs.XHeight <= 0 || !glyphs.CharacterToGlyphMap.TryGetValue('l', out ushort l)) continue;
                faces.Add((w, glyphs.AdvanceWidths[l] - glyphs.LeftSideBearings[l] - glyphs.RightSideBearings[l], glyphs.XHeight));
            }
            StemTables[(family, italic)] = table = faces.ToArray();
        }
        if (table.Length == 0) return fallback;
        double px = PreviewText.FontSize * VisualTreeHelper.GetDpi(this).DpiScaleY;
        return table.MinBy(face => Math.Abs((face.Stem * px + 0.4) / (face.XHeight * px) - target)).Weight;
    }

    private static double XHeightEm(string family, FontWeight weight, bool italic) =>
        new Typeface(new FontFamily(family), italic ? FontStyles.Italic : FontStyles.Normal, weight, FontStretches.Normal)
            .TryGetGlyphTypeface(out var glyphs) ? glyphs.XHeight : 0;

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private Color _glassFill, _glassLit, _glassShade;

    // The glass surface drawn like Chromium draws the mockup: the fill, then two inset
    // shadows without blur. Each inset is the pill minus the pill shifted by one CSS pixel
    // (1.25 px at 125%), so the edges keep their fractional width and follow the corner
    // curves. Built in DIPs at the pill's exact, pixel-aligned size.
    private Brush GlassSurface(double width, double height, double radius, double edge)
    {
        var outer = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        Geometry Shifted(double dx, double dy) => new RectangleGeometry(new Rect(dx, dy, width, height), radius, radius);
        var group = new DrawingGroup { ClipGeometry = outer };
        group.Children.Add(new GeometryDrawing(Solid(_glassFill), null, outer));
        group.Children.Add(new GeometryDrawing(Solid(_glassLit), null, new CombinedGeometry(GeometryCombineMode.Exclude, outer, Shifted(edge, edge))));
        group.Children.Add(new GeometryDrawing(Solid(_glassShade), null, new CombinedGeometry(GeometryCombineMode.Exclude, outer, Shifted(-edge, -edge))));
        var brush = new DrawingBrush(group)
        {
            Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top,
            Viewbox = new Rect(0, 0, width, height), ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, width, height), ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private void OnGlassChanged(bool enabled) => Dispatcher.BeginInvoke(() =>
    {
        ApplyStyle();
        var render = _lastRender;
        if (IsVisible && render != null && !_acceptAnimationRunning)
            ShowPreview(render.Text, render.Caret, render.Dpi, false, render.FontSize,
                render.Family, render.Weight, render.Italic, render.Source, render.FieldBackground,
                    render.BaselineRatio, render.StemToXHeight, render.XHeightRatio);
    });

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
        string Family, int Weight, bool Italic, string Source, uint FieldBackground,
        double BaselineRatio, double StemToXHeight, double XHeightRatio);
    private RenderRequest? _lastRender;

    public PreviewWindow()
    {
        InitializeComponent();
        _theme = new WindowsPreviewTheme(Dispatcher, ApplyTheme);
        Closed += (_, _) => _theme.Dispose();
        GlassPreviewOptions.Changed += OnGlassChanged;
        Closed += (_, _) => GlassPreviewOptions.Changed -= OnGlassChanged;
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
        double fontSizeDip = 0, string fontFamilyName = "", int fontWeight = 400, bool fontItalic = false, string fontSource = "",
        uint fieldBackground = 0, double baselineRatio = 0, double stemToXHeight = 0,
        double xHeightRatio = 0)
    {
        if (_acceptAnimationRunning)
            return;
        if (fieldBackground != _fieldBackground) { _fieldBackground = fieldBackground; ApplyStyle(); }
        using var measurement = PerformanceDiagnostics.Measure("preview-render");
        bool textChanged = _lastRender?.Text != text;
        _lastRender = new(text, caretPx, targetDpi, fontSizeDip, fontFamilyName, fontWeight, fontItalic, fontSource, fieldBackground,
            baselineRatio, stemToXHeight, xHeightRatio);
        new WindowInteropHelper(this).EnsureHandle();
        double overlayScale = VisualTreeHelper.GetDpi(this).DpiScaleY;
        _layoutScale = PreviewMetrics.OverlayScaleRatio(targetDpi, overlayScale);

        var fontChoice = PreviewFontResolver.Resolve(fontFamilyName, fontWeight, fontItalic, text);
        PreviewText.FontFamily = new FontFamily(fontChoice.Used);
        if (animateFirstAppearance || fontSource.Length > 0)
        {
            int weight = fontWeight is >= 100 and <= 900 ? fontWeight : 400;
            // A substituted family takes the fallback weight whose stroke density matches the
            // field's measured glyphs (GlyphCalibration), not the reported weight number.
            if (stemToXHeight > 0 && fontChoice.Substituted)
                weight = MatchWeight(fontChoice.Used, fontItalic, stemToXHeight, weight);
            PreviewText.FontWeight = FontWeight.FromOpenTypeWeight(weight);
            PreviewText.FontStyle = fontItalic ? FontStyles.Italic : FontStyles.Normal;
        }
        // Proportions of the approved mockup: wider sides, almost no vertical padding (the
        // text line already has its own leading), and a frame of exactly one physical pixel
        // (a scaled 1.25 px line blurs into a soft outline).
        // Measured against the browser-rendered reference: Chromium snaps a 1 px border to
        // one device pixel (classic), but draws a 1 px inset shadow at the full scaled width
        // (125%: one full and one quarter pixel), so the glass edge scales with the field.
        // Glass has no border: its inset edges are painted inside the fill (GlassSurface).
        // WPF's Segoe UI line box is one device pixel taller than Chromium's (measured), so
        // glass takes that pixel from the bottom padding to match the reference height.
        double side = _glassActive ? 7 : 6;
        PreviewBorder.Padding = new Thickness(side * _layoutScale, 1 * _layoutScale, side * _layoutScale,
            Math.Max(0, 2 * _layoutScale - (_glassActive ? 1 / overlayScale : 0)));
        PreviewBorder.BorderThickness = new Thickness(_glassActive ? 0 : 1 / overlayScale);
        // Glass follows the compositor's rounded corners of the backdrop window.
        PreviewBorder.CornerRadius = new CornerRadius((_glassActive ? 8 : 6) * _layoutScale);
        // Whole device pixels, matching the SetWindowPos offsets below. A half-pixel position
        // (125%: 170 DIP = 212.5 px) smeared the one-pixel edges against the browser reference.
        double pixelScale = Math.Max(1.0, targetDpi / 96.0);
        Canvas.SetLeft(PreviewBorder, Math.Round(LeftReserveDip * pixelScale) / overlayScale);
        Canvas.SetTop(PreviewBorder, Math.Round(VerticalReserveDip * pixelScale) / overlayScale);
        // Glass edges are fractional (1.25 px at 125%) like Chromium's inset shadows; snapping
        // would cut them to one pixel. The classic frame stays snapped like a CSS border.
        PreviewBorder.SnapsToDevicePixels = !_glassActive;

        PreviewBorder.BeginAnimation(WidthProperty, null);
        PreviewBorder.Width = double.NaN;
        double sourceSize = PreviewMetrics.ResolveFontSize(fontSizeDip, caretPx.Bottom - caretPx.Top,
            targetDpi, PreviewText.FontSize / _layoutScale, animateFirstAppearance, PreviewMetrics.HasReliableFormatting(fontSource), fontSource);
        PreviewText.FontSize = sourceSize * _layoutScale;
        // A substituted family is sized by the field's measured x-height (what the eye reads
        // as size): fonts that match in word width can still differ by 10-15% in x-height.
        if (xHeightRatio > 0 && fontChoice.Substituted &&
            XHeightEm(fontChoice.Used, PreviewText.FontWeight, fontItalic) is > 0 and var xEm)
        {
            double targetPx = xHeightRatio * Math.Max(1, caretPx.Bottom - caretPx.Top);
            double size = targetPx / (xEm * overlayScale);
            if (double.IsFinite(size) && size / _layoutScale is >= 6 and <= 96) PreviewText.FontSize = size;
        }
        PreviewText.Text = text.TrimEnd();
        // Measure the actual Display-mode TextBlock. FormattedText used different
        // glyph metrics and could leave an apparent extra blank at the right edge.
        PreviewText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // Whole device pixels as in the browser, so the outer edges fall on pixel boundaries
        // (full edge pixel plus the fractional remainder inside, as in the reference).
        double targetWidth = Math.Ceiling((PreviewText.DesiredSize.Width + PreviewBorder.Padding.Left +
            PreviewBorder.Padding.Right + PreviewBorder.BorderThickness.Left + PreviewBorder.BorderThickness.Right) * overlayScale) / overlayScale;
        double targetHeight = Math.Round((PreviewText.DesiredSize.Height + PreviewBorder.Padding.Top +
            PreviewBorder.Padding.Bottom + PreviewBorder.BorderThickness.Top + PreviewBorder.BorderThickness.Bottom) * overlayScale) / overlayScale;
        double oldWidth = PreviewBorder.ActualWidth > 0 ? PreviewBorder.ActualWidth : targetWidth;

        PreviewBorder.Width = targetWidth;
        PreviewBorder.Height = targetHeight;
        if (_glassActive)
            PreviewBorder.Background = GlassSurface(targetWidth, targetHeight, PreviewBorder.CornerRadius.TopLeft, _layoutScale);
        // Whole device pixels for the window too: a fractional layered-window width stretched
        // the content by a fraction of a pixel and shifted the left edge by half a pixel.
        Height = Math.Round(Math.Max(40 * _layoutScale, targetHeight + VerticalReserveDip * 2 * _layoutScale) * overlayScale) / overlayScale;
        Width = Math.Round((LeftReserveDip * _layoutScale + targetWidth + 8 * _layoutScale) * overlayScale) / overlayScale;
        RootCanvas.Width = Width;
        RootCanvas.Height = Height;

        if (!IsVisible)
            Show();
        FieldColors.PreviewVisible = true;

        // caretPx is Win32 PHYSICAL pixels. Position the WPF HWND with SetWindowPos in
        // physical pixels too, so Windows scaling cannot multiply the coordinates again.
        double scale = Math.Max(1.0, targetDpi / 96.0);
        int reservePx = (int)Math.Round(LeftReserveDip * scale);
        int verticalReservePx = (int)Math.Round(VerticalReserveDip * scale);
        int previewHeightPx = (int)Math.Round(targetHeight * overlayScale);
        int caretHeightPx = Math.Max(1, caretPx.Bottom - caretPx.Top);
        int gapPx = Math.Max(1, (int)Math.Round(3 * scale)); // mockup: 3 px after the caret

        int visibleLeftPx = caretPx.Right + gapPx;
        int visibleTopPx = caretPx.Top + (caretHeightPx - previewHeightPx) / 2;
        if (baselineRatio > 0)
        {
            // Measured baseline of the field font: put our text on the same line instead of
            // centering the pill (fonts split the line box differently above and below).
            var typeface = new Typeface(PreviewText.FontFamily, PreviewText.FontStyle, PreviewText.FontWeight, PreviewText.FontStretch);
            var line = new FormattedText(PreviewText.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                typeface, PreviewText.FontSize, Brushes.Black, overlayScale);
            double textBaselinePx = (PreviewBorder.BorderThickness.Top + PreviewBorder.Padding.Top + line.Baseline) * overlayScale;
            visibleTopPx = (int)Math.Round(caretPx.Top + baselineRatio * caretHeightPx - textBaselinePx);
        }
        int windowLeftPx = visibleLeftPx - reservePx;
        int windowTopPx = visibleTopPx - verticalReservePx;

        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, windowLeftPx, windowTopPx, 0, 0,
            Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        UpdateGlassShadow(targetWidth, targetHeight);
        string fontDiagnostic = $"source={fontSource} sizeBasis={(PreviewMetrics.NeedsOmniboxFallback(fontSource, fontSizeDip) ? "Chromium-desktop-default-14dip" : "font-metadata-or-geometry")} reportedFamily={fontFamilyName} usedFamily={fontChoice.Used} fallbackReason={fontChoice.Reason} fontKind={(fontChoice.Composite ? "composite" : "physical")} reportedDip={fontSizeDip:F2} resolvedDip={sourceSize:F2} renderedDip={PreviewText.FontSize:F2} sourceDpi={targetDpi} overlayScale={overlayScale:F2} caretPixels={caretHeightPx} textPixels={PreviewText.DesiredSize.Height * overlayScale:F2} weight={fontWeight} italic={fontItalic}";
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
            // The clipped glass shadow cannot scale with the pill, so glass skips the zoom-in.
            double startScale = _glassActive ? 1.0 : 0.96;
            PreviewScale.ScaleX = PreviewScale.ScaleY = startScale;
            PreviewTranslate.X = 0;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            // Keep the background opaque throughout the entrance so following
            // document text is covered; only the inactive label fades into view.
            Animate(PreviewText, UIElement.OpacityProperty, 0, 1, EnterDurationMs, ease);
            Animate(PreviewScale, ScaleTransform.ScaleXProperty, startScale, 1.0, EnterDurationMs, ease);
            Animate(PreviewScale, ScaleTransform.ScaleYProperty, startScale, 1.0, EnterDurationMs, ease);
        }
        else
        {
            PreviewBorder.BeginAnimation(OpacityProperty, null);
            PreviewBorder.Opacity = RestingOpacity;
            PreviewText.BeginAnimation(OpacityProperty, null);
            PreviewText.Opacity = 1;
            PreviewScale.ScaleX = PreviewScale.ScaleY = 1;
            PreviewTranslate.X = 0;

            if (textChanged && !_glassActive && SystemParameters.ClientAreaAnimation && Math.Abs(targetWidth - oldWidth) > 0.5)
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
        // The clipped shadow cannot follow the sliding pill.
        GlassShadowClip.Visibility = Visibility.Collapsed;
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
                    render.Family, render.Weight, render.Italic, render.Source, render.FieldBackground,
                    render.BaselineRatio, render.StemToXHeight, render.XHeightRatio);
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
        if (!_acceptAnimationRunning) FieldColors.PreviewVisible = false;
        if (IsVisible && !_acceptAnimationRunning)
        {
            StopVisualAnimations();
            PreviewBorder.Opacity = 0;
            Hide();
        }
    }
}
