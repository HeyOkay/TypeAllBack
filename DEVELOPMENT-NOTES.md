# Decisions retained from user feedback

- 2026-10-10: user reiterated that debug must contain a separate list of independently
  switchable session start rules. Added direct typing, selection replacement and
  deferred startup gates covering the three existing creation paths. Defaults retain
  prior behavior; switches apply live, reset the current session with an explicit reason,
  clear pending input/accepted suppression and reject keys captured under the previous
  policy. Settings are memory-only. Validation/security checks cannot be disabled.
  Runtime UI checks remain with the user per their request.


- 2026-10-09, current-state interview: Chrome, Discord, ChatGPT and Notepad show
  no noticeable lag and no reported false hints. Backspace and selection replacement
  work. Accept can leave the first character or part of a sentence unchanged,
  rarely change nothing; Right sometimes fails. Causes are not yet established.
- The user withdrew the requirement to preserve a session through editing in its
  middle. Do not implement that as an obligatory feature. Preserve working Backspace
  and selection replacement. Manual layout change must end the old session and allow
  a new one; the ten-word limit must end on the space following word ten. These policy
  changes and runtime rule switches belong to M1.2, not the observation-only M1.1.
- M1.1 adds a separate memory-only session debug window. Full captured session text
  is explicitly authorized for display there, not for disk logging or model training.
  Show confirmed versus projected text, starts/ends and Accept stages. Keep 200 events
  and the last completed session only. Refresh at most every 150 ms while visible;
  opening does not activate the window. Ctrl+Alt+F12 toggles it; close hides it;
  --no-debug-window starts it hidden. Existing session/accept behavior is retained.


- The original master prompt is an evolving specification. Later user feedback
  takes priority; record material behavior changes instead of silently following
  contradictory initial requirements.
- The user performs live Notepad checks. Build and regression tests remain the
  developer's responsibility. Closing/relaunching LayoutPreview to update the
  executable is authorized. Never report visual tests without observing them.
- EN-US/RU remain the supported pair. Physical conversion is deterministic.
- The user prefers fewer false hints over a guaranteed hint on the first letter.
  Accumulate language evidence over session segments, including previous words;
  there is no maximum word length of 3–5 characters. Short ambiguous input can
  remain silent until enough evidence arrives.
- The manually curated n-gram lists were rejected because fixes were being tailored
  to conversation examples. They have been removed. Models/en.json and ru.json
  are generated from independent, pinned corpora; do not edit individual counts.
  Validate changes on held-out corpus splits and report limitations explicitly.
- Model data is statistical language knowledge, not a word dictionary. User text
  is not persisted or used as training data. The running app never fetches data.
- General editor support uses native Edit/RichEdit plus a UI Automation TextPattern
  adapter. UIA reads run on an MTA worker; the keyboard hook only sees bounded cached
  snapshots. RuntimeId distinguishes multiple editable fields sharing one HWND.
  Accept revalidates the live field, document, range and actual keyboard layout,
  then uses verified selection plus tagged Unicode SendInput (no clipboard/Enter).
- UIA compatibility depends on the application's editable accessibility provider.
  Read-only, password, non-editable or unavailable TextPattern fields are excluded.
  ChatGPT/Codex composer compatibility requires user verification; the independent
  WPF fixture passed 13 runtime checks. Later repeated runs lost foreground focus
  to an external Slider control and must not be reported as passed.
- UIA caret geometry is optional presentation data. Missing rectangles or geometry
  COM errors hide the preview without discarding a verified input session. Resetting
  here made early characters become baseline text (e.g. ghb + вет after Accept).
- Start UIA sessions directly from an available pre-key snapshot. Requiring another
  complete startup snapshot regressed ChatGPT hints and was removed. Defer only
  keys captured while no editor snapshot exists, checking the exact captured prefix
  at the caret. Match the focused HWND's thread consistently and check post-Accept
  suppression before queuing a new startup.
- Optional diagnostics record lengths, offsets and state transitions only, never
  entered text. They work without --test-input so manual checks use normal hooks.
- A cached snapshot matching only the confirmed prefix must not clear the pending
  input deadline. Reconcile pending keys before rendering or processing new input,
  even when the old prefix itself still matches the document snapshot.
- Chromium composers can expose placeholder text and an implicit paragraph mark
  through TextPattern. When ValuePattern is available, use its actual entered value
  as the canonical document, map an empty placeholder caret to zero, and verify that
  nonempty TextPattern content matches that value with only a newline suffix. Use
  ValuePattern.IsReadOnly when TextPattern's read-only attribute is unsupported.
- Live traces showed that the composer can expose placeholder text through BOTH
  patterns (initial start=19, documentLength=19; first delivery length=1). During
  the first unconfirmed UIA edit only, adopt offset zero if the complete current
  document exactly equals the captured physical-key source and the collapsed caret
  is at its end. This updates session bookkeeping only. No old document characters
  enter the corrected range. After the first confirmed delivery, rebasing is disabled.
- Backspace in an active session queues physical repeats without synchronous editor
  reads/hashes in the keyboard hook. The watcher reconciles a complete deletion batch;
  native input is forwarded immediately. Dispatcher preview updates are coalesced.
- Preview overlays following document text with a solid opaque background at a
  small caret gap; it reserves no space in the target editor. Entrance fades the
  label only, keeping the background opaque. Accept retains its dissolve animation.
- Capture the first printable key replacing a selection before treating selection
  movement as dormant editing. The predicted baseline removes only the selected
  range; new typed content includes that first key, and unselected text remains
  untouched. Ctrl+A intent bridges a briefly stale UIA range in the same editor;
  replacement is still allowed only after the exact predicted document/caret match.
- The Backspace fast path is unconditional for ordinary Backspace, including when
  no tracked range remains. Ctrl+A deletion clears captured content without reading
  the editor in the hook or disarming accepted-layout suppression. The native guard
  refreshes once on the next printable/editing key, rather than on every repeat.
- Native-editor UIA caret fallback runs on the existing MTA worker, never on the
  WPF thread that owns the keyboard hook. Publish geometry only when fresh native
  document/selection/DPI still match; consume only bounded, matching cached data.
- Preview font size uses UIA's point-size attribute converted to DIP; missing font
  metadata retains the established size. Caret-box height no longer resizes an
  existing preview. Measure the displayed TextBlock itself and use compact padding
  to avoid mismatched glyph metrics appearing as a trailing blank.
- Keyboard hook now owns an independent Win32 message-pump thread. It captures
  immutable pre-key cached snapshots/modifiers and posts processing to WPF without
  waiting. It never reads native document text, calls COM, or renders. Right is
  consumed only by an atomically published confirmed-preview context matching the
  focused cached document, selection, layout and physical-input sequence; queued
  or intervening input invalidates that context. All Accept writes revalidate live.
- Compatibility work targets Chrome search/forms, Discord composers, Windows Start
  search/native fields and VS Code editor/search. Detect Win32 Edit/RichEdit variants
  and WinForms wrappers by known class names. UIA detects a writable TextPattern by
  capability and may resolve a nonfocusable text child to its editable parent.
  Menu mode alone does not exclude a genuinely editable field; password, readonly,
  ambiguous text/caret and IME contexts remain excluded.
- UIA Accept anchors its replacement range at the verified caret and walks only
  captured EN/RU characters, avoiding UTF-16 vs Character-unit errors in old emoji
  or combining text. Both backends supply font name/size to the shared preview.
- Expanded compatibility is not proof that each named third-party app has passed
  runtime testing. Fields without a writable TextPattern/native editor contract
  still need another backend or accessibility support in the host application.
- Bind a UIA focused element to the native focused/foreground owner process before
  combining its text with Win32 focus/layout/geometry. UIA focus can lag a window
  switch; a foreign application's accessibility element must never become a snapshot.
- Chrome omnibox inline completion is external selected content after the typed
  prefix. Preserve the segment only if removing that selected range recovers the
  exact predicted document, including its baseline fingerprint. Startup may confirm
  captured keys before an entirely selected generated suffix. Hide while selection
  is active; Accept still requires a collapsed, fully verified caret.
- Obtain UIA caret bounds from an adjacent glyph, not the collapsed range (which
  Chromium can map to the whole field). WinUI fields with no child native HWND use
  the foreground HWND plus verified UIA field/process identity. Rejected UIA contexts
  log only stage, process ID, control/window class and flags, never text or titles.
- Start/InputSite descendant scanning caused user-observed system-wide lag and was
  removed. Never repeatedly enumerate UIA subtrees as a fallback during polling.
  Unsupported contexts poll at 500 ms rather than 100 ms (focus events still wake
  the worker). Start's container-only field remains unsupported pending a targeted
  event-driven adapter. VS Code remains deferred by user request.
- SearchHost's differing display Value is not authoritative when a writable
  TextPattern independently confirms stable text and selection twice. Diagnostics
  contain only layout/count, hint length and geometry metrics and are opt-in.
- Chrome omnibox exposed an oversized font attribute. Bound the reported DIP size
  by the adjacent glyph/line height after physical-pixel-to-DIP conversion. Retain
  the established size when geometry suddenly resembles a whole paragraph and
  font metadata does not justify that change. No subtree polling is reintroduced.
- Read UIA font family/point size/weight/italic from an adjacent actual character,
  not only an empty caret range. Standard Win32 Edit fonts can be read via bounded
  WM_GETFONT + readonly GetObjectW(LOGFONT); negative height is em height in pixels.
  Prefer that exact font for plain Edit, keep glyph-local UIA formatting for RichEdit.
- The popup's actual WPF DPI can differ from the target HWND DPI. Normalize font,
  padding and transparent reserve into the popup's own DIP scale while retaining
  physical target position/size. Refresh locally on DPI change without querying UIA.
  Opt-in font diagnostics expose source, family, resolved size, both DPI scales and
  geometry only; no input text or UIA subtree enumeration is recorded.
- 2026-10-09: missing font metadata during native/UIA reconciliation alternated
  confirmed Notepad size 14.67 DIP with inferred 14.08 DIP (and 37.33 with 35.20
  for larger text), causing user-observed jumping. Preserve confirmed size/style
  during missing metadata. Font-only enrichment may cross document/selection
  changes in the same fresh editor/DPI context; caret geometry may not.
- Cache formatting per field/focus generation/DPI for 500 ms. A failed formatting
  read retains the prior font and cannot invalidate verified document state.
  Clear cached presentation at a context change; never enumerate UIA subtrees.
- Accept uses text/selection-only captures, waits briefly for asynchronous selection
  confirmation, and tolerates transient unavailable reads after injection. Verify
  replacement before playing the completion animation instead of delaying the edit
  by 90 ms. If unchanged text/caret are still proven after a failure, restore the
  original layout through the checked path and retain the hint for retry. Uncertain
  document state still ends the session.
- Validation for this change: 334 regression checks, hidden Win32 HFONT smoke,
  independent keyboard-thread smoke, and Debug build pass. The isolated WPF UIA
  replacement test was blocked because its window could not acquire foreground
  focus; it performed no injection. Notepad/ChatGPT visual and Accept checks remain
  manual user checks, not claimed as passed.
- 2026-10-09, performance pipeline: all facade reads use immutable background
  snapshots; native document reads, layout requests and checked replacements run
  on the same worker, never on the WPF or hook thread. Each fresh Accept command
  still revalidates full document/selection before editing. Native replacement
  also checks cancellation before selecting and before replacing the range.
- Worker wakes on input/focus and supported field text/selection events. Refresh
  intervals are 35 ms active (80 ms for slow captures), 500 ms supported idle,
  1000 ms unsupported idle. A minimum interval also caps wake storms. Snapshot
  freshness allows the supported idle interval; this is presentation freshness,
  not permission to replace text without a new checked capture.
- Bind the focused UIA element/pattern once per context; reuse native UIA objects
  so polling does not repeatedly detach/attach event handlers. Publish a content
  revision only when text/selection/caret/layout/format changes. The UI coalesces
  notifications and skips hash/render work for the same verified revision AND
  session source. The 250-ms UI fallback reads cached state only.
- UIA offsets use the current selection text plus either a prefix or suffix query
  chosen using the previous caret as a cost hint. Verify the returned boundary
  content and selected text against the document; old offsets never authorize
  edits. This avoids copying a long prefix while typing at a document's end.
- Windows search adapter is restricted to SearchHost/StartMenuExperienceHost and
  InputSite focus containers. At most two discovery attempts per focus context,
  separated by 300 ms, with at most 64 visited elements, depth 5 and a nominal
  100-ms traversal budget (an individual COM call cannot be forcibly timed out).
  Require exactly one enabled, nonpassword writable Edit; ambiguous, incomplete
  or expired traversals fail closed. This is not continuous subtree polling.
- Opt-in --performance-diagnostics PATH logs 10-second aggregate count/mean/max
  durations and counts over 16/50 ms for capture, binding, caret, formatting,
  dispatch, rendering and Accept commands. No text, window titles or keys are
  logged; disk writes happen only on the worker.
- Validation: 358 regression checks; actual Win32 font and hook-thread smoke
  checks; four real UIA discovery checks in an invisible WPF fixture (unique
  editable field, ambiguity, readonly/password exclusion, focus preservation).
  Full UIA Accept fixture remains blocked before input by foreground acquisition.
  Actual Windows Start/Notepad/ChatGPT behavior still requires the user's checks.
- All backends now read asynchronous snapshots, so the short Ctrl+A selection
  intent and accepted-layout suppression apply to native snapshots too. Refresh
  the guard's last confirmed timestamp, keeping its accepted layout, so a brief
  capture gap cannot restart suggestions in the same typing flow.
