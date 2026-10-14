using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace LayoutPreviewPrototype;

// Test fields from the preview mockup: dark (Discord-like), light (Google-like) and colored.
// Opened from the tray as a second process of the same executable (--field-tester): the
// running utility ignores focus events of its own windows, as with any real application.
// Every field starts with a sample phrase and shows a pinned preview, rendered by the real
// PreviewWindow with the field's exact color, which never disappears: the look can be
// judged without typing. Typing still triggers the live preview of the running utility.
internal static class FieldTesterWindow
{
    internal const string Argument = "--field-tester";
    private const string SampleText = "ш дщму фтеркщзшс";
    private static readonly List<(TextBox Box, uint Background, PreviewWindow Preview)> Pinned = [];

    internal static Window Create()
    {
        var rows = new StackPanel { Margin = new Thickness(32, 24, 32, 16) };
        rows.Children.Add(new TextBlock
        {
            Text = "В каждом поле закреплена подсказка в оформлении TypeAllBack, она не исчезает. Набор в полях по-прежнему вызывает обычную подсказку работающей утилиты. Поля ничего не сохраняют.",
            FontSize = 13, Foreground = Solid("#57606A"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
        });
        var glass = new CheckBox
        {
            Content = "Стеклянная подсказка (только в этом окне, настройка трея не меняется)",
            IsChecked = GlassPreviewOptions.Enabled, FontSize = 13, Margin = new Thickness(0, 0, 0, 16)
        };
        glass.Checked += (_, _) => GlassPreviewOptions.SetEnabled(true);
        glass.Unchecked += (_, _) => GlassPreviewOptions.SetEnabled(false);
        rows.Children.Add(glass);
        rows.Children.Add(Row("Тёмное поле", "как Discord, #383A40", "#313338", "#383A40", "#DBDEE1", 8, false));
        rows.Children.Add(Row("Светлое поле", "как поиск Google, #FFFFFF", "#F8F9FA", "#FFFFFF", "#202124", 22, true));
        rows.Children.Add(Row("Цветное поле", "тёмно-синее, #17212B", "#0E1621", "#17212B", "#F5F5F5", 8, false));
        rows.Children.Add(Row("Пёстрый фон", "неоновый леопард: у поля нет одного цвета", "#000000", "#000000", "#FFFFFF", 8, false, TestBackground()));
        var window = new Window
        {
            Title = "TypeAllBack — тестовые поля",
            Width = 760,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.CanMinimize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = Solid("#ECEDEF"),
            FontFamily = new FontFamily("Segoe UI"),
            Content = rows
        };
        void Refresh() => window.Dispatcher.BeginInvoke(() => RefreshPinned(window), System.Windows.Threading.DispatcherPriority.Background);
        window.ContentRendered += (_, _) => Refresh();
        window.LocationChanged += (_, _) => Refresh();
        window.SizeChanged += (_, _) => Refresh();
        window.StateChanged += (_, _) => Refresh();
        // Previews are topmost windows: pin them only while this window is in front, so
        // they never float over other applications.
        window.Activated += (_, _) => Refresh();
        window.Deactivated += (_, _) => Refresh();
        window.Closed += (_, _) => { foreach (var pinned in Pinned) pinned.Preview.Close(); };
        foreach (var pinned in Pinned) pinned.Box.TextChanged += (_, _) => Refresh();
        return window;
    }

    // Each pinned preview sits after the end of its field's text, like a live one.
    private static void RefreshPinned(Window window)
    {
        bool shown = window.IsVisible && window.IsActive && window.WindowState != WindowState.Minimized;
        uint dpi = (uint)Math.Round(VisualTreeHelper.GetDpi(window).PixelsPerInchY);
        foreach (var (box, background, preview) in Pinned)
        {
            string hint = Hint(box.Text);
            if (!shown || hint.Length == 0 || PresentationSource.FromVisual(box) == null) { preview.HidePreview(); continue; }
            var end = box.GetRectFromCharacterIndex(box.Text.Length);
            if (end.IsEmpty || !double.IsFinite(end.Top)) { preview.HidePreview(); continue; }
            var top = box.PointToScreen(end.TopLeft);
            var bottom = box.PointToScreen(end.BottomLeft);
            int x = (int)Math.Round(top.X);
            var caret = new Native.RECT { Left = x, Right = x + 1, Top = (int)Math.Round(top.Y), Bottom = (int)Math.Round(bottom.Y) };
            preview.ShowPreview(hint, caret, dpi, false, box.FontSize, "Segoe UI", 400, false, "UIA character", background);
        }
    }

    // The last word typed in the Russian layout, converted to the English one, as the
    // utility would suggest for "ш дщму фтеркщзшс" -> "anthropic".
    private static readonly Lazy<Dictionary<char, char>> RussianToEnglish = new(() =>
    {
        var map = new Dictionary<char, char>();
        for (uint vk = 0x20; vk <= 0xDE; vk++)
            foreach (bool shift in new[] { false, true })
            {
                var key = new PhysicalKey(vk, shift, false);
                if (KeyboardConversion.TryConvert(key, KeyboardLayout.Russian, out char ru) &&
                    KeyboardConversion.TryConvert(key, KeyboardLayout.English, out char en))
                    map.TryAdd(ru, en);
            }
        return map;
    });

    private static string Hint(string text)
    {
        string converted = new string(text.Select(c => RussianToEnglish.Value.TryGetValue(c, out char en) ? en : c).ToArray()).TrimEnd();
        return converted[(converted.LastIndexOf(' ') + 1)..];
    }

    private static uint Argb(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return 0xFF000000u | (uint)color.R << 16 | (uint)color.G << 8 | color.B;
    }

    private static UIElement Row(string title, string detail, string panel, string field, string text, double radius, bool shadow,
        Brush? pattern = null)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Solid("#1F2328") });
        label.Children.Add(new TextBlock { Text = detail, FontSize = 13, Foreground = Solid("#57606A"), Margin = new Thickness(0, 4, 0, 0) });

        var input = new TextBox
        {
            Background = Brushes.Transparent,
            Foreground = Solid(text),
            CaretBrush = Solid(text),
            BorderThickness = new Thickness(0),
            FontSize = 16,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(input, title);
        input.Text = SampleText;
        input.CaretIndex = SampleText.Length;
        // A patterned field has no single color: the preview uses its fallback, as in real use.
        Pinned.Add((input, pattern == null ? Argb(field) : 0, new PreviewWindow()));
        var fieldBox = new Border
        {
            Background = pattern ?? Solid(field),
            CornerRadius = new CornerRadius(radius),
            Height = 44,
            Padding = new Thickness(radius > 12 ? 18 : 14, 0, 14, 0),
            Child = input
        };
        var stack = new Grid();
        if (shadow)
        {
            // A separate layer, so the text itself keeps ClearType and gets no shadow.
            stack.Children.Add(new Border
            {
                Background = pattern ?? Solid(field),
                CornerRadius = new CornerRadius(radius),
                Height = 44,
                Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Direction = 270, Opacity = 0.28, Color = Color.FromRgb(32, 33, 36) }
            });
        }
        stack.Children.Add(fieldBox);

        var surround = new Border
        {
            Background = pattern ?? Solid(panel),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(18, 24, 18, 24),
            Child = stack
        };
        Grid.SetColumn(surround, 1);
        grid.Children.Add(label);
        grid.Children.Add(surround);
        return grid;
    }

    // The user's local image (Debug output only, git-ignored) when present; otherwise the
    // pattern drawn in code below.
    private static Brush TestBackground()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "TestAssets", "leopard.png");
        if (!File.Exists(path)) return Leopard();
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            var brush = new ImageBrush(image)
            {
                TileMode = TileMode.Tile, Stretch = Stretch.Fill,
                Viewport = new Rect(0, 0, image.Width, image.Height), ViewportUnits = BrushMappingMode.Absolute
            };
            brush.Freeze();
            return brush;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UriFormatException or InvalidOperationException or FileFormatException)
        { return Leopard(); }
    }

    // A busy, high-contrast pattern drawn in code (no bundled image): the field has no
    // dominant color, so the preview must fall back gracefully. Spots are repeated across
    // the tile edges, so the tiling has no seams.
    private static Brush Leopard()
    {
        const double tile = 160;
        var random = new Random(7);
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, tile, tile)) };
        Brush green = Solid("#8CF20A"), black = Solid("#000000"), pink = Solid("#FF1F6E");
        group.Children.Add(new GeometryDrawing(green, null, new RectangleGeometry(new Rect(0, 0, tile, tile))));
        for (int i = 0; i < 28; i++)
        {
            double x = random.NextDouble() * tile, y = random.NextDouble() * tile;
            double rx = 9 + random.NextDouble() * 11, ry = 7 + random.NextDouble() * 9, angle = random.NextDouble() * 180;
            bool rosette = random.NextDouble() < 0.5;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var center = new Point(x + dx * tile, y + dy * tile);
                var turn = new RotateTransform(angle, center.X, center.Y);
                group.Children.Add(new GeometryDrawing(black, null, new EllipseGeometry(center, rx, ry, turn)));
                if (rosette)
                    group.Children.Add(new GeometryDrawing(pink, null, new EllipseGeometry(center, rx * 0.5, ry * 0.42, turn)));
            }
        }
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None,
            Viewbox = new Rect(0, 0, tile, tile), ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, tile, tile), ViewportUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Solid(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
