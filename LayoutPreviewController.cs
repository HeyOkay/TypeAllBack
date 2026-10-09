using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
namespace LayoutPreviewPrototype;
public sealed class LayoutPreviewController : IDisposable
{
    private readonly PreviewWindow _preview = new();
    private readonly AcceptedInputGuard _acceptedInput = new();
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly bool _testInput = Environment.GetCommandLineArgs().Contains("--test-input");
    private readonly List<QueuedInput> _pendingInputs = [];
    private readonly List<QueuedInput> _deferredStartupInputs = [];
    private readonly DispatcherTimer _deferredStartupWatch = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private KeyboardHookPump? _keyboard;
    private long _processedSequence;
    private bool _capturedCapsLock;
    private InputSession? _session;
    private EditorSnapshot? _editor;
    private EditorSnapshot? _acceptedEditor;
    private IntPtr _targetLayout;
    private bool _visible, _hasAppeared, _accepting, _disposed;
    private bool _uiaStartupUnconfirmed;
    private bool _updateQueued;
    private EditorSnapshot? _selectAllEditor;
    private long _selectAllAt;
    private bool _acceptedBackspacePending;
    private bool _controlDown, _altDown, _winDown;
    private int _generation;
    private long _pendingUntil;
    private long _deferredStartupUntil;
    private IntPtr _deferredStartupForeground;
    private uint _deferredStartupThread;
    private string _lastPreview = "";
    private Native.RECT _lastCaret;
    private uint _lastDpi;
    private double _lastFontSizeDip;
    private string _lastFontFamilyName = "";
    private int _lastFontWeight;
    private bool _lastFontItalic;
    private string _lastFontSource = "";
    private string _lastPreviewDiagnostic = "";
    private int _snapshotUpdateQueued;
    private long _lastVerifiedRevision = -1;
    private string _lastVerifiedSource = "";
    private void OnSnapshotChanged()
    {
        if (_disposed || Interlocked.Exchange(ref _snapshotUpdateQueued, 1) != 0) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _snapshotUpdateQueued, 0);
            if (_disposed) return;
            if (_deferredStartupInputs.Count > 0) TryAttachDeferredStartup();
            UpdatePreview();
        }, DispatcherPriority.Background);
    }
    public void Start()
    {
        TextEditor.Start();
        TextEditor.SnapshotChanged += OnSnapshotChanged;
        _preview.AcceptRequested += (_, _) => AcceptPreview();
        _watch.Tick += (_, _) => UpdatePreview();
        _deferredStartupWatch.Tick += (_, _) => TryAttachDeferredStartup();
        _keyboard = new KeyboardHookPump(input => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_disposed) return;
            PerformanceDiagnostics.Observe("key-dispatch", Environment.TickCount64 - input.CapturedAt);
            ProcessKey(input);
            _processedSequence = input.Sequence;
        }, DispatcherPriority.Input), _testInput);
        _keyboard.Start();
    }
    private IntPtr ProcessKey(CapturedKeyboardInput input)
    {
        var data = input.Data;
        int message = input.Message;
        _capturedCapsLock = input.CapsLock;
        int vk = (int)data.vkCode;
        bool isDown = message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
        bool isUp = message is 0x0101 or 0x0105; // WM_KEYUP / WM_SYSKEYUP
        if (!isDown && !isUp) return Next();

        if (vk is 0x11 or 0xA2 or 0xA3) { _controlDown = isDown; return Next(); }
        if (vk is 0x12 or 0xA4 or 0xA5) { _altDown = isDown; return Next(); }
        if (vk is Native.VK_LWIN or Native.VK_RWIN)
        {
            _winDown = isDown;
            if (isDown) Reset();
            return Next();
        }
        if (isUp) return Next();
        if ((data.flags & Native.LLKHF_INJECTED) != 0 && data.dwExtraInfo == Native.LayoutPreviewInput) return Next();
        if (_accepting) { Trace.WriteLine("Accept cancelled by intervening input"); Reset(); return Next(); }
        if ((data.flags & Native.LLKHF_INJECTED) != 0 && !_testInput) { Reset(); return Next(); }
        if (vk is 0x10 or 0xA0 or 0xA1) return Next();
        bool shift = input.Shift;
        // Shortcuts such as Ctrl+A, Ctrl+C and formatting commands do not invalidate
        // the input session by themselves. The editor watcher verifies the text before
        // the preview can resume or an accept can modify anything.
        if (_controlDown || _altDown || _winDown || input.Control || input.Alt || input.Win)
        {
            if (vk == 0x41 && (_controlDown || input.Control) && !_altDown && !_winDown && !shift)
            {
                _selectAllEditor = _editor ?? _acceptedEditor;
                _selectAllAt = input.CapturedAt;
            }
            else if (vk is 0x56 or 0x58 or 0x59 or 0x5A) _selectAllEditor = null;
            return Next();
        }
        // Every Backspace, including repeats after the tracked range is empty,
        // must pass through without reading the editor or hashing the document.
        // A modifier shortcut was already forwarded above (e.g. Ctrl+Backspace).
        if (vk == Native.VK_BACK)
        {
            _acceptedBackspacePending |= _acceptedInput.IsArmed;
            if (_selectAllEditor != null)
            {
                // Ctrl+A followed by native deletion removes all captured content.
                // Keep the focused flow and accepted-layout guard; no old typed
                // range remains to track. The next printable key is captured anew.
                _selectAllEditor = null;
                _pendingInputs.Clear();
                ClearDeferredStartup();
                _session = null; _editor = null; _targetLayout = IntPtr.Zero;
                _uiaStartupUnconfirmed = false; _visible = false;
                if (_keyboard != null) _keyboard.AcceptContext = null;
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    if (!_visible) _preview.HidePreview();
                }, DispatcherPriority.Background);
            }
            else if (_session != null && _editor != null)
            {
                // Deleting a character is independent of the current layout.
                TryQueuePending(data, shift, _session.SourceLayout);
                _watch.Start();
            }
            return Next();
        }
        var snapshot = input.Snapshot;
        if (snapshot == null)
        {
            if (_acceptedInput.IsArmed && _acceptedEditor is { } intentionalEditor &&
                Native.GetForegroundWindow() == intentionalEditor.Foreground &&
                Native.GetKeyboardLayout(intentionalEditor.Thread) == intentionalEditor.Layout)
                return Next();
            if (_session != null && _editor is { } activeEditor &&
                Native.GetForegroundWindow() == activeEditor.Foreground &&
                TryLayout(Native.GetKeyboardLayout(activeEditor.Thread), out var activeLayout) &&
                TryQueuePending(data, shift, activeLayout))
                return Next();
            if (_session == null && TryQueueDeferredStartup(data, shift)) return Next();
            Trace.WriteLine("Hook context unsupported"); Reset(); return Next();
        }
        if (!TryLayout(snapshot.Layout, out var layout))
        { Trace.WriteLine("Hook layout unsupported"); Reset(); return Next(); }

        // Accept switches to the target layout. Subsequent characters in that layout
        // are presumed intentional; do not immediately raise an opposite-layout hint
        // for the next word in the same uninterrupted typing flow.
        bool startsNewContext = vk is Native.VK_RETURN or Native.VK_LEFT or Native.VK_UP or
            Native.VK_DOWN or Native.VK_HOME or Native.VK_END or Native.VK_PRIOR or
            Native.VK_NEXT or Native.VK_DELETE or Native.VK_ESCAPE;
        var guardKey = new PhysicalKey(data.vkCode, shift, _capturedCapsLock);
        // The provider may still expose its pre-Ctrl+A collapsed range. Predict a
        // full-field selection only for that briefly stale snapshot in this editor.
        bool selectAllWasPending = _selectAllEditor != null &&
            TextEditor.SameEditor(_selectAllEditor, snapshot) &&
            Environment.TickCount64 - _selectAllAt < 500 && snapshot.CapturedAt <= _selectAllAt + 50;
        _selectAllEditor = null;
        if (_acceptedEditor != null && !TextEditor.SameEditor(_acceptedEditor, snapshot))
        { _acceptedInput.Disarm(); _acceptedEditor = null; }
        if (_acceptedBackspacePending)
        {
            // Refresh the native guard once before the next non-Backspace input,
            // instead of predicting/hashing a document on every repeat.
            if (_acceptedInput.IsArmed && _acceptedEditor != null &&
                snapshot.Layout == _acceptedEditor.Layout)
                _acceptedInput.Arm(snapshot.Text, snapshot.SelectionEnd, layout);
            _acceptedBackspacePending = false;
        }
        // UIA snapshots arrive asynchronously. Once accepted, preserve the intentional
        // layout for this focused field instead of comparing typed text with a stale
        // snapshot and accidentally rearming the opposite-language hint.
        if (_acceptedInput.IsArmed && _acceptedEditor != null &&
            snapshot.Layout == _acceptedEditor.Layout) return Next();
        if (_acceptedInput.ShouldSuppress(snapshot.Text, snapshot.SelectionStart, snapshot.SelectionEnd,
            layout, guardKey, startsNewContext))
            return Next();

        int selectedStart = selectAllWasPending ? 0 : snapshot.SelectionStart;
        int selectedEnd = selectAllWasPending ? snapshot.Text.Length : snapshot.SelectionEnd;
        if (selectedStart != selectedEnd && KeyboardConversion.TryConvert(guardKey, layout, out _))
        {
            if (_session != null && _editor != null && TextEditor.SameEditor(_editor, snapshot))
            {
                TryConfirmUiaStartup(snapshot);
                if (_session.TryAppendOverExternalSelection(snapshot.Text, selectedStart, selectedEnd, guardKey, layout))
                {
                    _pendingInputs.Clear();
                    ScheduleUpdate();
                    return Next();
                }
            }
            // Handle native selection replacement BEFORE the dormant-selection
            // return. Otherwise its first key reaches the app but is never captured.
            _session = InputSession.FromSelectionReplacement(snapshot.Text, selectedStart, selectedEnd, guardKey, layout);
            _editor = snapshot with { Text = "" };
            _uiaStartupUnconfirmed = false;
            _pendingInputs.Clear();
            ClearDeferredStartup();
            _targetLayout = FindLayout(_session.TargetLayout);
            _visible = false;
            if (_keyboard != null) _keyboard.AcceptContext = null;
            _preview.HidePreview();
            if (_targetLayout == IntPtr.Zero) { Reset(); return Next(); }
            _watch.Start();
            ScheduleUpdate();
            return Next();
        }

        // Defer only keys received before an editor snapshot was available. A
        // supported snapshot starts the session immediately at its pre-key caret.
        if (_session == null && _deferredStartupInputs.Count > 0)
        {
            if (!TryAttachDeferredStartup(snapshot))
            {
                if (TryQueueDeferredStartup(data, shift)) return Next();
                ClearDeferredStartup();
            }
        }

        if (_session != null && _editor != null && TextEditor.SameEditor(_editor, snapshot))
            TryConfirmUiaStartup(snapshot);
        if (_session != null && (_editor == null || !TextEditor.SameEditor(_editor, snapshot)))
        {
            Reset();
        }
        else if (_session != null && _pendingInputs.Count > 0)
        {
            if (!TryReconcilePending(snapshot) && _pendingUntil > Environment.TickCount64 && TryQueuePending(data, shift, layout))
                return Next();
            if (_pendingInputs.Count > 0)
            {
                Trace.WriteLine($"Hook pending input could not be reconciled length={snapshot.Text.Length} selection={snapshot.SelectionStart},{snapshot.SelectionEnd}");
                Reset();
            }
        }
        else if (_session != null && !_session.Matches(snapshot.Text, snapshot.SelectionStart, snapshot.SelectionEnd))
        {
            // Selection and caret movement are reversible editor state. Keep the session
            // dormant while the captured document and typed range remain unchanged.
            if (_session.MatchesDocument(snapshot.Text)) return Next();
            if (_pendingUntil > Environment.TickCount64 && TryQueuePending(data, shift, layout))
                return Next();
            Trace.WriteLine($"Hook mismatch actualLength={snapshot.Text.Length} actualSelection={snapshot.SelectionStart},{snapshot.SelectionEnd} expectedEnd={_session.End}");
            Reset();
        }
        if (vk == Native.VK_RIGHT)
        {
            if (input.AcceptedRight && !shift && _visible && _session?.Preview.Length > 0 && _targetLayout != IntPtr.Zero)
            { Trace.WriteLine("Accept queued"); _accepting = true; Application.Current.Dispatcher.BeginInvoke(AcceptPreview); return (IntPtr)1; }
            Trace.WriteLine("Right native: no valid preview"); Reset(); return Next();
        }
        var key = new PhysicalKey(data.vkCode, shift, _capturedCapsLock);
        if (!KeyboardConversion.TryConvert(key, layout, out _) || snapshot.SelectionStart != snapshot.SelectionEnd) { Reset(); return Next(); }
        if (_session == null)
        {
            _session = new InputSession(snapshot.Text, snapshot.SelectionEnd, layout); _editor = snapshot with { Text = "" };
            _uiaStartupUnconfirmed = snapshot.AutomationIdentity.Length > 0;
            Trace.WriteLine($"Session started uia={snapshot.AutomationIdentity.Length > 0} start={snapshot.SelectionEnd} documentLength={snapshot.Text.Length}");
            _targetLayout = FindLayout(_session.TargetLayout);
            if (_targetLayout == IntPtr.Zero) { Reset(); return Next(); }
            _watch.Start();
        }
        if (_session.Source.Length >= 4096) { Reset(); return Next(); }
        _session.Append(key, layout, snapshot.Text); ScheduleUpdate(); return Next();
        IntPtr Next() => IntPtr.Zero; // Native delivery already happened on the hook thread.
    }
    private void ScheduleUpdate()
    {
        _pendingUntil = Environment.TickCount64 + 350;
        if (_updateQueued) return;
        _updateQueued = true;
        int generation = _generation;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _updateQueued = false;
            if (generation == _generation) UpdatePreview();
        }, DispatcherPriority.Background);
    }
    private void UpdatePreview()
    {
        using var measurement = PerformanceDiagnostics.Measure("preview-update");
        if (_disposed || _accepting) return;
        if (_session == null)
        {
            if (!_acceptedInput.IsArmed || _acceptedEditor == null) { _watch.Stop(); return; }
            if (TextEditor.TryRead(out var focused))
            {
                if (!TextEditor.SameEditor(_acceptedEditor, focused))
                { _acceptedInput.Disarm(); _acceptedEditor = null; _watch.Stop(); }
                else _acceptedEditor = _acceptedEditor with { CapturedAt = focused.CapturedAt };
            }
            else if (Native.GetForegroundWindow() != _acceptedEditor.Foreground ||
                Environment.TickCount64 - _acceptedEditor.CapturedAt > 1200)
            { _acceptedInput.Disarm(); _acceptedEditor = null; _watch.Stop(); }
            return;
        }
        if (!TextEditor.TryRead(out var current))
        {
            // UIA providers can briefly stop answering while the same app remains
            // focused. Keep the session dormant through that gap; Accept still has
            // to revalidate a fresh snapshot before it can change any text.
            if (_editor is { } activeEditor &&
                Native.GetForegroundWindow() == activeEditor.Foreground &&
                Environment.TickCount64 - activeEditor.CapturedAt < 1200)
                return;
            Reset(); return;
        }
        if (_editor == null || !TextEditor.SameEditor(_editor, current)) { Reset(); return; }
        if (_pendingInputs.Count == 0 && _lastVerifiedRevision == current.Revision && _lastVerifiedSource == _session.Source)
        { if (_visible) PublishAcceptContext(current); return; }
        TryConfirmUiaStartup(current);
        if (_pendingInputs.Count > 0 && !TryReconcilePending(current))
        {
            // A stale snapshot may still exactly match the confirmed prefix.
            // It must not clear the pending deadline or render that prefix as if
            // the queued keystrokes had never happened.
            if (Environment.TickCount64 >= _pendingUntil)
            {
                Trace.WriteLine($"Pending input timed out length={current.Text.Length} selection={current.SelectionStart},{current.SelectionEnd}");
                Reset();
            }
            return;
        }
        if (!_session.Matches(current.Text, current.SelectionStart, current.SelectionEnd))
        {
            if (_session.MatchesDocument(current.Text) ||
                _session.MatchesWithExternalSelection(current.Text, current.SelectionStart, current.SelectionEnd))
            {
                _visible = false;
                if (_keyboard != null) _keyboard.AcceptContext = null;
                _preview.HidePreview();
                return;
            }
            // The hook precedes WM_CHAR. Give the editor time to consume the native key;
            // never show or accept a suggestion until the exact predicted text arrives.
            // Keep the current preview visible while the editor processes the key so a
            // Backspace update does not look like a new session or replay its entrance.
            if (Environment.TickCount64 >= _pendingUntil) { Trace.WriteLine($"Update mismatch length={current.Text.Length} selection={current.SelectionStart},{current.SelectionEnd} expectedEnd={_session.End}"); Reset(); }
            return;
        }
        _pendingUntil = 0;
        _lastVerifiedRevision = current.Revision;
        _lastVerifiedSource = _session.Source;
        _editor = current with { Text = "" }; string text = _session.Preview;
        string previewDiagnostic = $"layout={_session.SourceLayout} chars={_session.Source.Length} hintLength={text.Length} selection={current.SelectionStart},{current.SelectionEnd} caretHeight={current.Caret.Bottom - current.Caret.Top}";
        if (previewDiagnostic != _lastPreviewDiagnostic)
        {
            Trace.WriteLine("Preview state: " + previewDiagnostic);
            _lastPreviewDiagnostic = previewDiagnostic;
        }
        if (text.Length == 0) { _visible = false; if (_keyboard != null) _keyboard.AcceptContext = null; _preview.HidePreview(); return; }
        if (!TextEditor.TryGetCaret(current, out var caret))
        {
            // Missing screen geometry is a presentation failure, not a document
            // change. Chromium may expose text before it exposes its caret bounds.
            // Retain the verified session boundary until coordinates become usable.
            Trace.WriteLine($"Caret geometry unavailable; session retained start={_session.Start} chars={_session.Source.Length}");
            _visible = false;
            if (_keyboard != null) _keyboard.AcceptContext = null;
            _preview.HidePreview();
            return;
        }
        if (_visible && text == _lastPreview && caret.Left == _lastCaret.Left && caret.Top == _lastCaret.Top &&
            caret.Right == _lastCaret.Right && caret.Bottom == _lastCaret.Bottom && current.Dpi == _lastDpi &&
            current.FontSizeDip == _lastFontSizeDip && current.FontFamilyName == _lastFontFamilyName &&
            current.FontWeightValue == _lastFontWeight && current.FontItalic == _lastFontItalic &&
            current.FontSource == _lastFontSource) { PublishAcceptContext(current); return; }
        bool first = !_hasAppeared; _visible = true; _hasAppeared = true;
        _lastPreview = text; _lastCaret = caret; _lastDpi = current.Dpi; _lastFontSizeDip = current.FontSizeDip;
        _lastFontFamilyName = current.FontFamilyName;
        _lastFontWeight = current.FontWeightValue; _lastFontItalic = current.FontItalic; _lastFontSource = current.FontSource;
        _preview.ShowPreview(text, caret, current.Dpi == 0 ? 96 : current.Dpi, first, current.FontSizeDip,
            current.FontFamilyName, current.FontWeightValue, current.FontItalic, current.FontSource);
        PublishAcceptContext(current);
    }

    private void PublishAcceptContext(EditorSnapshot current)
    {
        if (_keyboard == null) return;
        _keyboard.AcceptContext = _visible && !_accepting && _targetLayout != IntPtr.Zero &&
            _pendingInputs.Count == 0 && _keyboard.LastSequence == _processedSequence
            ? new KeyboardAcceptContext(current, _processedSequence) : null;
    }

    private bool TryQueuePending(Native.KBDLLHOOKSTRUCT data, bool shift, KeyboardLayout layout)
    {
        if (_session == null || _pendingInputs.Count >= 256) return false;
        int vk = (int)data.vkCode;
        if (vk != Native.VK_BACK && !KeyboardConversion.TryConvert(
                new PhysicalKey(data.vkCode, shift, _capturedCapsLock), layout, out _))
            return false;

        _pendingInputs.Add(new QueuedInput(data.vkCode, shift,
            _capturedCapsLock, layout));
        _pendingUntil = Environment.TickCount64 + 350;
        return true;
    }

    private bool TryReconcilePending(EditorSnapshot snapshot)
    {
        if (_session == null || _pendingInputs.Count == 0) return false;
        try
        {
            var candidate = _session.Copy();
            candidate.ReplayPending(_pendingInputs);
            string document = snapshot.Text;
            int caret = snapshot.SelectionEnd;
            if (snapshot.SelectionEnd > snapshot.SelectionStart)
            {
                if (snapshot.SelectionStart != candidate.End &&
                    !(_uiaStartupUnconfirmed && snapshot.SelectionEnd == document.Length)) return false;
                document = document.Remove(snapshot.SelectionStart, snapshot.SelectionEnd - snapshot.SelectionStart);
                caret = snapshot.SelectionStart;
            }
            if (!candidate.Reconcile(document, caret, caret))
            {
                if (!_uiaStartupUnconfirmed || !candidate.TryRebaseUnconfirmedStartup(document,
                        caret, caret, out candidate)) return false;
                Trace.WriteLine($"UIA startup rebased with pending input chars={candidate.Source.Length}");
            }
            _uiaStartupUnconfirmed = false;
            _session = candidate;
            _editor = snapshot with { Text = "" };
            _pendingInputs.Clear();
            _pendingUntil = 0;
            Trace.WriteLine($"Pending input reconciled chars={candidate.Source.Length}");
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void TryConfirmUiaStartup(EditorSnapshot snapshot)
    {
        if (!_uiaStartupUnconfirmed || _session == null) return;
        if (_session.Matches(snapshot.Text, snapshot.SelectionStart, snapshot.SelectionEnd))
        { _uiaStartupUnconfirmed = false; return; }
        string document = snapshot.Text;
        int caret = snapshot.SelectionStart;
        if (snapshot.SelectionEnd > caret && snapshot.SelectionEnd == document.Length)
            document = document.Remove(caret, snapshot.SelectionEnd - caret);
        if (!_session.TryRebaseUnconfirmedStartup(document, caret, caret, out var confirmed))
            return;
        Trace.WriteLine($"UIA startup rebased start={_session.Start} chars={confirmed.Source.Length}");
        _session = confirmed;
        _editor = snapshot with { Text = "" };
        _uiaStartupUnconfirmed = false;
    }

    private bool TryQueueDeferredStartup(Native.KBDLLHOOKSTRUCT data, bool shift)
    {
        bool capsLock = _capturedCapsLock;
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        uint thread = Native.GetWindowThreadProcessId(foreground, out _);
        var gui = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (thread != 0 && Native.GetGUIThreadInfo(thread, ref gui) && gui.hwndFocus != IntPtr.Zero)
            thread = Native.GetWindowThreadProcessId(gui.hwndFocus, IntPtr.Zero);
        var activeLayout = Native.GetKeyboardLayout(thread);
        if (thread == 0 || !TryLayout(activeLayout, out var layout)) return false;
        var key = new PhysicalKey(data.vkCode, shift, capsLock);
        if (!KeyboardConversion.TryConvert(key, layout, out _)) return false;

        long now = Environment.TickCount64;
        if (_deferredStartupInputs.Count > 0 &&
            (now > _deferredStartupUntil || foreground != _deferredStartupForeground || thread != _deferredStartupThread))
            ClearDeferredStartup();
        if (_deferredStartupInputs.Count == 0)
        {
            _deferredStartupForeground = foreground;
            _deferredStartupThread = thread;
        }
        if (_deferredStartupInputs.Count >= 64) { ClearDeferredStartup(); return false; }
        _deferredStartupInputs.Add(new QueuedInput(data.vkCode, shift, capsLock, layout));
        _deferredStartupUntil = now + 800;
        _deferredStartupWatch.Start();
        return true;
    }

    private bool TryAttachDeferredStartup()
    {
        if (Environment.TickCount64 > _deferredStartupUntil)
        { ClearDeferredStartup(); return false; }
        return TextEditor.TryRead(out var snapshot) && TryAttachDeferredStartup(snapshot);
    }

    private bool TryAttachDeferredStartup(EditorSnapshot snapshot)
    {
        if (_deferredStartupInputs.Count == 0) return false;
        if (Environment.TickCount64 > _deferredStartupUntil)
        { ClearDeferredStartup(); return false; }
        if (snapshot.Foreground != _deferredStartupForeground || snapshot.Thread != _deferredStartupThread ||
            snapshot.SelectionStart != snapshot.SelectionEnd)
            return false;

        var source = new System.Text.StringBuilder(_deferredStartupInputs.Count);
        foreach (var input in _deferredStartupInputs)
        {
            if (!KeyboardConversion.TryConvert(new PhysicalKey(input.VirtualKey, input.Shift, input.CapsLock), input.Layout, out var character))
            { ClearDeferredStartup(); return false; }
            source.Append(character);
        }
        if (source.Length == 0) return false;
        int end = snapshot.SelectionEnd;
        if (end < source.Length || !snapshot.Text.AsSpan(end - source.Length, source.Length)
                .SequenceEqual(source.ToString().AsSpan()))
            return false;
        int start = end - source.Length;
        string baselineText = snapshot.Text.Remove(start, source.Length);
        var session = new InputSession(baselineText, start, _deferredStartupInputs[0].Layout);

        session.ReplayPending(_deferredStartupInputs);
        if (!session.Reconcile(snapshot.Text, snapshot.SelectionStart, snapshot.SelectionEnd))
            return false;

        _session = session;
        _editor = snapshot with { Text = "" };
        _targetLayout = FindLayout(session.TargetLayout);
        ClearDeferredStartup();
        if (_targetLayout == IntPtr.Zero) { Reset(); return false; }
        _watch.Start();
        ScheduleUpdate();
        Trace.WriteLine($"Deferred startup attached chars={source.Length}");
        return true;
    }

    private void ClearDeferredStartup()
    {
        _deferredStartupInputs.Clear();
        _deferredStartupUntil = 0;
        _deferredStartupForeground = IntPtr.Zero;
        _deferredStartupThread = 0;
        _deferredStartupWatch.Stop();
    }
    private async void AcceptPreview()
    {
        if (_disposed || !_visible || _session == null || _editor == null || _session.Preview.Length == 0) { _accepting = false; return; }
        if (_keyboard != null) _keyboard.AcceptContext = null;
        _accepting = true; var session = _session; var editor = _editor; int generation = _generation;
        bool replacementConfirmed = false;
        IntPtr originalLayout = IntPtr.Zero;
        try
        {
            if (!TextEditor.TryRead(out var current) || !TextEditor.SameEditor(editor, current)) return;
            // A click can arrive while the UIA cache still lags behind the keystrokes
            // that formed the visible preview. Wait briefly for an exact text/caret
            // reconciliation; never accept only the confirmed prefix.
            for (int i = 0; _pendingInputs.Count > 0 && i < 10; i++)
            {
                if (TextEditor.TryRead(out current) && TextEditor.SameEditor(editor, current))
                    TryReconcilePending(current);
                if (_pendingInputs.Count > 0) await Task.Delay(20);
            }
            if (_pendingInputs.Count > 0 || _session == null || _editor == null) return;
            session = _session; editor = _editor;
            if (!TextEditor.TryRead(out current) || !TextEditor.SameEditor(editor, current) || !session.Matches(current.Text, current.SelectionStart, current.SelectionEnd)) return;
            originalLayout = current.Layout;
            if (!await TextEditor.TrySwitchLayoutAsync(editor, session, _targetLayout) || generation != _generation) { Trace.WriteLine("Accept switch failed"); return; }
            Trace.WriteLine("Accept layout switched");
            if (generation == _generation)
            {
                bool replaced = await TextEditor.TryReplaceAsync(editor, session, _targetLayout, () => generation == _generation && _accepting);
                replacementConfirmed = replaced;
                Trace.WriteLine("Accept replace result: " + replaced);
                if (replaced && TextEditor.TryRead(out var accepted) &&
                    accepted.SelectionStart == accepted.SelectionEnd &&
                    accepted.SelectionStart == session.Start + session.Replacement.Length)
                {
                    _acceptedInput.Arm(accepted.Text, accepted.SelectionEnd, session.TargetLayout);
                    _acceptedEditor = accepted with { Text = "" };
                }
            }
            // Provider verification precedes the visual completion. Do not hide
            // an unaccepted hint, or leave a 90-ms editing gap before replacement.
            if (replacementConfirmed) await _preview.PlayAcceptAnimationAsync();
        }
        catch (Exception error) { Trace.WriteLine("Accept exception: " + error.GetType().Name); }
        finally
        {
            Trace.WriteLine("Accept finished");
            bool retryable = !replacementConfirmed && generation == _generation && _session == session &&
                TextEditor.TryRead(out var unchanged) && TextEditor.SameEditor(editor, unchanged) &&
                session.Matches(unchanged.Text, unchanged.SelectionStart, unchanged.SelectionEnd);
            if (retryable && originalLayout != IntPtr.Zero)
                await TextEditor.TrySwitchLayoutAsync(editor, session, originalLayout);
            _accepting = false;
            if (retryable && generation == _generation) UpdatePreview();
            else Reset();
        }
    }
    internal static bool TryLayout(IntPtr hkl, out KeyboardLayout layout)
    {
        ushort language = unchecked((ushort)hkl.ToInt64()); layout = language == 0x0419 ? KeyboardLayout.Russian : KeyboardLayout.English;
        return (ulong)hkl.ToInt64() == (language == 0x0419 ? 0x04190419UL : 0x04090409UL);
    }
    private static IntPtr FindLayout(KeyboardLayout desired)
    {
        int count = Native.GetKeyboardLayoutList(0, null); if (count <= 0) return IntPtr.Zero;
        var list = new IntPtr[count]; Native.GetKeyboardLayoutList(count, list);
        return list.FirstOrDefault(hkl => (ulong)hkl.ToInt64() == (desired == KeyboardLayout.Russian ? 0x04190419UL : 0x04090409UL));
    }
    private void Reset([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        _lastVerifiedRevision = -1;
        _lastVerifiedSource = "";
        if (_keyboard != null) _keyboard.AcceptContext = null;
        _uiaStartupUnconfirmed = false;
        _selectAllEditor = null;
        _acceptedBackspacePending = false;
        Trace.WriteLine($"Session reset: {caller} start={_session?.Start} chars={_session?.Source.Length} queued={_pendingInputs.Count} startup={_deferredStartupInputs.Count}"); ClearDeferredStartup(); _lastPreview = ""; _pendingUntil = 0; _pendingInputs.Clear(); _generation++; _session = null; _editor = null; _targetLayout = IntPtr.Zero; _visible = false; _hasAppeared = false;
        if (caller is "Dispose" or "ProcessKey") { _acceptedInput.Disarm(); _acceptedEditor = null; }
        if (!_disposed && _acceptedInput.IsArmed && _acceptedEditor != null) _watch.Start(); else _watch.Stop();
        _preview.HidePreview();
    }
    public void Dispose()
    {
        TextEditor.SnapshotChanged -= OnSnapshotChanged;
        _disposed = true; Reset();
        _keyboard?.Dispose();
        TextEditor.Stop();
        _preview.Close();
    }
}
