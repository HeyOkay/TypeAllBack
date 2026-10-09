using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace LayoutPreviewPrototype;

internal sealed class SessionDebugWindow : Window
{
    private readonly SessionDiagnostics _diagnostics;
    private readonly Func<SessionDiagnosticFrame?> _capture;
    private readonly DispatcherTimer _refresh = new(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly TextBlock _summary = new() { Text = "Нет активной сессии. Начните набор в поддерживаемом поле.", TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly TextBlock _boundaries = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSteelBlue };
    private readonly TextBox _confirmed = TextField();
    private readonly TextBox _expected = TextField();
    private readonly TextBox _replacement = TextField();
    private readonly TextBox _lastSession = TextField();
    private readonly ListBox _history = new() { FontFamily = new FontFamily("Consolas"), FontSize = 11,
        Background = new SolidColorBrush(Color.FromRgb(22, 25, 31)), Foreground = Brushes.Gainsboro,
        BorderThickness = new Thickness(0) };
    private readonly CheckBox _follow = new() { Content = "Следить за событиями", IsChecked = true };
    private readonly CheckBox _pause = new() { Content = "Пауза просмотра" };
    private SessionDiagnosticFrame? _shown;
    private long _historyRevision = -1;
    private string _shownBoundaries = "";
    private bool _shutdown;

    public SessionDebugWindow(SessionDiagnostics diagnostics, Func<SessionDiagnosticFrame?> capture, SessionStartRules rules)
    {
        _diagnostics = diagnostics; _capture = capture;
        Title = "LayoutPreview — отладка сессии и правила";
        Width = 750; Height = 820; MinWidth = 550; MinHeight = 580;
        ShowActivated = false;
        Background = new SolidColorBrush(Color.FromRgb(30, 34, 42)); Foreground = Brushes.Gainsboro;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 12;
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel();
        header.Children.Add(new TextBlock { Text = "Сессия ввода", FontSize = 22, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Текст хранится только в памяти. Ctrl+Alt+F12 — скрыть / показать окно.",
            Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 4, 0, 10), TextWrapping = TextWrapping.Wrap });
        header.Children.Add(_summary); header.Children.Add(_boundaries);
        root.Children.Add(header);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 8) };
        _pause.Margin = new Thickness(0, 0, 20, 0); controls.Children.Add(_pause); controls.Children.Add(_follow);
        var clear = new Button { Content = "Очистить историю", Margin = new Thickness(20, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
        clear.Click += (_, _) => { _diagnostics.ClearHistory(); _lastSession.Clear(); Refresh(); };
        controls.Children.Add(clear); Grid.SetRow(controls, 1); root.Children.Add(controls);
        var textPanel = new StackPanel();
        var rulePanel = new StackPanel { Margin = new Thickness(10) };
        rulePanel.Children.Add(new TextBlock
        {
            Text = "Каждое правило включается отдельно. Изменение завершает текущую сессию; следующий ввод использует новые настройки. Настройки хранятся до перезапуска.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSteelBlue,
            Margin = new Thickness(0, 0, 0, 8)
        });
        foreach (var rule in rules.Items)
        {
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = rule.Title, FontWeight = FontWeights.SemiBold });
            label.Children.Add(new TextBlock { Text = rule.Description, TextWrapping = TextWrapping.Wrap,
                FontSize = 11, Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 3, 0, 0) });
            var toggle = new CheckBox { Content = label, IsChecked = rule.Enabled,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 7, 0, 7) };
            System.Windows.Automation.AutomationProperties.SetName(toggle, rule.Title);
            toggle.Checked += (_, _) => rules.SetEnabled(rule.Id, true);
            toggle.Unchecked += (_, _) => rules.SetEnabled(rule.Id, false);
            rulePanel.Children.Add(toggle);
        }
        rulePanel.Children.Add(new TextBlock
        {
            Text = "Переключатели управляют только началом сессии. Проверки поля, текста, диапазона и защищённых контекстов обязательны.",
            TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightSteelBlue,
            Margin = new Thickness(0, 8, 0, 0)
        });
        var ruleList = new Expander { Header = "Правила начала сессии", Content = rulePanel,
            IsExpanded = true, Foreground = Brushes.Gainsboro, Margin = new Thickness(0, 0, 0, 8) };
        // Dedicated list above the text fields, with the whole area scrollable on small screens.
        textPanel.Children.Add(ruleList);
        AddField(textPanel, "Подтверждено редактором", _confirmed);
        AddField(textPanel, "Ожидаемый текст после всех захваченных клавиш", _expected);
        AddField(textPanel, "Предполагаемая замена всей сессии", _replacement);
        AddField(textPanel, "Последняя завершённая сессия — ожидаемый текст", _lastSession);
        var scroller = new ScrollViewer { Content = textPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroller, 2); root.Children.Add(scroller);
        var events = new Grid();
        events.RowDefinitions.Add(new() { Height = GridLength.Auto });
        events.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        events.Children.Add(new TextBlock { Text = "События и причины · последние 200", FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 12, 0, 6) });
        Grid.SetRow(_history, 1); events.Children.Add(_history); Grid.SetRow(events, 3); root.Children.Add(events);
        var footer = new TextBlock { Text = "Правила начала доступны в отдельном списке выше. Лимит 10 слов — следующий этап.\n" +
            "Закрытие скрывает окно; утилита продолжает работать. Пауза останавливает только просмотр.",
            Foreground = Brushes.LightSteelBlue, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(footer, 4); root.Children.Add(footer); Content = root;
        _refresh.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _refresh.Start(); } else _refresh.Stop(); };
        Closing += (_, args) => { if (!_shutdown) { args.Cancel = true; Hide(); } };
    }

    private static TextBox TextField() => new() { IsReadOnly = true, AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 13,
        MinHeight = 42, MaxHeight = 120, Padding = new Thickness(8),
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = new SolidColorBrush(Color.FromRgb(22, 25, 31)), Foreground = Brushes.WhiteSmoke,
        BorderBrush = new SolidColorBrush(Color.FromRgb(61, 69, 82)) };

    private static void AddField(Panel panel, string label, TextBox field)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 4) });
        panel.Children.Add(field);
    }

    private void Refresh()
    {
        if (_pause.IsChecked == true) return;
        var frame = _capture();
        if (frame != _shown)
        {
            _shown = frame;
            _summary.Text = frame == null ? "Нет активной сессии. Начните набор в поддерживаемом поле." :
                $"#{frame.Id} · {frame.State}\n{frame.Context}\n{frame.Layouts} · диапазон [{frame.Start}, {frame.End}) · слов: {frame.Words}\n" +
                $"Ожидающих событий: {frame.Pending} · подсказка: {(frame.PreviewVisible ? "видна" : "скрыта")} · ввод {frame.ProcessedSequence}/{frame.CapturedSequence}";
            SetText(_confirmed, frame?.Confirmed ?? ""); SetText(_expected, frame?.Expected ?? "");
            SetText(_replacement, frame?.Replacement ?? "");
        }
        string boundaries = $"Начало: {_diagnostics.StartReason}\nПоследнее окончание: {_diagnostics.LastEndReason}";
        if (_shownBoundaries != boundaries) { _shownBoundaries = boundaries; _boundaries.Text = boundaries; }
        SetText(_lastSession, _diagnostics.LastSession?.Expected ?? "");
        if (_historyRevision == _diagnostics.Revision) return;
        _historyRevision = _diagnostics.Revision;
        var selected = _history.SelectedItem;
        _history.ItemsSource = _diagnostics.Events.ToArray();
        if (selected != null) _history.SelectedItem = selected;
        if (_follow.IsChecked == true && _history.Items.Count > 0) _history.ScrollIntoView(_history.Items[^1]);
    }

    private static void SetText(TextBox field, string text) { if (field.Text != text) field.Text = text; }
    public void Toggle() { if (IsVisible) Hide(); else Show(); }
    public void Shutdown() { _shutdown = true; _refresh.Stop(); Close(); }
}
