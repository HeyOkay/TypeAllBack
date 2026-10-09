using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace LayoutPreviewPrototype;

internal sealed class UiaTextEditor : IDisposable
{
    private const int MaximumText = 65536;
    private readonly UiaRangeFont _rangeFont = new();
    private int _rangeExperimentRevision = -1;
    private bool _initialRangeProbeUsed;
    public UiaTextEditor() { _rangeFont.StatusChanged += RangeFontExperiment.Report; }
    private string _nativeCaptureFailure = "Native-снимок недоступен";
    private string _lastCaptureFailure = "Снимок ещё не получен";
    public string LastCaptureFailure => Volatile.Read(ref _lastCaptureFailure);
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private EditorSnapshot? _snapshot;
    private volatile bool _stopping, _paused;
    internal bool IsPaused => _paused;
    internal void SetPaused(bool paused)
    {
        _paused = paused;
        Volatile.Write(ref _snapshot, null);
        Interlocked.Increment(ref _focusGeneration);
        _wake.Set();
    }
    private Thread? _worker;
    private AutomationFocusChangedEventHandler? _focusChanged;
    private static string _lastRejectedContext = "";
    private static long _lastRejectionAt;
    private EditorSnapshot? _formattingSnapshot;
    private long _formattingAt;
    private int _focusGeneration, _formattingGeneration;
    private long _lastInputAt = long.MinValue / 2, _revision;
    public event Action? SnapshotChanged;
    private string _boundContext = "";
    private AutomationElement? _boundElement;
    private TextPattern? _boundPattern;
    private long _bindingRejectedAt;
    private readonly ShellDiscoveryBudget _shellDiscovery = new();
    private AutomationElement? _eventElement;
    private AutomationEventHandler? _textChanged;
    private AutomationElement? _nativeElement;
    private IntPtr _nativeElementHandle;
    private int _nativeElementGeneration = -1;
    public void NotifyInput() { Interlocked.Exchange(ref _lastInputAt, Environment.TickCount64); _wake.Set(); }

    private void Publish(EditorSnapshot? snapshot)
    {
        var previous = Volatile.Read(ref _snapshot);
        bool changed = snapshot == null ? previous != null : previous == null ||
            previous with { CapturedAt = snapshot.CapturedAt, Revision = 0 } != snapshot with { Revision = 0 };
        if (snapshot != null) snapshot = snapshot with { Revision = changed ? ++_revision : _revision };
        Volatile.Write(ref _snapshot, snapshot);
        if (changed) SnapshotChanged?.Invoke();
    }

    public void Start()
    {
        if (_worker != null) return;
        _worker = new Thread(Run) { IsBackground = true, Name = "LayoutPreview UIA" };
        _worker.SetApartmentState(ApartmentState.MTA);
        _worker.Start();
    }

    private void Run()
    {
        _focusChanged = (_, _) => { Interlocked.Increment(ref _focusGeneration); Volatile.Write(ref _snapshot, null); _wake.Set(); SnapshotChanged?.Invoke(); };
        try { Automation.AddAutomationFocusChangedEventHandler(_focusChanged); }
        catch (COMException) { /* Polling remains available if focus events are unavailable. */ }
        long nextCaptureAt = 0;
        int lastCaptureGeneration = -1;
        while (!_stopping)
        {
            if (_paused)
            {
                Publish(null);
                SetTextEvents(null);
                _wake.WaitOne();
                continue;
            }
            while (_commands.TryDequeue(out var command)) command();
            int generation = Volatile.Read(ref _focusGeneration);
            long now = Environment.TickCount64;
            if (generation == lastCaptureGeneration && now < nextCaptureAt)
            { _wake.WaitOne((int)(nextCaptureAt - now)); continue; }
            if (generation != lastCaptureGeneration) SetTextEvents(null);
            lastCaptureGeneration = generation;
            long started = Stopwatch.GetTimestamp();
            Capture(out var snapshot, out _, out _, out _);
            double captureMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            PerformanceDiagnostics.Observe("capture", captureMs);
            if (_paused || generation != Volatile.Read(ref _focusGeneration)) continue;
            Publish(snapshot);
            PerformanceDiagnostics.Flush();
            nextCaptureAt = Environment.TickCount64 + (captureMs >= 50 ? 80 : 35);
            _wake.WaitOne(EditorPolling.Delay(Environment.TickCount64, Interlocked.Read(ref _lastInputAt), snapshot != null, captureMs));
        }
        Volatile.Write(ref _snapshot, null);
        SetTextEvents(null);
        try { Automation.RemoveAutomationFocusChangedEventHandler(_focusChanged); }
        catch (COMException) { }
    }

    public bool TryReadCached(out EditorSnapshot snapshot)
    {
        snapshot = Volatile.Read(ref _snapshot)!;
        if (_paused) return false;
        if (snapshot == null || Environment.TickCount64 - snapshot.CapturedAt > EditorPolling.MaximumSnapshotAgeMs ||
            Native.GetForegroundWindow() != snapshot.Foreground) return false;
        var gui = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(Native.GetWindowThreadProcessId(snapshot.Foreground, IntPtr.Zero), ref gui) ||
            (gui.hwndFocus == IntPtr.Zero ? snapshot.Foreground : gui.hwndFocus) != snapshot.Editor ||
            (gui.flags & Native.GUI_INMOVESIZE) != 0) return false;
        snapshot = snapshot with { Layout = Native.GetKeyboardLayout(snapshot.Thread) };
        return true;
    }

    public bool TryEnrichNativeSnapshot(EditorSnapshot current, out EditorSnapshot enriched)
    {
        var cached = Volatile.Read(ref _snapshot);
        long now = Environment.TickCount64;
        if (TryMergeNativeGeometry(current, cached, now, out enriched)) return true;
        // Font metadata is independent of document/caret freshness. Geometry is
        // still merged only when the exact text and selection match.
        return TryMergeNativeFormatting(current, cached, now, out enriched);
    }

    internal static bool TryMergeNativeFormatting(EditorSnapshot current, EditorSnapshot? cached, long now,
        out EditorSnapshot enriched)
    {
        enriched = current;
        if (cached == null || cached.AutomationIdentity.Length != 0 ||
            now < cached.CapturedAt || now - cached.CapturedAt > 200 ||
            !TextEditor.SameEditor(current, cached) || current.Dpi != cached.Dpi || cached.FontSizeDip <= 0)
            return false;
        enriched = CopyFormatting(current, cached);
        return true;
    }

    private static EditorSnapshot CopyFormatting(EditorSnapshot target, EditorSnapshot source) => target with
    {
        FontSizeDip = source.FontSizeDip, FontFamilyName = source.FontFamilyName,
        FontWeightValue = source.FontWeightValue, FontItalic = source.FontItalic, FontSource = source.FontSource
    };

    private EditorSnapshot WithFormatting(EditorSnapshot context, bool presentation, Func<EditorSnapshot> read, bool initialRangeProbe = false)
    {
        int generation = Volatile.Read(ref _focusGeneration);
        if (_rangeExperimentRevision != RangeFontExperiment.Revision)
        { _rangeExperimentRevision = RangeFontExperiment.Revision; _formattingSnapshot = null; _formattingAt = 0; }
        bool sameContext = _formattingSnapshot != null && _formattingGeneration == generation &&
            TextEditor.SameEditor(context, _formattingSnapshot) && context.Dpi == _formattingSnapshot.Dpi;
        if (!sameContext) { _formattingSnapshot = null; _initialRangeProbeUsed = false; }
        bool earlyRangeProbe = initialRangeProbe && !_initialRangeProbeUsed;
        // Text/caret polling and acceptance never depend on these slower attributes.
        // Refresh twice per second, with one early bootstrap probe at two characters.
        // This runs on the worker; it never waits on the keyboard/UI thread.
        if (presentation && (!sameContext || earlyRangeProbe || Environment.TickCount64 - _formattingAt >= 500))
        {
            if (earlyRangeProbe) _initialRangeProbeUsed = true;
            using var measurement = PerformanceDiagnostics.Measure("formatting");
            _formattingAt = Environment.TickCount64;
            _formattingGeneration = generation;
            var candidate = context;
            try { candidate = read(); }
            catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or ElementNotAvailableException)
            { /* Unavailable formatting must not invalidate verified text or the last font. */ }
            if (candidate.FontSizeDip > 0 || _formattingSnapshot == null || _formattingSnapshot.FontSizeDip <= 0)
                _formattingSnapshot = candidate with { Text = "" };
        }
        return _formattingSnapshot == null ? context : CopyFormatting(context, _formattingSnapshot);
    }

    internal static bool TryMergeNativeGeometry(EditorSnapshot current, EditorSnapshot? cached, long now, out EditorSnapshot enriched)
    {
        enriched = current;
        if (cached == null || cached.AutomationIdentity.Length != 0 ||
            now - cached.CapturedAt > 200 || now < cached.CapturedAt ||
            !TextEditor.SameEditor(current, cached) || current.Text != cached.Text ||
            current.SelectionStart != cached.SelectionStart || current.SelectionEnd != cached.SelectionEnd ||
            current.Dpi != cached.Dpi) return false;
        enriched = current with
        {
            Caret = current.Caret.Bottom > current.Caret.Top ? current.Caret : cached.Caret,
            FontSizeDip = cached.FontSizeDip,
            FontFamilyName = cached.FontFamilyName,
            FontWeightValue = cached.FontWeightValue,
            FontItalic = cached.FontItalic,
            FontSource = cached.FontSource
        };
        return true;
    }

    public async Task<bool> TryForwardEnterAsync(EditorSnapshot expected, Func<bool> stillValid)
    {
        using var cancellation = new CancellationTokenSource();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(() =>
        {
            try
            {
                using var measurement = PerformanceDiagnostics.Measure("enter-replay");
                bool Valid() => !cancellation.IsCancellationRequested && stillValid();
                bool confirmed = Valid() && Capture(out var current, out _, out _, out _, false) &&
                    current != null && EnterReplay.Matches(expected, current);
                if (!confirmed || !Valid() || Native.GetForegroundWindow() != expected.Foreground ||
                    Native.IsKeyDown(Native.VK_SHIFT) || Native.IsKeyDown(Native.VK_CONTROL) ||
                    Native.IsKeyDown(Native.VK_MENU) || Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN))
                { result.TrySetResult(false); return; }
                var gui = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
                bool sameFocus = Native.GetGUIThreadInfo(Native.GetWindowThreadProcessId(expected.Foreground, IntPtr.Zero), ref gui) &&
                    (gui.hwndFocus == IntPtr.Zero ? expected.Foreground : gui.hwndFocus) == expected.Editor;
                // No retries: an ambiguous partial SendInput cannot send another Enter.
                result.TrySetResult(sameFocus && Valid() && Native.GetForegroundWindow() == expected.Foreground && EnterReplay.Send());
            }
            catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or ElementNotAvailableException)
            { result.TrySetResult(false); }
        });
        _wake.Set();
        try { return await result.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { cancellation.Cancel(); return false; }
    }

    public async Task<bool> TryReplaceAsync(EditorSnapshot expected, InputSession session, IntPtr layout, Func<bool> stillValid)
    {
        using var cancellation = new CancellationTokenSource();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(() =>
        {
            try
            {
                using var measurement = PerformanceDiagnostics.Measure("replace");
                bool Valid() => !cancellation.IsCancellationRequested && stillValid();
                bool replaced = expected.AutomationIdentity.Length == 0
                    ? Valid() && NotepadEditor.TryReplace(expected, session, layout, Valid)
                    : Replace(expected, session, layout, Valid);
                if (replaced && Capture(out var after, out _, out _, out _, false)) Publish(after);
                result.TrySetResult(replaced);
            }
            catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or ElementNotAvailableException)
            { result.TrySetResult(false); }
        });
        _wake.Set();
        try { return await result.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { cancellation.Cancel(); return false; }
    }

    public async Task<bool> TrySwitchLayoutAsync(EditorSnapshot expected, InputSession session, IntPtr layout)
    {
        using var cancellation = new CancellationTokenSource();
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(() =>
        {
            try
            {
                using var measurement = PerformanceDiagnostics.Measure("switch-layout");
                bool switched = false;
                if (!cancellation.IsCancellationRequested && expected.AutomationIdentity.Length == 0)
                    switched = NotepadEditor.TrySwitchLayoutAsync(expected, session, layout).GetAwaiter().GetResult();
                else if (!cancellation.IsCancellationRequested && Capture(out var current, out _, out _, out _, false) &&
                    current != null && TextEditor.SameEditor(expected, current) &&
                    session.Matches(current.Text, current.SelectionStart, current.SelectionEnd))
                {
                    foreach (var hwnd in new[] { current.Foreground, current.Editor }.Distinct())
                    {
                        if (cancellation.IsCancellationRequested || Native.GetForegroundWindow() != current.Foreground) break;
                        Native.SendMessageTimeout(hwnd, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout, 0x0002, 50, out _);
                        if (Native.GetKeyboardLayout(current.Thread) == layout) break;
                    }
                    for (int i = 0; i < 20 && !cancellation.IsCancellationRequested; i++)
                    {
                        if (Capture(out var verified, out _, out _, out _, false) && verified != null)
                        {
                            if (!TextEditor.SameEditor(expected, verified) ||
                                !session.Matches(verified.Text, verified.SelectionStart, verified.SelectionEnd)) break;
                            if (verified.Layout == layout) { Publish(verified); switched = true; break; }
                        }
                        if (Native.GetForegroundWindow() != expected.Foreground) break;
                        Thread.Sleep(20);
                    }
                }
                if (switched && Capture(out var after, out _, out _, out _, false)) Publish(after);
                result.TrySetResult(switched);
            }
            catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or ElementNotAvailableException)
            { result.TrySetResult(false); }
        });
        _wake.Set();
        try { return await result.Task.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { cancellation.Cancel(); return false; }
    }

    private bool Replace(EditorSnapshot expected, InputSession session, IntPtr layout, Func<bool> stillValid)
    {
        bool Reject(string reason) { Trace.WriteLine("Accept check: UIA — " + reason); return false; }
        Trace.WriteLine("Accept check: UIA — получен запрос замены");
        if (!stillValid() || !Capture(out var before, out _, out var pattern, out var saved, false) || before == null ||
            !TextEditor.SameEditor(expected, before) || !session.Matches(before.Text, before.SelectionStart, before.SelectionEnd) || before.Layout != layout)
            return Reject("Перед выделением не совпали поле, текст, каретка, раскладка или отменён запрос");
        string replacement = session.Replacement;
        if (replacement.Any(char.IsControl)) return Reject("В замене обнаружены управляющие символы");
        // UTF-16 offsets before the session can contain surrogate pairs/graphemes.
        // Anchor at the verified caret and move across only the physically typed
        // EN/RU characters; never interpret the old prefix as Character-unit counts.
        var range = saved!.Clone();
        if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -session.Source.Length) != -session.Source.Length)
            return Reject("Провайдер не выделил полный диапазон сессии");
        if (range.GetText(MaximumText + 1) != session.Source || !stillValid()) return Reject("Текст выделяемого диапазона не совпал или запрос отменён");
        Trace.WriteLine("Accept check: UIA — полный диапазон проверен; запрос выделения");
        range.Select();
        EditorSnapshot? selected = null;
        bool selectionConfirmed = false;
        for (int i = 0; i < 10 && stillValid(); i++)
        {
            if (Native.GetForegroundWindow() != before.Foreground) break;
            if (Capture(out selected, out _, out _, out _, false) && selected != null)
            {
                if (!TextEditor.SameEditor(before, selected) || selected.Text != before.Text || selected.Layout != layout) break;
                if (selected.SelectionStart == session.Start && selected.SelectionEnd == session.End)
                { selectionConfirmed = true; break; }
            }
            Thread.Sleep(15);
        }
        if (!selectionConfirmed || !stillValid())
        {
            if (stillValid() && Native.GetForegroundWindow() == before.Foreground && selected != null &&
                TextEditor.SameEditor(before, selected) && selected.Text == before.Text && selected.Layout == layout)
                saved!.Select();
            return Reject("Выделение диапазона не подтверждено либо операция отменена");
        }
        // TextPattern cannot insert text. Replace only the verified selection using
        // Unicode keyboard input; never use the clipboard or a whole-field SetValue.
        var inputs = replacement.SelectMany(c => new[] { Unicode(c, false), Unicode(c, true) }).ToArray();
        if (!stillValid() || Native.GetForegroundWindow() != before.Foreground ||
            Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>()) != inputs.Length) return Reject("Unicode-ввод не отправлен полностью либо контекст изменился; результат требует проверки");
        Trace.WriteLine("Accept check: UIA — ввод отправлен; проверка результата");
        string expectedText = before.Text.Remove(session.Start, session.Source.Length).Insert(session.Start, replacement);
        for (int i = 0; i < 25 && stillValid(); i++)
        {
            if (Native.GetForegroundWindow() != before.Foreground) return Reject("Во время подтверждения результата сменилось окно");
            bool captured = Capture(out var after, out _, out _, out _, false) && after != null;
            if (captured && !TextEditor.SameEditor(before, after!)) return Reject("Во время подтверждения результата сменилось поле");
            if (captured && after!.Text == expectedText && after.SelectionStart == session.Start + replacement.Length && after.SelectionEnd == after.SelectionStart)
            {
                Publish(after);
                Trace.WriteLine("Accept check: UIA — весь документ и каретка после замены подтверждены");
                return true;
            }
            Thread.Sleep(20);
        }
        return Reject("За отведённое ожидание результат замены не подтверждён");
    }

    private static Native.INPUT Unicode(char c, bool up) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion { ki = new Native.KEYBDINPUT { wScan = c,
            dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0), dwExtraInfo = Native.LayoutPreviewInput } }
    };

    private bool Capture(out EditorSnapshot? snapshot, out AutomationElement? element,
        out TextPattern? pattern, out TextPatternRange? selection, bool presentation = true)
    {
        snapshot = null; element = null; pattern = null; selection = null;
        string stage = "native focus", controlType = "unknown", nativeClass = "unknown";
        uint diagnosticPid = 0, diagnosticFlags = 0;
        int captureFocusGeneration = Volatile.Read(ref _focusGeneration);
        try
        {
            IntPtr foreground = Native.GetForegroundWindow();
            uint thread = Native.GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
            diagnosticPid = foregroundProcessId;
            var gui = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
            if (thread == 0 || !Native.GetGUIThreadInfo(thread, ref gui) || (gui.flags & Native.GUI_INMOVESIZE) != 0) return false;
            diagnosticFlags = gui.flags;
            // WinUI/composite controls need not own a separate native HWND.
            // UIA still has to identify a writable focused field in this window.
            IntPtr focusHandle = gui.hwndFocus == IntPtr.Zero ? foreground : gui.hwndFocus;
            var className = new System.Text.StringBuilder(256);
            Native.GetClassName(focusHandle, className, className.Capacity);
            nativeClass = className.ToString();
            if (NotepadEditor.IsSupportedClass(nativeClass))
            {
                // A transient failed native read must not manufacture another UIA
                // field identity for the same Edit/RichEdit HWND. UIA may enrich
                // geometry/fonts inside CaptureNativeGeometry, not replace its backend.
                bool captured = CaptureNativeGeometry(out snapshot, presentation);
                stage = _nativeCaptureFailure;
                return captured;
            }
            using var captureMeasurement = PerformanceDiagnostics.Measure("uia-capture");
            var focused = AutomationElement.FocusedElement;
            stage = "UIA focused element";
            if (focused != null) controlType = focused.Current.ControlType.ProgrammaticName;
            if (focused == null || !focused.Current.HasKeyboardFocus || focused.Current.IsPassword || !focused.Current.IsEnabled)
                return false;
            stage = "UIA process binding";
            Native.GetWindowThreadProcessId(focusHandle, out uint focusedProcessId);
            if (!BelongsToFocusedWindow(focused.Current.ProcessId, focusedProcessId, foregroundProcessId)) return false;
            int[] focusedId = focused.GetRuntimeId();
            stage = "writable TextPattern";
            using (PerformanceDiagnostics.Measure("field-binding"))
                if (!TryResolveEditable(focused, foreground, nativeClass, out element, out pattern)) return false;
            if (element.Current.IsPassword || !element.Current.IsEnabled) return false;
            var document = pattern.DocumentRange;
            ValuePattern? valuePattern = element.TryGetCurrentPattern(ValuePattern.Pattern, out var rawValue)
                ? (ValuePattern)rawValue : null;
            var readOnlyAttribute = document.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
            stage = "readonly attributes";
            if (valuePattern?.Current.IsReadOnly == true ||
                (readOnlyAttribute is bool readOnly ? readOnly : valuePattern == null))
                return false;
            string? value = valuePattern?.Current.Value;
            stage = "IME composition";
            var ime = Native.ImmGetContext(focusHandle);
            if (ime != IntPtr.Zero)
            {
                bool composing = Native.ImmGetCompositionString(ime, 8, IntPtr.Zero, 0) > 0;
                Native.ImmReleaseContext(focusHandle, ime);
                if (composing) return false;
            }
            string text = document.GetText(MaximumText + 1);
            string rawDocumentText = text;
            bool usingValue = false;
            stage = "document limit";
            if (text.Length > MaximumText) return false;
            var selections = pattern.GetSelection();
            stage = "selection ranges";
            if (selections.Length != 1) return false;
            selection = selections[0];
            string selectedText = selection.GetText(MaximumText + 1);
            bool collapsed = selection.CompareEndpoints(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End) == 0;
            if (collapsed != (selectedText.Length == 0)) return false;
            var boundary = document.Clone();
            int start, end;
            // Read the nearer side based on the previous caret. The hint chooses
            // only a cheaper query; offsets still come from the current UIA range.
            var previous = Volatile.Read(ref _snapshot);
            bool fromEnd = previous?.Foreground == foreground && previous.Editor == focusHandle && previous.SelectionEnd > text.Length / 2;
            if (fromEnd)
            {
                boundary.MoveEndpointByRange(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End);
            }
            else
            {
                boundary.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection, TextPatternRangeEndpoint.Start);
            }
            stage = "selection offsets";
            if (!TryResolveOffsets(text, selectedText, boundary.GetText(MaximumText + 1), fromEnd, out start, out end)) return false;
            if (value != null)
            {
                stage = "ValuePattern/TextPattern consistency";
                int rawStart = start, rawEnd = end;
                if (TryCanonicalDocument(rawDocumentText, value, rawStart, rawEnd, out text, out start, out end))
                    usingValue = true;
                else
                {
                    // SearchHost can expose a display Value different from its
                    // editable TextPattern. Use only explicitly writable, stable
                    // text/selection, not an unverified merge of the two sources.
                    if (readOnlyAttribute is not false || document.GetText(MaximumText + 1) != rawDocumentText ||
                        !TryCanonicalDocument(rawDocumentText, rawDocumentText, rawStart, rawEnd, out text, out start, out end))
                        return false;
                    TraceTextValueDifference(rawDocumentText.Length, value.Length, rawStart, rawEnd);
                }
            }
            Native.RECT caret = default;
            try
            {
                using var caretMeasurement = PerformanceDiagnostics.Measure("caret");
                if (presentation) caret = GetCaret(selection, collapsed);
            }
            catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or ElementNotAvailableException)
            {
                // Text and selection remain useful even if Chromium has not yet
                // published screen bounds. Rendering must not invalidate input.
            }
            stage = "Идентификатор UIA-поля недоступен";
            uint editorThread = Native.GetWindowThreadProcessId(focusHandle, IntPtr.Zero);
            string identity = string.Join(".", element.GetRuntimeId());
            if (identity.Length == 0) return false;
            stage = "Активное окно изменилось во время снимка";
            if (Native.GetForegroundWindow() != foreground) return false;
            stage = "UIA-фокус недоступен при повторном чтении";
            var focusedAfter = AutomationElement.FocusedElement;
            if (focusedAfter == null) return false;
            stage = "Идентификатор UIA-фокуса изменился во время снимка";
            if (!focusedAfter.GetRuntimeId().SequenceEqual(focusedId)) return false;
            stage = "Фокус перешёл в защищённое поле";
            if (focusedAfter.Current.IsPassword) return false;
            stage = "Значение поля изменилось во время снимка";
            if (usingValue && valuePattern!.Current.Value != value) return false;
            stage = "Число диапазонов выделения изменилось во время снимка";
            var finalSelections = pattern.GetSelection();
            if (finalSelections.Length != 1) return false;
            stage = "Начало выделения изменилось во время снимка";
            if (selection.CompareEndpoints(TextPatternRangeEndpoint.Start, finalSelections[0], TextPatternRangeEndpoint.Start) != 0) return false;
            stage = "Конец выделения изменился во время снимка";
            if (selection.CompareEndpoints(TextPatternRangeEndpoint.End, finalSelections[0], TextPatternRangeEndpoint.End) != 0) return false;
            stage = "Текст документа изменился во время снимка";
            if (!usingValue && pattern.DocumentRange.GetText(MaximumText + 1) != rawDocumentText) return false;
            snapshot = new(foreground, focusHandle, editorThread, Native.GetKeyboardLayout(editorThread), text,
                start, end, caret, Native.GetDpiForWindow(focusHandle), identity, Environment.TickCount64);
            var context = snapshot;
            var selectedRange = selection;
            var editableElement = element;
            snapshot = WithFormatting(context, presentation, () =>
            {
                var formatting = GetFormattingRange(selectedRange);
                var styled = context with { FontSizeDip = ReadFontSizeDip(formatting), FontFamilyName = ReadFontFamilyName(formatting),
                    FontWeightValue = ReadFontWeight(formatting), FontItalic = ReadFontItalic(formatting), FontSource = ReadUiaFontSource(focused, editableElement) };
                return _rangeFont.Enrich(styled, selectedRange, Volatile.Read(ref _focusGeneration));
            }, _rangeFont.NeedsInitialProbe(_formattingSnapshot == null ? context : CopyFormatting(context, _formattingSnapshot)));
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or System.ComponentModel.Win32Exception or ElementNotAvailableException)
        { stage += " (" + e.GetType().Name + ")"; _boundContext = ""; return false; }
        finally
        {
            // A focus event can race the optional formatting calls after the
            // document check. Never republish the old field across that event.
            if (captureFocusGeneration != Volatile.Read(ref _focusGeneration))
            {
                stage = snapshot != null ? "Событие смены фокуса во время снимка" : stage + "; событие смены фокуса";
                snapshot = null;
            }
            Volatile.Write(ref _lastCaptureFailure, snapshot == null ? stage : "");
            if (snapshot == null)
            {
                PerformanceDiagnostics.Observe("unavailable-" + stage, 0);
                string context = $"stage={stage} pid={diagnosticPid} class={nativeClass} type={controlType} guiFlags={diagnosticFlags:X}";
                long now = Environment.TickCount64;
                if (context != _lastRejectedContext || now - _lastRejectionAt >= 2000)
                {
                    Trace.WriteLine("UIA unavailable: " + context);
                    _lastRejectedContext = context; _lastRejectionAt = now;
                }
            }
        }
    }

    private bool TryResolveEditable(AutomationElement focused, IntPtr foreground, string nativeClass,
        out AutomationElement element, out TextPattern pattern)
    {
        string context = $"{foreground}:{Volatile.Read(ref _focusGeneration)}:{string.Join('.', focused.GetRuntimeId())}";
        element = _boundElement!; pattern = _boundPattern!;
        if (context == _boundContext && pattern != null) return true;
        if (context == _boundContext && Environment.TickCount64 - _bindingRejectedAt < 300) return false;
        if (context != _boundContext) SetTextEvents(null);
        _boundContext = context;
        bool resolved = TryResolveEditableAncestors(focused, out element, out pattern);
        if (!resolved && nativeClass.Contains("InputSite", StringComparison.Ordinal) &&
            _shellDiscovery.TryBegin(context, Environment.TickCount64))
        {
            int pid = focused.Current.ProcessId;
            using var process = Process.GetProcessById(pid);
            if (ShellSearchResolver.IsSearchProcess(process.ProcessName))
            {
                var root = AutomationElement.FromHandle(foreground);
                if (root != null) resolved = ShellSearchResolver.TryFind(root, pid, out element, out pattern);
            }
        }
        _boundElement = resolved ? element : null;
        _boundPattern = resolved ? pattern : null;
        if (resolved) SetTextEvents(element);
        else _bindingRejectedAt = Environment.TickCount64;
        return resolved;
    }

    private void SetTextEvents(AutomationElement? element)
    {
        if (ReferenceEquals(_eventElement, element)) return;
        if (_eventElement != null && _textChanged != null)
        {
            try { Automation.RemoveAutomationEventHandler(TextPattern.TextChangedEvent, _eventElement, _textChanged); }
            catch (Exception e) when (e is InvalidOperationException or COMException or ElementNotAvailableException) { }
            try { Automation.RemoveAutomationEventHandler(TextPattern.TextSelectionChangedEvent, _eventElement, _textChanged); }
            catch (Exception e) when (e is InvalidOperationException or COMException or ElementNotAvailableException) { }
        }
        _eventElement = element;
        if (element == null) return;
        _textChanged ??= (_, _) => NotifyInput();
        try { Automation.AddAutomationEventHandler(TextPattern.TextChangedEvent, element, TreeScope.Element, _textChanged); }
        catch (Exception e) when (e is InvalidOperationException or COMException or ElementNotAvailableException) { }
        try { Automation.AddAutomationEventHandler(TextPattern.TextSelectionChangedEvent, element, TreeScope.Element, _textChanged); }
        catch (Exception e) when (e is InvalidOperationException or COMException or ElementNotAvailableException) { }
    }

    private static bool TryResolveEditableAncestors(AutomationElement focused, out AutomationElement element, out TextPattern pattern)
    {
        element = focused; pattern = null!;
        int processId = focused.Current.ProcessId;
        AutomationElement? candidate = focused;
        for (int depth = 0; candidate != null && depth < 8; depth++)
        {
            var properties = candidate.Current;
            if (properties.ProcessId != processId || properties.IsPassword || !properties.IsEnabled) return false;
            if (candidate.TryGetCurrentPattern(TextPattern.Pattern, out var raw))
            {
                var candidatePattern = (TextPattern)raw;
                var attribute = candidatePattern.DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute);
                bool? valueReadOnly = candidate.TryGetCurrentPattern(ValuePattern.Pattern, out var rawValue)
                    ? ((ValuePattern)rawValue).Current.IsReadOnly : null;
                if (IsWritableText(attribute is bool readOnly ? readOnly : null, valueReadOnly))
                {
                    element = candidate; pattern = candidatePattern;
                    return true;
                }
                // A readonly, nonfocusable text child can belong to an editable
                // composite control. A readonly input itself must stay excluded.
                if (properties.IsKeyboardFocusable || properties.ControlType == ControlType.Edit)
                    return false;
            }
            candidate = TreeWalker.ControlViewWalker.GetParent(candidate);
        }
        return false;
    }

    private static long _lastTextValueDifferenceAt;
    private static void TraceTextValueDifference(int textLength, int valueLength, int start, int end)
    {
        long now = Environment.TickCount64;
        if (now - _lastTextValueDifferenceAt < 2000) return;
        _lastTextValueDifferenceAt = now;
        Trace.WriteLine($"UIA stable TextPattern fallback textLength={textLength} valueLength={valueLength} selection={start},{end}");
    }

    internal static bool IsWritableText(bool? textReadOnly, bool? valueReadOnly) =>
        textReadOnly != true && valueReadOnly != true && (textReadOnly == false || valueReadOnly == false);

    internal static bool BelongsToFocusedWindow(int processId, uint focusedProcessId, uint foregroundProcessId) =>
        processId > 0 && ((uint)processId == focusedProcessId || (uint)processId == foregroundProcessId);

    private bool CaptureNativeGeometry(out EditorSnapshot? snapshot, bool presentation)
    {
        using var measurement = PerformanceDiagnostics.Measure("native-capture");
        snapshot = null;
        _nativeCaptureFailure = "Первый native-снимок недоступен";
        if (!NotepadEditor.TryRead(out var before)) return false;
        if (!presentation) { snapshot = before; return true; }
        var caret = before.Caret;
        TextPatternRange? selectedRange = null;
        bool formattingDue = _formattingSnapshot == null || _formattingGeneration != Volatile.Read(ref _focusGeneration) ||
            !TextEditor.SameEditor(before, _formattingSnapshot) || before.Dpi != _formattingSnapshot.Dpi ||
            Environment.TickCount64 - _formattingAt >= 500;
        if (caret.Bottom > caret.Top && !formattingDue)
        {
            snapshot = CopyFormatting(before, _formattingSnapshot!) with { CapturedAt = Environment.TickCount64 };
            return true;
        }
        try
        {
            if (caret.Bottom <= caret.Top || formattingDue)
            {
                int generation = Volatile.Read(ref _focusGeneration);
                if (_nativeElement == null || _nativeElementHandle != before.Editor || _nativeElementGeneration != generation)
                {
                    _nativeElement = AutomationElement.FromHandle(before.Editor);
                    _nativeElementHandle = before.Editor; _nativeElementGeneration = generation;
                }
                var element = _nativeElement;
                if (element != null && element.Current.HasKeyboardFocus && !element.Current.IsPassword &&
                    element.TryGetCurrentPattern(TextPattern.Pattern, out var raw))
                {
                    var ranges = ((TextPattern)raw).GetSelection();
                    if (ranges.Length == 1)
                    {
                        SetTextEvents(element);
                        selectedRange = ranges[0];
                        if (caret.Bottom <= caret.Top) caret = GetCaret(ranges[0], before.SelectionStart == before.SelectionEnd);
                    }
                }
            }
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or ElementNotAvailableException)
        { _nativeElement = null; /* Missing geometry must not discard a readable native document. */ }
        var styled = WithFormatting(before, true, () =>
        {
            var result = before;
            if (selectedRange != null)
            {
                var formatting = GetFormattingRange(selectedRange);
                result = result with { FontSizeDip = ReadFontSizeDip(formatting), FontFamilyName = ReadFontFamilyName(formatting),
                    FontWeightValue = ReadFontWeight(formatting), FontItalic = ReadFontItalic(formatting), FontSource = "UIA character" };
            }
            var className = new System.Text.StringBuilder(256);
            Native.GetClassName(before.Editor, className, className.Capacity);
            if ((NotepadEditor.IsPlainEditClass(className.ToString()) || result.FontSizeDip <= 0) &&
                NotepadEditor.TryReadFont(before, out var nativeFont, out var nativeSize))
                result = result with { FontSizeDip = nativeSize, FontFamilyName = nativeFont.FaceName,
                    FontWeightValue = nativeFont.Weight, FontItalic = nativeFont.Italic != 0, FontSource = "Win32 HFONT" };
            return result;
        });
        // COM can be slow and the user can edit/move the caret while it is running.
        // Publish its coordinates only if a fresh native read still matches.
        _nativeCaptureFailure = "Повторный native-снимок недоступен после чтения оформления";
        if (!NotepadEditor.TryRead(out var after)) return false;
        _nativeCaptureFailure = "Контекст native-поля изменился во время снимка";
        if (!TextEditor.SameEditor(before, after)) return false;
        _nativeCaptureFailure = "Текст native-поля изменился во время чтения оформления";
        if (before.Text != after.Text) return false;
        _nativeCaptureFailure = "Выделение native-поля изменилось во время чтения оформления";
        if (before.SelectionStart != after.SelectionStart || before.SelectionEnd != after.SelectionEnd) return false;
        _nativeCaptureFailure = "DPI native-поля изменился во время снимка";
        if (before.Dpi != after.Dpi) return false;
        snapshot = CopyFormatting(after, styled) with { Caret = caret, CapturedAt = Environment.TickCount64 };
        return true;
    }

    internal static bool IsOmniboxFormatting(string className, string shortcut) =>
        className.Equals("OmniboxViewViews", StringComparison.Ordinal) ||
        className.Equals("Chrome_OmniboxView", StringComparison.Ordinal) ||
        shortcut.Replace(" ", "").Equals("Ctrl+L", StringComparison.OrdinalIgnoreCase) ||
        shortcut.Replace(" ", "").Equals("Control+L", StringComparison.OrdinalIgnoreCase);

    private static string ReadUiaFontSource(AutomationElement focused, AutomationElement editable)
    {
        // Optional metadata failures cannot suppress a known class or readable font.
        static string Read(Func<string> get)
        {
            try { string value = get(); return value.Length <= 128 ? value : value[..128]; }
            catch (Exception error) when (error is InvalidOperationException or COMException or ElementNotAvailableException) { return ""; }
        }
        string focusedClass = Read(() => focused.Current.ClassName);
        string focusedShortcut = Read(() => focused.Current.AcceleratorKey);
        string editableClass = Read(() => editable.Current.ClassName);
        string editableShortcut = Read(() => editable.Current.AcceleratorKey);
        bool omnibox = IsOmniboxFormatting(focusedClass, focusedShortcut) || IsOmniboxFormatting(editableClass, editableShortcut);
        return (omnibox ? "UIA omnibox" : "UIA character") +
            $"; focusedClass={focusedClass}; focusedShortcut={focusedShortcut}; editableClass={editableClass}; editableShortcut={editableShortcut}";
    }

    private static string ReadFontFamilyName(TextPatternRange range)
    {
        try { return range.GetAttributeValue(TextPattern.FontNameAttribute) is string name && name.Length <= 256 ? name : ""; }
        catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or ElementNotAvailableException)
        { return ""; }
    }

    private static TextPatternRange GetFormattingRange(TextPatternRange selection)
    {
        if (selection.GetText(1).Length != 0) return selection;
        var range = selection.Clone();
        if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1) != 0 &&
            range.GetText(2).IndexOfAny(['\r', '\n']) < 0) return range;
        range = selection.Clone();
        if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1) != 0 &&
            range.GetText(2).IndexOfAny(['\r', '\n']) < 0) return range;
        return selection;
    }

    private static int ReadFontWeight(TextPatternRange range)
    {
        try { return range.GetAttributeValue(TextPattern.FontWeightAttribute) is int value && value > 0 ? Math.Clamp(value, 100, 900) : 400; }
        catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or ElementNotAvailableException) { return 400; }
    }

    private static bool ReadFontItalic(TextPatternRange range)
    {
        try { return range.GetAttributeValue(TextPattern.IsItalicAttribute) is true; }
        catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or ElementNotAvailableException) { return false; }
    }

    private static double ReadFontSizeDip(TextPatternRange range)
    {
        try
        {
            return range.GetAttributeValue(TextPattern.FontSizeAttribute) is double points
                ? FontPointsToDip(points) : 0;
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or ArgumentException or ElementNotAvailableException)
        { return 0; }
    }

    internal static double FontPointsToDip(double points) =>
        double.IsFinite(points) && points > 0 ? points * 96.0 / 72.0 : 0;

    internal static bool TryResolveOffsets(string document, string selected, string boundary, bool fromEnd,
        out int start, out int end)
    {
        end = fromEnd ? document.Length - boundary.Length : boundary.Length + selected.Length;
        start = end - selected.Length;
        return start >= 0 && end >= start && end <= document.Length &&
            document.AsSpan(start, selected.Length).SequenceEqual(selected) &&
            (fromEnd ? document.AsSpan(end).SequenceEqual(boundary) : document.AsSpan(0, start).SequenceEqual(boundary));
    }

    internal static string DescribeFocusedElement()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element == null) return "No focused automation element";
            if (element.Current.IsPassword) return "Password field excluded";
            string patterns = string.Join(",", element.GetSupportedPatterns().Select(p => p.ProgrammaticName));
            string readOnly = element.TryGetCurrentPattern(TextPattern.Pattern, out var raw)
                ? ((TextPattern)raw).DocumentRange.GetAttributeValue(TextPattern.IsReadOnlyAttribute).ToString() ?? "unknown" : "no TextPattern";
            string details = "";
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valueRaw))
            {
                string value = ((ValuePattern)valueRaw).Current.Value;
                string name = element.Current.Name;
                string help = element.Current.HelpText;
                details = $" ValueLength={value.Length} NameLength={name.Length} HelpLength={help.Length} ValueEqualsName={value == name} ValueEqualsHelp={value == help} TrailingNewlines={value.Length - value.TrimEnd('\r', '\n').Length} OnlyNewlines={value.Trim('\r', '\n').Length == 0}";
            }
            return $"Type={element.Current.ControlType.ProgrammaticName} Focus={element.Current.HasKeyboardFocus} ReadOnly={readOnly} Patterns={patterns}{details}";
        }
        catch (Exception e) when (e is InvalidOperationException or COMException or ElementNotAvailableException)
        { return $"UIA unavailable: {e.GetType().Name}"; }
    }

    internal static bool TryCanonicalDocument(string document, string value, int start, int end,
        out string text, out int selectionStart, out int selectionEnd)
    {
        text = value; selectionStart = start; selectionEnd = end;
        if (value.Length > MaximumText || start < 0 || end < start) return false;
        // Some Chromium providers include the terminal paragraph mark in BOTH
        // ValuePattern and TextPattern. Remove one mark only when the selection
        // ends before it; a line break containing the caret remains entered text.
        int markerLength = value.EndsWith("\r\n", StringComparison.Ordinal) ? 2
            : value.EndsWith('\n') || value.EndsWith('\r') ? 1 : 0;
        if (markerLength > 0 && end <= value.Length - markerLength)
            value = value[..^markerLength];
        text = value;
        // Chromium exposes the placeholder through TextPattern in an empty composer,
        // while ValuePattern correctly reports no entered text. Its collapsed range
        // can point to the end of that placeholder rather than to logical offset zero.
        if (value.Length == 0)
        {
            if (start != end) return false;
            selectionStart = selectionEnd = 0;
            return true;
        }
        if (end > value.Length || !document.StartsWith(value, StringComparison.Ordinal)) return false;
        // TextPattern may append an implicit paragraph mark. ValuePattern preserves
        // real user-entered line breaks, so only discard the extra provider suffix.
        foreach (char c in document.AsSpan(value.Length))
            if (c is not ('\r' or '\n')) return false;
        return true;
    }

    private static Native.RECT GetCaret(TextPatternRange selection, bool? collapsed = null)
    {
        if (collapsed == false || (collapsed == null && selection.GetText(1).Length != 0)) return default;
        var range = selection.Clone();
        // Chromium can return the whole field's rectangle for a collapsed range.
        // Expand one adjacent character instead of treating that rectangle as caret.
        bool previous = true;
        if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1) == 0 ||
            range.GetText(2).IndexOfAny(['\r', '\n']) >= 0)
        {
            range = selection.Clone();
            previous = false;
            if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1) == 0 ||
                range.GetText(2).IndexOfAny(['\r', '\n']) >= 0)
                return default;
        }
        var bounds = range.GetBoundingRectangles();
        if (bounds.Length == 0) return default;
        var glyph = previous ? bounds[^1] : bounds[0];
        if (glyph.IsEmpty || glyph.Height <= 0) return default;
        int x = (int)Math.Round(previous ? glyph.Right : glyph.Left);
        int y = (int)Math.Round(glyph.Top);
        return new() { Left = x, Right = x + 1, Top = y, Bottom = y + (int)Math.Ceiling(glyph.Height) };
    }

    public void Dispose() { _stopping = true; _wake.Set(); }
}
