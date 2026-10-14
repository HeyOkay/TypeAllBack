## 2026-10-10 — Opt-in исправление перед Enter

Пользователь согласовал эксперимент. Опция defaultoff в отладке, до перезапуска. Hook резервирует только plain Enter при точном опубликованном accept context; keyup/autorepeat/второй Enter во время операции подавляются, внутренний Enter tagged ownInput не перехватывается. Не готовая подсказка/Shift+Enter — native. После checked replacement текущего участка отдельная worker-команда повторно проверяет поле/полный итоговый текст/каретку/layout, generation/inputSequence/опцию/модификаторы и только тогда SendInput одинdown/up безretry. Неясная замена или новаяклавиша/поле отменяют отправку, причина в логе. Анимация Enter-пути пропускается для меньшей задержки. Нет allowlist по сайтам, явное включение пользователем, проверять сначалаChatGPT/Discord; назначение Enter сохраняетприложение. Автоотправка в пользовательские приложения агентом не тестируется; проверку выполняет пользователь.

## 2026-10-10 — Реальная проверка оптимизации отладки

Пользователь сообщил готово (ujnjdj) после повторной открытой фазы. Активные111,093с,378keyboardevents: CPU6,723% одного ядра; debug-refreshmean1,797ms vs9,612ms исходной фазы, >16ms9/702 vs187/676; рабочая память в конце172,543MiB/private98,754MiB/managed22,179MiB. Исходная открытая фаза112,032с/491events имела CPU13,696%, endWorking241,484MiB/private170,406MiB/managed71,863MiB. Разный объём ввода/прогрев не позволяет объявлять точное causal2xCPU снижение. Cumulative allocations1531,221->191,394MiB не означают peakresidentmemory. Остались raredebug-refresh191,657ms иdispatcher204ms; дальнейшая адресная profiling нужна, M1 не завершён целиком. PERFORMANCE-REPORT.md содержит данные и ограничения. Код на этом шаге не менялся, после завершения сбора приложение возвращено в обычный режим без записи performance log.

## 2026-10-10 — Измеренная нагрузка окна отладки

В первой пользовательской фазе debug-refresh средние по10с интервалам доходили до23,334ms, отдельные максимумы120,305ms; allocation counter ~1,5GiB после активной работы. Hook capture обычно доли ms. По коду журнал пересоздавал до200 Paragraph/Run на каждое изменение и сохранял Undo при read-only. Исправление: инкрементальные вставка/удаление по eventNumber, повторное использование строк, переокрашивание только при изменении цвета, явное отключение Undo в read-only log/полях. Фильтр/выделение/копирование/следование и оттенки сохранены. Причина роста live heap ещё не доказана профилем удержания; это исправление измеренной UI/alloc нагрузки, не заявление об устранении всех утечек. Первичная открытая/скрытая фаза записываются до перезапуска, новая версия требует отдельного сравнения.

## 2026-10-10 — Инструменты проверки M1 performance

Пользователь согласовал замеры CPU/задержек/памяти. Opt-in performance diagnostics теперь разделяет visible/hidden debug stages, измеряет debug-refresh, раз в10сек worker пишет только process CPU/durations, working/private/managed/heap/allocated memory и GC counts. Нет текста/названий полей/клавиш, принудительного GC, фоновых сетевых обращений и дискового IO в hook/UI. Числовые данные собираются в workspace для сравнения обычного ввода с открытой/скрытой отладкой. Подозрение на избыточную память RichTextBox/Undo пока не считается измеренным дефектом, алгоритм отображения не менялся.

## 2026-10-10 — Участки ручной смены раскладки

Пользователь согласовал одну сессию с границами участков вместо завершения на ручной смене. Смена EN/RU скрывает предыдущую подсказку, начинает пустой текущий участок в End без нового session ID. Оценка текущего участка, принятие только его диапазона. Транзакция замены проверяет весь документ с прежними участками/префиксом как неизменяемым baseline. Переключение обратно не реактивирует старый участок. Backspace через границу сохраняет прежний остаток, текущий участок остаётся пустым до новых символов. Автоматическое переключение принятия не создаёт boundary (_accepting). Принятие по-прежнему завершает принятую сессию; следующее подавление раскладки/сессия прежние. Лимит10слов всё ещё отложен. Реальная проверка пользователем ожидается.

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

## 2026-10-10 — Backspace в ChatGPT подтверждён

Пользователь уточнил, что задержка Backspace была в ChatGPT, и после bd09f44 сообщил «теперь все хорошо». Удаление принято для этого проверенного сценария. Все приложения/варианты принятия этой репликой не подтверждены. Код и сборка сохранены.

## 2026-10-10 — Предложенные сценарии замены приняты пользователем

На проверку всей фразы после Backspace/повторного набора, принятия Right и мышью, сохранения заранее введённого префикса пользователь ответил «все проверил все хорошо». Эти сценарии приняты в выполненной проверке; конкретный список приложений в ответе не указан. Не считать это универсальным подтверждением всех Windows-полей. Код и сборка не менялись.

## 2026-10-10 — Участки раскладки приняты

Пользователь подтвердил «да все работает» после smoke ручного EN->RU->EN и принятия текущего участка при сохранении прежнего текста/session ID. Поведение принято в выполненной проверке. Перечень приложений и все edge cases этой репликой не подтверждены. Код/сборка сохранены; следующая проверка — отправка, вставка и независимый переход между полями.

## 2026-10-10 — Проверки границ приняты, следующий шаг performance

Пользователь подтвердил предложенные отправку/первый ввод, вставку с сохранением префикса, смену поля/возврат. Конкретные приложения в ответе не перечислены. Короткое наблюдение текущего процесса за10,013с: прирост CPU0мс на точности счётчика, working set240MiB/private170,6MiB; это не stress/утечки benchmark. Следующая проверка M1 — текущие CPU/latency и рост памяти, сравнение видимой/скрытой отладки; историческую полную серию принятий не объявлять выполненной.

## 2026-10-10 — Исправление перед Enter принято пользователем

Пользователь подтвердил «все проверил работает как нужно» в ответ на предложенный чек opt-in исправления перед Enter, единственной отправки и сохранения обычного/Shift+Enter. Выполненные сценарии приняты; приложения и количество попыток не указаны. Отдельные искусственные отказы/гонки или мгновенный Enter до подтверждённой подсказки этим ответом не подтверждены. Опция остаётся выключенной по умолчанию. Код и сборка сохранены.


## 2026-10-10 — Предварительная иконка TypeAllBack

Пользователь согласовал временно взять круглый синий векторный вариант с белой возвратной стрелкой. Assets/TypeAllBack.svg сохраняет редактируемую геометрию; ICO включает 16/24/32/48/64/128/256 px. Иконка встроена в исполняемый файл и явно назначена окну отладки. Логика ввода не менялась. Полное переименование проекта и значок трея пока не выполнялись.


## 2026-10-10 — Возврат задержки Backspace и несколько процессов

Пользователь сообщил об огромной задержке Backspace. Обнаружены четыре процесса одной сборки; старое исключение Backspace из синхронного чтения focus snapshot сохранено. Лишние экземпляры остановлены. Добавлен атомарный именованный объект на пользователя/Windows-сессию: повторный запуск завершается до установки hook и запуска worker. Объект удерживается до завершения приложения, без ожиданий и владения mutex. Связь задержки с несколькими обработчиками предполагается, окончательное подтверждение требует повторной пользовательской проверки.


## 2026-10-10 — Трей TypeAllBack и Enter по умолчанию

Пользователь подтвердил исчезновение задержки Backspace после устранения четырёх экземпляров. Исправление перед Enter включено по умолчанию; выбор сохраняется. Добавлено меню трея с Mica (DWM main-window backdrop на Windows 11 build 22621+, обычный фон при недоступности/высоком контрасте), паузой обработки, Enter, отладкой, автозапуском, переключателем проверки обновлений и ручной проверкой, выходом. Отладка теперь скрыта при обычном запуске, --debug-window открывает её. Пауза сохраняет журнал и снимает hook и приостанавливает опрос worker, устаревшие события прошлой hook-сессии отбрасываются. Настройки в LocalAppData/TypeAllBack/settings.json. Источник релизов отсутствует (git remote пуст); механизм HTTPS-проверки JSON готов, адрес UpdateFeedUrl необходимо согласовать. Никакого скачивания/установки обновлений без пользователя.

Проверки: 580 регрессионных проверок; скрытый WPF smoke проверил старт на паузе, единственный hook при повторном включении, снятие и восстановление hook, создание HWND меню/инициализацию backdrop без инъекции ввода. Реальное визуальное отображение Mica и сценарии меню оставлены для проверки пользователем. При изменении паузы кэш снимка сбрасывается, поколение focus увеличивается; снимок, завершившийся из прошлого поколения, не публикуется.


## 2026-10-10 — Выбранный звук подтверждения замены

Пользователь выбрал Desktop/29e177c8-c0c1-49b7-8f16-c4d7c3e5dda7_2.wav (PCM16, 48 kHz stereo, 477 ms). Файл скопирован без обработки в Assets/Correction.wav и встроен. Сигнал только после положительного backend-подтверждения замены (мышь/Right/Enter), не при появлении подсказки или отказе. Он подтверждает замену, а не отправку сообщения: при последующей отмене Enter исправленный текст мог остаться в поле. Отдельный worker загружает один раз и запускает асинхронное воспроизведение из закреплённого буфера; поток ввода/редактора/UI не ждёт звука. Поверх играющего сигнала новые не накладываются. В трее «Звук исправления», по умолчанию включён, выбор сохраняется.

## 2026-10-10 — Замена звука на 1.wav
Пользователь выбрал Desktop/1.wav. Исходник float32 WAV 48 kHz stereo, 477 ms переведён в PCM16 WAV для существующего PlaySound; длительность, частота, каналы сохранены, дополнительные эффекты/нормализация не применялись. Assets/Correction.wav заменён. Логика и переключатель звука сохранены.

## 2026-10-10 — Обновлённый файл 1.wav

Пользователь повторно выбрал Desktop/1.wav; содержимое отличается от предыдущего варианта. Встроенный звук обновлён из текущего файла с преобразованием float32 в PCM16, без дополнительных эффектов или нормализации. Частота 48 kHz, stereo, длительность 477 ms сохранены.


## 2026-10-10 — Оформление меню по HaloBattery

Пользователь попросил меню как HaloBattery. Изучен открытый flyout.py (HeyOkay/HaloBattery): адаптированы компактные отступы и строки, Segoe UI Variable Text с Segoe UI fallback, галочка Segoe Fluent Icons/MDL2, разделители и подсветка по теме, заголовок/подпись, мягкое появление 120 ms и положение над курсором с отступом. Реализация остаётся собственной WPF; исходник HaloBattery не переносился. По прежнему выбору пользователя сохранён Mica, а не Acrylic оригинала. Пункты и настройки не изменялись.


## 2026-10-10 — Полное оформление HaloBattery

Пользователь уточнил «полностью скопируй оформление». Это заменяет прежнее требование Mica для меню: включён Acrylic с теми же оттенками tint #99202020/#99F3F3F3, AccentState=4/Flags=2; оставлен обычный фон для недоступного эффекта/высокого контраста. Выравнивание заголовка по тексту строк, одинаковый шрифт 13 DIP, центрирование галочки, естественная высота строки, рамка DWM, отступы и подсветка по образцу. Позиция привязана к основной/дополнительной панели задач, включая верхний/боковые края и авто-скрытие. Собственная реализация WPF, источник визуального референса https://github.com/HeyOkay/HaloBattery/blob/main/flyout.py.

## 2026-10-10 — Пользователь подтвердил проверки оболочки

Пользователь сообщил «проверил» в ответ на список: оформление меню и закрытие по внешнему клику/Esc, пауза и повторное включение, сохранение переключателей Enter/звука после перезапуска, автозапуск с Windows. Эти сценарии приняты в выполненной пользовательской проверке. В процитированном старом списке указан Mica; текущая согласованная оболочка уже использует Acrylic по последующему требованию полного оформления HaloBattery. Повторного подтверждения Mica не подразумевается. Установка/обновления/универсальная совместимость этим сообщением не подтверждены. Следующий предлагаемый этап — упаковка первой устанавливаемой версии TypeAllBack и источник релизов; пока не начат.


## 2026-10-10 — TypeAllBack 0.1.0 и MSI

Пользователь согласовал переименование и первый установщик. EXE/assembly/product/file metadata теперь TypeAllBack 0.1.0; проект csproj и внутренние пространства имён сохранены для стабильности сборки. Настройки и mutex совместимы с прежними. Автозапуск старого EXE в той же папке мигрируется, только если совпадает точная собственная запись. WiX SDK 5.0.2 восстанавливается локально через NuGet, глобальных инструментов нет. MSI per-user x64, self-contained .NET10, стабильные GUID/UpgradeCode, HKCU keypath компонентов, ярлык, сохранение настроек вне InstallFolder, перенос ранее включённого автозапуска, очистка своей записи при uninstall (не при major upgrade). Пакет 48.2 MiB. Build/ICE без ошибок и предупреждений (только ICE91 намеренно исключён для фиксированного perUser scope). Проверены таблицы MSI и автономный запуск с недоступным внешним runtime; settings.json не изменился. Фактическая установка/upgrade/uninstall пользователем ещё не подтверждены; онлайн-источник релизов по-прежнему не выбран.

## 2026-10-10 — Установка TypeAllBack 0.1.0 подтверждена

После передачи MSI и инструкции закрыть утилиту, установить пакет, запустить из меню «Пуск» и проверить сохранение настроек пользователь сообщил «проверил все работает». Установка и запуск приняты в выполненной пользовательской проверке. Обновление поверх предыдущего MSI, удаление и получение онлайн-обновлений этим ответом не подтверждены. Следующий шаг — согласовать источник релизов и проверить цикл обновления отдельным пакетом.


## 2026-10-10 — Папка репозитория и лицензия

Пользователь запросил папку TypeAllBack на рабочем столе и запрет коммерческого использования без его согласия. Единственный репозиторий целиком перенесён из Desktop/LayoutPreview в Desktop/TypeAllBack, включая .git, без создания второй копии. Git-история сохранена. Добавлен неизменённый официальный PolyForm Noncommercial 1.0.0, Required Notice Copyright2026HeyOkay (текущий Git user.name), отдельная политика предварительного письменного коммерческого разрешения. Собственные файлы отделены по лицензированию от ранее лицензированных CC BY-SA данных моделей, их лицензия/атрибуция сохранены. Лицензионные документы включаются в последующие сборки/публикации. Установленная утилита и сохранённые настройки не менялись; GitHub remote не создан, публикация не выполнялась.


## 2026-10-10 — Публичный GitHub и прямое обновление

Пользователь согласовал публичный репозиторий, затем уточнил загрузку без браузера и автоматическую установку/закрытие/перезапуск приложения. Создан https://github.com/HeyOkay/TypeAllBack. GitHub API latest release служит источником: выбирается только MSI win-x64 этого репозитория с digest SHA256 и ограниченным размером. Загрузка асинхронно, допускает только HTTPS GitHub/CDN редиректы, проверяет размер/хеш, поддерживает отмену; UI прогресс ограничен10Hz. После загрузки отдельный локальный PowerShell helper ждёт выхода текущего процесса, повторно проверяет SHA256, выполняет per-user MSI /qn /norestart, запускает установленный EXE. При ошибке пишет журнал, пытается вернуть прежнюю версию и показывает ошибку. Внешний браузер не используется, установщик вручную не открывается. Автопроверка не прерывает работу без выбора обновления пользователем. 0.1.1 также включает LICENSE/уведомления/лицензии моделей. Для перехода с0.1.0 необходима однократная установка0.1.1, старый бинарник механизма не имеет. В GitHub Actions добавленыWindows checks и сборка/публикация MSIпо v* tag. Реальная автоматическая установка/rollback/перезапуск ещё требуют пользовательского чека.


Публичная CI и сборка0.1.1 успешны; фактическая загрузка GitHubMSI кодом приложения с SHA256 успешна, MSI не выполнялся. В0.1.2 передача установки дополнена подтверждением helper.ready перед выходом приложения, timeout5s сохраняет работающую версию; скрипт сохраняетсяUTF8 BOM для правильных русских сообщенийWindowsPowerShell5.1. Реальная установка/перезапуск требуют чека пользователем.

Проверка реального WindowsPowerShell5.1 выявила недоступность Get-FileHash при наследовании PSModulePath отPowerShell7. В0.1.3 helper использует .NET File/SHA256/Process напрямую, без зависимости от модулей PowerShell. Проверка готовности до закрытия оставляет текущую версию работающей при раннем отказе.

Итог0.1.3: локальные592проверки иCIуспешны, MSI собран и опубликован GitHubActions https://github.com/HeyOkay/TypeAllBack/releases/tag/v0.1.3. Проверено распознавание реального релиза, прямое скачивание MSI и SHA256 2f3f2dcdb939be16861ab81aa324cb7b330c45a06e2e7fbf1655225c88d0b43d. Отдельный smoke встроенногоhelperподWindowsPowerShell5.1 подтвердил ready и ожидание родителя, MSI не запускался. Фактические install/rollback/restart остаются пользовательским чеком; установленная0.1.0 агентом не обновлялась.


## 2026-10-10 — Критическое зависание Chrome при открытии GitHub

Пользователь сообщил: https://github.com/HeyOkay в Chrome зависает сразу при открытии с запущенным TypeAllBack, без клика/набора. Предполагаемый источник — глобальная UIA-подписка и чтение DocumentRange обычной страницы. Глобальная UIA-подписка заменена native out-of-context foreground/focus событиями на существующем потоке клавиатуры. До ввода Chromium не опрашивается через UIA; подтверждённые поля продолжают обновляться. Document-роль Chromium без явно writable ValuePattern отсекается до атрибутов всего документа. Новые правила не зависят от URL. Фактическое устранение зависания требует повторного пользовательского чека; воспроизведение агентом пока не подтверждено.


## 2026-10-10 — Подтверждение и хотфикс 0.1.4

Пользователь проверил исправленную сборку и сообщил «помогло», затем попросил выпустить хотфикс. Устранение зависания Chrome при открытии профиля GitHub подтверждено пользователем. Версия поднята до 0.1.4; выпуск через штатную сборку и публикацию MSI в GitHub Releases.


## 2026-10-10 — Упорядочивание репозитория

По просьбе пользователя сокращён список верхнего уровня GitHub: исходники и ресурсы приложения — src/TypeAllBack, установщик — src/Installer, пользовательская и техническая документация — docs. Проверки — Tests, инструменты — Tools. Обновлены пути проектов, сборки MSI, CI, запуска VS Code и README. Лицензии верхнего уровня и установленная структура программы сохранены.


## 2026-10-14 — Потеря первой буквы, обрывы сессии, шрифт Discord

Жалоба: иногда первая буква не попадает в сессию и не заменяется при принятии; подсказка пропадает без видимой причины; в Discord подсказка крупнее и не на уровне текста.

Найдено по коду:
- Спящая сессия (документ совпал, каретка в другом месте) возвращала Next() для печатного символа без захвата; следующая клавиша начинала сессию после него. Теперь печатный символ при свёрнутой каретке вне конца сессии завершает её и сразу начинает новую с этого символа.
- Сбросы из-за несовпадения/смены поля/отсутствия снимка отбрасывали неподтверждённые клавиши. Теперь они переносятся в отложенное начало и подтверждаются, только если обновлённый снимок содержит ровно эти символы перед кареткой. Окно отложенного начала 800 → 2000 мс от последней клавиши; снимок, полученный ≥400 мс после последней клавиши и не содержащий их, отклоняет их сразу.
- Таймаут подтверждения 350 мс считался по часам, а не по свежести снимка: медленный Chromium/Electron обрывал сессию. Теперь отказ только по снимку, снятому после срока, плюс жёсткий предел 1500 мс.
- Одиночный несовпавший снимок после подтверждения сразу завершал сессию; теперь несовпадение должно сохраниться в снимке через 250 мс (подсказка на это время скрыта, принятие недоступно).
- Гонки «изменилось во время снимка» публиковали null, что скрывало подсказку и запускало 1200-мс ожидание; теперь сохраняется предыдущий снимок (в пределах 750 мс). Смена фокуса/окна/защищённого поля по-прежнему сбрасывает.
- Временное отсутствие геометрии каретки при неизменной подсказке больше не скрывает её.
- Потерянное отпускание Ctrl/Alt/Win (Win+L, защищённый рабочий стол) навсегда скрывало подсказки; флаги сверяются с физическим состоянием.
- Защита принятой раскладки не снималась Enter/навигацией (ранний return) и после Enter-отправки; следующее сообщение в той же раскладке не получало подсказок. Исправлено.
- Шрифт: при недоступном семействе (gg sans) оценка по ширине диапазонов — единственный источник размера; одна попытка с бюджетом 25 мс часто не успевает в Electron. Теперь до 3 попыток с бюджетом 60 мс; для полных сообщённых атрибутов остаётся 1 попытка/25 мс.

Сборка в облачной среде невозможна (нет доступа к .NET SDK); требуется dotnet build и регрессионные проверки на Windows. Вертикальное смещение в Discord не диагностировано: нужны строки «Шрифт подсказки» и «Оценка шрифта» из отладки Discord.

## 2026-10-14 — Запоминание размера для недоступных шрифтов

Скриншоты Discord Desktop: одна и та же подсказка то совпадает с текстом, то крупнее и ниже. Причина: gg sans недоступен WPF, размер берётся из оценки по ширинам диапазонов, которая выполняется заново в каждом контексте поля/фокуса и иногда не успевает; до успеха показывается сообщённый кегль 16 DIP в Segoe UI. Общее правило без условий по приложениям: FontScaleMemory (%LOCALAPPDATA%\TypeAllBack\font-metrics.json) хранит отношение оценённого размера к сообщённому для пары «семейство/начертание → fallback» либо абсолютный размер для поля без кегля (ключ по классу поля). Сохранённое значение применяется сразу при первом снимке нового поля/сессии; новая оценка уточняет его скользящим средним, а при расхождении ≤6% отображаемый размер не меняется. Поля с доступным сообщённым шрифтом (Блокнот, ChatGPT) и omnibox не затрагиваются. Первая встреча с новым шрифтом по-прежнему зависит от успешной оценки. Сборка и визуальная проверка — на Windows пользователем.

## 2026-10-14 — Эксперимент «стеклянная подсказка»

По просьбе пользователя (ориентир — liquid glass; репозиторий archisvaze/liquid-glass изучен, без лицензии — код не переносится, взят только принцип: блик и кромка зависят от формы скоса, а не от фона). Настоящее преломление требует пикселей под окном (постоянный захват экрана) и отложено. Реализовано: переключатель трея «Стеклянная подсказка (эксперимент)», по умолчанию выключен, сохраняется в settings.json (GlassPreview). GlassBackdrop — отдельное неслоистое popup-окно (HwndSource, NOACTIVATE/TOOLWINDOW/TRANSPARENT/TOPMOST) ровно под пилюлей, с тем же accent-акрилом, что у меню трея (AccentState 4, светлый tint 0x40), скруглением DWM и без системной рамки; системный DWM backdrop не используется из-за сплошной подложки у неактивных окон. Пилюля в этом режиме: полупрозрачный tint + верхний блик одним DrawingBrush, градиентная кромка (свет сверху-слева), радиус 8. Окно акрила не масштабируется и не сдвигается вместе с пилюлей, поэтому в стеклянном режиме нет zoom-in и анимации ширины, а при принятии акрил скрывается в начале анимации. Высокий контраст, выключенная прозрачность Windows или отказ API — прежний непрозрачный вид; статус в событии «Оформление». Риски: известная задержка accent-акрила в Windows 10 при перемещении окна; читаемость над текстом поля. Сборка и визуальная проверка — пользователем.

## 2026-10-14 — Цвет поля и стекло по макету

Скриншоты пользователя: светлая тема Windows на тёмном поле Discord и тёмная тема на светлом поиске Google — подсказка выглядит чужеродно. Первая версия стекла (серый tint + белый блик) отклонена как «стрёмно». Согласован макет (Design-артефакт «Подсказка TypeAllBack — варианты оформления»), выбран вариант «Стекло в цвет поля».

FieldColors: фон поля — доминирующий цвет полосы в несколько физических пикселей справа от каретки (BitBlt SRCCOPY без CAPTUREBLT, доминанта ≥60% полосы, иначе неизвестно), на UIA worker после публикации снимка, не чаще раза в 10 с на поле и только пока подсказка не показана (окно подсказки не исключается из захвата, чтобы оставаться видимым на скриншотах/демонстрации экрана пользователя). Изображение не хранится. Обычный вид: фон = поле, смещённое к контрасту на 8%/5%, рамка 20%/16%, текст 62%/58%. Стекло: акрил с tint цвета поля, заливка поле+10%/4% с альфой 0xB8/0xCC, кромка светлая сверху-слева и слабая снизу-справа, тень без смещения, радиус 3 DIP, плотность 0.34/0.16, вне пилюли (обрезана по её скруглению, чтобы не затемнять стекло). Без цвета поля основой служит фон темы Windows; высокий контраст — системные цвета. Сборка и визуальная проверка — пользователем.

Отдельное окно с accent-акрилом под пилюлей удалено (GlassBackdrop): Windows 11 рисует такому popup с расширенной рамкой собственную тень со смещением вниз, у однобуквенной подсказки она выглядела полоской (скриншот пользователя). Отключить её можно только вместе с неклиентским рендерингом DWM, то есть вместе с размытием. Стекло теперь рисуется самой пилюлей: заливка в цвет поля с альфой 0xE0/0xE6 (без размытия текст под подсказкой должен скрываться), кромка и равномерная тень 3 DIP прежние. Выключенная «Прозрачность» Windows или высокий контраст — обычный вид.

## 2026-10-14 — Тестовые поля

Пункт трея «Тестовые поля…» запускает тот же exe с --field-tester вторым процессом (без маркера экземпляра, hook и трея): утилита пропускает события фокуса своих окон, поэтому поля должны быть в другом процессе. Окно повторяет макет: тёмное (#383A40), светлое скруглённое с тенью (#FFFFFF), тёмно-синее (#17212B) и пёстрое (неоновый леопард, рисуется кодом без стороннего изображения; у поля нет доминирующего цвета). Поля заполнены фразой «ш дщму фтеркщзшс»; у каждого закреплена подсказка «anthropic», отрисованная настоящим PreviewWindow с точным цветом поля (у пёстрого — 0, запасной вид), пока окно активно. Флажок стекла действует только внутри окна; начальное значение — из настроек. Закреплённые подсказки проверяют оформление, а не распознавание цвета/шрифта — это проверяется набором.

Пропорции по макету (пользователь: «на макете лучше»): отступы подсказки 6/1/6/2 DIP (стекло 7/1/7/2) вместо 4/2/4/3, рамка ровно 1 физический пиксель вместо масштабируемой (при 125% давала размытую обводку). Кромка стекла — вертикальный градиент (ярче сверху), тень BlurEffect радиус 4 DIP.

## 2026-10-14 — Стекло совпадает с эталоном (принято)

Попиксельное сравнение с эталоном в браузере (артефакт «Эталон подсказки TypeAllBack», 125%, Segoe UI) по скриншотам пользователя. Итоговая отрисовка стекла: без рамки; поверхность — DrawingBrush точного пиксельного размера: заливка цвета поля (72%/80%), затем две вставки как CSS inset-тени без размытия («плашка минус плашка, сдвинутая на 1 CSS px»: светлая сверху-слева 0x38/0xE6 белого, тень снизу-справа 0x14 белого/0x0F чёрного), радиус 8 DIP. Позиция и размеры пилюли и окна — в целых физических пикселях (иначе кромки и левый край смещались на полпикселя), SnapsToDevicePixels выключен для стекла. Нижний отступ уменьшен на 1 физический пиксель: строка Segoe UI в WPF на пиксель выше, чем в Chromium. Тень — BlurEffect радиус 5 DIP, 0.36/0.18. На светлых полях холодный оттенок (к #101828: заливка 4.3%, текст 66%). Текст — RenderOptions.ClearTypeHint=Enabled (в прозрачном окне иначе серое сглаживание). Совпали высота/ширина (31/102 px), кромки (112/82 сверху, 76/86 снизу), заливка, текст, ClearType. Пользователь: «все нравится».

## 2026-10-14 — Базовая линия и начертание недоступного шрифта

Discord (лог #61): размер по ширинам применён (14.81 DIP), но подсказка на 2 px ниже текста и светлее (штрих gg sans 3 px против 2 px у Segoe UI). GlyphCalibration: для семейств, которые WPF не может отрисовать, не более 3 замеров на шрифт за всё время (интервал ≥1.5 с), только при ≥4 буквах текущего слова перед кареткой, известном цвете поля и скрытой подсказке: полоса в две высоты строки слева от каретки (SRCCOPY без CAPTUREBLT). Покрытие считается вдоль линии фон→цвет текста; пиксели вдали от неё (красное подчёркивание орфографии, выделение) не считаются текстом. Плотная полоса x-height даёт базовую линию (доля высоты каретки) и x-height; медиана масс сглаженных штрихов в ней — толщину штриха (доля x-height). Хранится в FontScaleMemory (baseline|…, stem|…). Применение: подсказка ставится базовой линией на измеренную (вместо центрирования по каретке); начертание fallback-семейства выбирается по ближайшему отношению штрих/x-height ('l' из GlyphTypeface, +0.4 px на сглаживание — эмпирическая поправка, проверить визуально). Доступные шрифты (Блокнот, ChatGPT) и omnibox не затрагиваются.

Стекло — вид по умолчанию для новых настроек (GlassPreview=true; пункт трея «Стеклянная подсказка» без пометки эксперимента). Уже сохранённый settings.json сохраняет прежний выбор.

## 2026-10-14 — Анимации появления, скрытия и ширины

Пилюля и тень вынесены в общий слой PreviewLayer. Появление (в т.ч. повторное после скрытия): прозрачность 0→1 и размытие слоя 4 DIP→0 за 150 мс (CubicEase out); прерванное скрытие продолжается с текущей прозрачности. Скрытие: 1→0 и 0→4 DIP за 120 мс (ease in), затем Hide(); во время затухания подсказка не кликается, а выборка цвета поля ждёт настоящего скрытия. В покое эффект и анимация прозрачности снимаются (ClearType и дешёвая композиция). Анимация ширины (120 мс) снова и для стекла: поверхность и тень перестраиваются по SizeChanged; окно на время анимации шириной по большей из старой/новой пилюли; Border обрезает текст по своим границам. Без «эффектов анимации» Windows всё мгновенно. Визуальная проверка — пользователем.


Анимации идут с частотой обновления монитора (GetDeviceCaps VREFRESH, 60–360 Гц), а не с фиксированными 60 кадрами; BlurEffect тени переиспользуется.

### Базовая линия по целому пикселю и стекло на почти чёрном поле

Claude desktop (Anthropic Sans → Segoe UI): высота строчных совпадала, но подсказка стояла на 1 px ниже текста — базовая линия текста внутри пилюли была дробной и округлялась вниз. Теперь верхний отступ пилюли сдвигается на долю пикселя так, чтобы базовая линия попадала на целый пиксель (снизу отступ уменьшается на столько же), и позиция окна считается от этой целой базовой линии.

Подбор начертания по толщине штриха (MatchWeight) убран по решению пользователя: подсказка всегда использует сообщённый полем вес. Пробовали подбирать по отрисованным пикселям — Segoe UI Semibold в Display выглядел грубо; режим Ideal/grayscale тоже не оставили. Замер штриха GlyphCalibration по-прежнему сохраняется, но не используется.

Стекло на почти чёрном поле (#202020) было почти невидимым: подъём заливки к белому растёт от 0.11 (яркость поля ≥ 0.22, как в макете #383A40) до 0.20, края светлее пропорционально (блик 0x38→0x58, нижний край 0x14→0x20).

### Рамка по центру каретки; замер шрифта по высоте строки

Обзор пользователя (7 скриншотов): «супер» — omnibox, настройки Google, ChatGPT (замера нет, пилюля по центру каретки); «сомнительно, рамка съехала вверх» — Discord, Claude, Chrome Web Store (замер есть, пилюля ставилась от базовой линии, а у Segoe UI над x-height места больше, чем у веб-шрифта); «хуже всех» — VK (x-height +1 px, пилюля на 6 px выше каретки: VK сообщает Roboto, и применялся замер Roboto из другого поля с другой высотой строки). Изменения: пилюля всегда центрируется по каретке, а при известной базовой линии текст сдвигается внутри неё равными и противоположными Margin (не больше четверти высоты пилюли); ключ замера GlyphCalibration включает высоту каретки в пикселях, старые записи без высоты больше не читаются.
