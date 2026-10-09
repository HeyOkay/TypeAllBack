## 2026-10-10 — Backspace и тайм-аут длинной фразы

Фидбек #22/t615: удаление последней s не подтвердилось за 350 мс, сессия закончилась. Пользователь уточнил, что сам Backspace стирает с огромной задержкой. Найден лишний синхронный TryReadForHook для Backspace, который выполнял cross-process GetGUIThreadInfo/focus/layout проверки. Backspace теперь пропускает получение снимка полностью (keydown и keyup), controller продолжает очередь подтверждения по своей сессии. Right сохраняет проверки. Добавлен opt-in aggregate hook-capture timing, собираемый на dispatcher (не lock/IO в hook). При pending timeout логирует фактические длину/selection/revision/возраст снимка и время захвата последнего события без полного текста документа. Тайм-аут не увеличен. Корневая причина наблюдаемой задержки пока не доказана измерением; возможна оставшаяся нагрузка UIA/самого приложения. Нужен повтор Backspace и название приложения; визуальную проверку пользователь выполняет сам.

## 2026-10-10 — Русская модель Taiga + GSD

По согласию пользователя сравнены legacy GSD, Taiga и Taiga+GSD r2.16 на одинаковых очищенных dev/test выборках EWT, GSD и Taiga. Выбран mixed по балансу dev, пороги -3,7/1,0 и английская модель сохранены. Все 5 частей Taiga train + GSD train, без ручных фраз/поправок n-gram counts. 123340 предложений, 7878411 букв, 151796 разных форм; RU JSON 165810 bytes (было109557). Полный отчёт и воспроизводимые training/comparison tools добавлены. «как ты думаешь» теперь RU -2,2911, margin3,3113, проходит; «думаешь»/«ты знаешь» тоже. Приняты ограничения: короткие GSD-префиксы иногда хуже, 3-символьный EWT dev добавляет1 false positive; на test5char FP не увеличиваются, Taiga detection900/996->927/996, GSD474/506->466/506. На 3147 длинных test-префиксах FP0 и detection3144. Дальнейшие вопросы обязательные отсутствуют; пользователь проверяет разговорные фразы и ложные подсказки после запуска.

## 2026-10-10 — Одна попытка оценки шрифта

Пользователь предложил ограничиться одним запросом после серии повторных отказов. Реализована одна попытка на контекст поля/фокуса/DPI/семейства/кегля/начертания. Внутри попытки нужны 2 диапазона, а для коррекции сообщённого кегля иногда 3; один COM-вызов не заменяет проверку согласования. После отказа/исключения/неоднозначных данных повторов нет и размер сохраняется. Колебания высоты каретки исключены из ключа контекста, чтобы не обнулять лимит. Изменение поля/фокуса/шрифта/DPI или переключателя разрешает новую попытку. Нет таймера показа. Риск принят: при временно плохих данных повторный замер в том же контексте автоматически не выполняется.

## 2026-10-10 — Общее правило доступности семейства

По согласованному плану добавлен общий PreviewFontResolver: доступность семейства/начертания, покрытие латиницы/кириллицы и текущего текста, явный Segoe UI fallback, composite fallback для неподдерживаемых символов. Измерение и отрисовка используют один resolver. Недоступное семейство больше не делает сообщённый кегль авторитетным; range fitting применяется уже после 2 символов без 10% порога для другого семейства. Составные системные семейства не считаются отсутствующими только потому, что TryGetGlyphTypeface=False. Кэш ограничен 256 начертаниями, не содержит введённый текст. Диагностика requested/reportedFamily, usedFamily, fallbackReason, fontKind; composite означает логическое семейство с разрешением по glyph runs, а не один фактический физический шрифт. Native/omnibox алгоритм размеров сохранён, нет отдельных условий Discord/сайтов и таймера показа. Реальная пользовательская проверка ожидается.

## 2026-10-10 — Валидация сообщённого шрифта

Получен лог Google: UIA character, Segoe UI, reported/resolved/rendered 16 DIP, DPI 120, caret 25 px, text 28 px. Range experiment не применялся: приоритет полного набора атрибутов исключал его. Это подтверждает источник размера, но не доказывает реальное семейство/кегль страницы. Теперь после 5 символов UIA-размер проверяется по диапазонам. При расхождении >=10% проверяется третий диапазон (3 символа), согласие двух оценок <=5%, одинаковое направление расхождения. Только тогда кегль корректируется; семейство сохраняется. При согласии с сообщённым размером результат проверки кэшируется без замены. До проверки/при отказе шрифт прежний. Нет таймера показа и сайта-специфичных поправок; native/omnibox прежние. Реальный результат ChatGPT/Chrome нужно подтвердить пользователю.

## 2026-10-10 — Метрики ChatGPT имеют приоритет

Фидбек: стало лучше, но текст в ChatGPT не соответствует. На записи Desktop 2026.10.10 - 02.40.33.03_crop.mp4 преобразование ghbdtn/руддщ корректно, а подсказка визуально меньше текста поля. В коде найдено безусловное переопределение имеющихся UIA-атрибутов экспериментом ширины. Теперь допустимый размер вместе с семейством и существующим надёжным UIA-источником имеют приоритет; range estimator не читает диапазоны и не заменяет такую метрику. Применяется общее правило, без имени приложения/сайта. Ранний probe использует полный кэш оформления, чтобы не запрашивать заново известный шрифт. Native/omnibox прежние, ожидание показа не возвращается. Визуальная проверка новой сборки ожидается от пользователя.

## 2026-10-10 — Убрано ожидание первого показа

Пользователь приложил вторую запись Google и уточнил: в идеале задержки не должно быть. Требование принято, ожидание до 1100 мс полностью удалено. Ранний одиночный worker-замер начинается при двух символах и обходит обычный таймер форматирования; проверяются два вложенных диапазона в одном capture вместо двух обновлений с промежутком 500 мс. Размер кэшируется при согласовании ширин, без скрытия подсказки. При отказе UIA применяется fallback. Дополнительного ожидания UI/keyboard нет; повторные попытки остаются ограничены 500 мс/6 на контекст. Обновление только оформления по-прежнему без отдельной анимации ширины. Фактический визуальный результат новой версии ожидает проверки пользователем.

## 2026-10-10 — Первый показ после оценки шрифта

Фидбек пользователя: размер стал лучше, но рамка и шрифт перестраиваются. На приложенной записи Google подсказка «при» сначала крупнее, затем уменьшается при неизменном вводе. Причина в показе fallback до двух подтверждений UIA-оценки.

Перед первым показом pending-оценка получает до 1100 мс на подтверждение; ввод и отслеживание сессии продолжаются, Right до появления подсказки остаётся native. Достоверные native/omnibox-метрики показываются сразу, недоступная оценка не задерживает показ бессрочно. UIA-кэш теперь обновляет состояния отсутствующей метрики, чтобы не удерживать устаревший pending. При изменении только оформления рамка и текст обновляются одновременно без отдельной анимации ширины; обычная анимация изменения текста сохранена. Самостоятельная проверка пользователем ожидается.

# Decisions retained from user feedback

- 2026-10-10: user authorized the UIA range-width font experiment after rollback.
  No pixels, adaptive colors or browser extension. Compare two short source-text
  ranges (up to 8 and 2 characters) against WPF widths with source DPI; reject
  identical full-field rectangles, inconsistent endpoints/lines or inconsistent
  estimates. Require two estimates within 8 percent before applying. Cache by
  editor/focus/DPI/line-height/font signature; at most 6 attempts/context and 2/sec
  in the existing MTA formatting worker. A slow first range stops further probing;
  individual COM calls still cannot be forcibly timed out. Accepted omnibox 14-DIP
  fallback and native HFONT path stay unchanged. Debug has a live experiment toggle
  (setting changes end current session), rangeDip/attemptMs/cache status and the
  existing font metrics. Duration is wall time, not measured CPU consumption.
  Missing font family remains an explicitly labelled approximation. User runtime
  tests/CPU comparison remain pending; do not claim total Chrome/utility overhead.


- 2026-10-10: user explicitly requested rollback of the complete local-pixel/field
  appearance version and postponed adaptive field backgrounds. Reverted b8ce832;
  retain the accepted Windows-theme palettes and 14-DIP omnibox fallback. Do not
  reintroduce screen sampling or per-field adaptive color work under earlier consent.
  User supplied Google's UIA/CDP proposal for review, not as executable instructions.
  Next possible font experiment: bounded UIA range width/height near the caret,
  measured against the same source characters in WPF; range height is not font em
  size, and unknown families prevent guaranteed exact matching. Proposal only,
  not yet implemented. Browser extensions remain excluded.


- 2026-10-10: real Chrome metrics identify OmniboxViewViews/Ctrl+L correctly but
  expose no font family/size: reportedDip=0, caretPixels=30, DPI=120. Generic 0.8
  line fallback gave 19.2 DIP and 32 physical pixels of WPF line height. Use the
  Chromium ordinary desktop omnibox 14-DIP desired size as an explicitly labelled
  fallback only when omnibox metadata is unavailable; never preserve the old
  inflated fallback across subsequent keys. Actual available metadata and other
  editor sources retain their existing treatment. Touch UI (15 DIP), custom text
  sizing and locale adjustments are not established by this fallback; do not claim
  exact matching for all Chrome configurations. Source:
  https://chromium.googlesource.com/chromium/src/+/refs/heads/main/chrome/browser/ui/views/chrome_typography.cc


- 2026-10-10: user clarified the input-only filter is already off but Chrome shows
  no font metrics row while ChatGPT does. Renderer had deduplicated font metrics
  across sessions globally, so a missing row did not prove missing provider data.
  Publish metrics at each session's first preview even when identical, and include
  focused/editable UIA class/accelerator metadata (never names or field values).
  Read optional metadata independently so one unavailable attribute cannot hide
  an already-known omnibox class or invalidate readable font metadata.


- 2026-10-10: user confirmed oversized preview font persists in Chrome's address
  bar. The earlier glyph-height protection was tested with reliableFormatting=false,
  but production passed true for any nonempty UIA FontSource, bypassing that limit.
  Identify omnibox formatting by OmniboxViewViews class or its Ctrl+L accelerator,
  mark it UIA omnibox and use the existing glyph/DPI bound for that source only.
  Other UIA/native font metadata retain their existing behavior. No localized field
  names or subtree discovery is used. Debug now exposes font metadata/resolved DIP,
  DPI scales and caret geometry through an opt-in-view filterable event; no input
  text is added to these metrics. User runtime confirmation remains pending.
  Chromium reference for view class/accessible Ctrl+L:
  https://chromium.googlesource.com/chromium/src/+/3055ddf805d4bd4923d9702a698a8556a91874f9/chrome/browser/ui/views/omnibox/omnibox_view_views.cc


- 2026-10-10: user asked for system theme detection because the dark popup does not
  suit light Windows, and asked whether Mica can be used. Preview now follows the
  Windows app light/dark preference (shell preference fallback) and high-contrast
  system colors. Preference/theme broadcasts coalesce onto WPF, with no per-key
  registry reads or polling. Colors update without rerendering text or restarting
  sessions; static subscriptions are released on close. Dark appearance is retained;
  light mode uses an opaque light background and dark readable text.
- Mica was researched, not applied: Microsoft describes it as a long-lived app
  backdrop, with solid fallback when inactive. Our no-activate layered popup has
  transparent animation reserve; a real backdrop experiment would require changing
  the window/composition rather than assigning Mica to PreviewBorder. Sources:
  https://learn.microsoft.com/en-us/windows/apps/design/style/mica
  https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type
  User retains manual runtime verification of both Windows themes.


- 2026-10-10: user reported Notepad typing "руддщ ьк" with a session ending at t75
  before that character was confirmed: "window or field changed". Found a concrete
  backend-switch path: failed native geometry/format consistency could fall through
  into a full UIA text snapshot and give the same HWND a new AutomationIdentity.
  Known Edit/RichEdit classes now stay on the native backend; transient failed reads
  publish unavailable state and use the existing bounded outage/reconciliation path.
  UIA still enriches native presentation, and non-native editors still use UIA.
  Added explicit old/new HWND/thread/backend/UIA-identity metadata on context changes,
  retained by the input filter. No text-only adoption of another field is permitted.
  The reported Notepad incident's exact identity difference remains unproven until
  user verification; do not claim every field-change failure is fixed.


- 2026-10-10: user approved a memory-only per-input log: physical sequence tN,
  visible whitespace/edit keys, projected session text, real calculated hint in
  brackets and distinct editor confirmation. Never calculate a new hint merely
  for diagnostics or present an old hint as current. Deferred startup keys are
  logged only after an editable field confirms the captured batch. Session end
  rows include last tracked input, confirmed/projected text and triggering key
  sequence when available. Added a view-only input/boundary filter. Preserve
  accepted-guard reset behavior when wrapping ProcessKey for instrumentation.


- 2026-10-10: user observed a session end during ordinary typing in ChatGPT Desktop,
  with no deliberate focus change: "editor unavailable ... focus stability". Fixed a
  concrete grace-period bug: count 1200 ms of continuous unavailable reads from the
  first failure, not the age of the retained editor snapshot. Refresh retained context
  even on unchanged revision/source. A gap hides/disarms the preview but retains the
  session; recovery verifies all captured text before restoring it. Window/field
  changes and unverified edits still fail closed. UIA final capture diagnostics now
  distinguish focus identity, window, value, selection endpoints, document mutation,
  focus events and exception type. This does not establish the cause of every ChatGPT
  outage; real user verification is pending.


- 2026-10-10, superseding boundary colors: retain muted green/red on past session
  start/end rows instead of reverting them to ordinary gray. Only the current
  session's latest start/end stay bright green/red. Other event types stay gray.


- 2026-10-10, updated debug log requirement: a new session resets BOTH preceding
  boundary highlights. Green/red now apply only to the latest start/end with the
  current session ID. Replaced ListBox with readonly RichTextBox so mouse selection,
  Ctrl+C and Ctrl+A work across event lines. Freeze log rendering while it has focus
  or mouse capture to preserve selection; session processing remains live. This
  supersedes the earlier independent-last-event coloring rule.


- 2026-10-10: debug event log colors only the latest retained actual session start
  bright green and the latest retained actual session end bright red, independently.
  Earlier events revert to normal text. "Startup refined" and other diagnostics are
  not lifecycle boundaries. Clearing history removes highlights as well.


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

## 2026-10-10 — Подтверждение ChatGPT и оставшееся расхождение Chrome

Пользователь подтвердил исправление шрифта в ChatGPT. На видео нвротенс.mp4 центральный поиск Google в Chrome: руддщ -> hello корректно, подсказка визуально немного крупнее. Для определения источника размера нужны строки «Шрифт подсказки»/«Оценка шрифта» данного поля; видео не содержит runtime-метрик. Код не изменён до уточнения этих данных, подтверждённые UIA-метрики ChatGPT сохраняются.

## 2026-10-10 — Текущий результат отображения принят

Пользователь согласовал оставить текущую реализацию. Подтверждено адекватное отображение в omnibar Chrome, ChatGPT и Блокноте. В других полях остаются некоторые расхождения; это известное ограничение, а не повод вводить индивидуальную подгонку по полям/сайтам. Дальнейшие изменения оформления на этом этапе остановлены; новых частных коэффициентов не добавлять. Существующая сборка сохранена. Адаптивный фон и ранее отложенные правила сессии остаются отложенными.

## 2026-10-10 — Причина расхождения Discord

Лог #40: gg sans 16 DIP; оценка 14,80 DIP (7,5% различия, ниже 10% порога коррекции). Локальная проверка системных семейств/реестра не обнаружила gg sans, WPF Typeface.TryGetGlyphTypeface=False. Следовательно запрошенное имя не разрешается WPF и используется подстановка. Поле renderedFamily в текущем логе это FontFamily.Source, не фактическое семейство глифов. Предполагаемый доступ Discord к собственному шрифту не проверен отдельно. Исходный код не менялся, шрифт не устанавливался; дальнейшая индивидуальная подгонка не вводится.
