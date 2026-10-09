using LayoutPreviewPrototype;
using System.Runtime.InteropServices;
using System.Text;
if (args.Contains("--shell-discovery-smoke"))
{
    ShellDiscoverySmokeTests.Run();
    return;
}
if (args.Contains("--native-font-smoke"))
{
    NativeFontSmokeTests.Run();
    return;
}
if (args.Contains("--hook-pump-smoke"))
{
    using var pump = new KeyboardHookPump(_ => { }, false);
    pump.Start();
    if (!pump.IsRunning || pump.ThreadId == Native.GetCurrentThreadId())
        throw new Exception("Keyboard hook is not running on its independent thread");
    pump.Dispose();
    if (pump.IsRunning) throw new Exception("Keyboard hook message pump did not stop");
    Console.WriteLine("PASS: independent Windows hook thread startup and shutdown (no injected input)");
    return;
}
if (args.Contains("--uia-smoke"))
{
    UiaSmokeTests.Run();
    return;
}
if (args.Contains("--editor-probe"))
{
    TextEditor.Start();
    Thread.Sleep(600);
    Console.WriteLine(Task.Run(UiaTextEditor.DescribeFocusedElement).GetAwaiter().GetResult());
    Console.WriteLine(TextEditor.TryRead(out var focused) ? $"Editor=OK UIA={focused.AutomationIdentity.Length > 0} length={focused.Text.Length} selection={focused.SelectionStart},{focused.SelectionEnd} caretHeight={focused.Caret.Bottom-focused.Caret.Top}" : "Editor=UNSUPPORTED");
    TextEditor.Stop();
    return;
}
if (args.Length == 3 && args[0] == "--corpus")
{
    CorpusEvaluation.Run(args[1], args[2]);
    return;
}
if (args.Contains("--probe"))
{
    if (args.Contains("--delay")) Thread.Sleep(8000);
    var fg = Native.GetForegroundWindow(); var thread = Native.GetWindowThreadProcessId(fg, out uint pid);
    var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() }; Native.GetGUIThreadInfo(thread, ref info);
    var cls = new StringBuilder(256); Native.GetClassName(info.hwndFocus, cls, 256);
    Console.WriteLine($"PID={pid} class={cls} flags={info.flags} layout=0x{Native.GetKeyboardLayout(thread).ToInt64():X} caretHeight={info.rcCaret.Bottom-info.rcCaret.Top} caretHandle={info.hwndCaret}");
    Console.WriteLine(NotepadEditor.TryRead(out var snapshot) ? $"Adapter=OK length={snapshot.Text.Length} selection={snapshot.SelectionStart},{snapshot.SelectionEnd} dpi={snapshot.Dpi} editorThread={snapshot.Thread} editorLayout=0x{snapshot.Layout.ToInt64():X}" : "Adapter=UNSUPPORTED");
    if (snapshot != null) Console.WriteLine(NotepadEditor.TryGetCaret(snapshot, out var caret) ? $"Caret=OK {caret.Left},{caret.Top},{caret.Right},{caret.Bottom}" : "Caret=UNAVAILABLE");
    return;
}
int checks = 0;
void Eq<T>(T actual, T expected, string name)
{ checks++; if (!Equals(actual, expected)) throw new Exception($"{name}: expected {expected}, actual {actual}"); }
PhysicalKey Us(char c)
{
    if (char.IsLetter(c)) return new((uint)char.ToUpperInvariant(c), char.IsUpper(c), false);
    uint vk = c switch { ',' => 0xBC, '[' => 0xDB, ' ' => 0x20, '.' => 0xBE, ';' => 0xBA, '/' => 0xBF, '`' => 0xC0, _ => (uint)c };
    return new(vk, false, false);
}
string ConvertWord(string value, KeyboardLayout layout) => new(value.Select(c => { KeyboardConversion.TryConvert(Us(c), layout, out var result); return result; }).ToArray());
var hello = new[] { "П", "Пр", "При", "Прив", "Приве", "Привет" };
for (int i=1; i<=6; i++) Eq(ConvertWord("Ghbdtn"[..i], KeyboardLayout.Russian), hello[i-1], "hello prefix");
var critical = new[] { "з", "за", "заб", "забу", "забух", "забуха", "забухал" };
for (int i=1; i<=7; i++) Eq(ConvertWord("pf,e[fk"[..i], KeyboardLayout.Russian), critical[i-1], "punctuation prefix");
Eq(ConvertWord("`", KeyboardLayout.Russian), "ё", "yo");
KeyboardConversion.TryConvert(new(0x47,false,true), KeyboardLayout.Russian, out var caps); Eq(caps,'П',"caps");
KeyboardConversion.TryConvert(new(0x47,true,true), KeyboardLayout.Russian, out caps); Eq(caps,'п',"shift caps");
KeyboardConversion.TryConvert(new(0x33,true,false), KeyboardLayout.Russian, out var punctuation); Eq(punctuation,'№',"shift digit RU");
KeyboardConversion.TryConvert(new(0x33,true,false), KeyboardLayout.English, out punctuation); Eq(punctuation,'#',"shift digit US");
foreach (char c in "QWERTYUIOPASDFGHJKLZXCVBNM")
    foreach (bool shift in new[] { false, true }) foreach (bool cap in new[] { false, true })
    { KeyboardConversion.TryConvert(new((uint)c,shift,cap),KeyboardLayout.English,out var en); Eq(en, (shift ^ cap) ? c : char.ToLowerInvariant(c),"EN case"); }
string document = "Старый правильный текст. "; int initial = document.Length;
var session = new InputSession(document, initial, KeyboardLayout.English);
void Type(string keys, KeyboardLayout layout)
{
    foreach(char c in keys)
    {
        Eq(session.Matches(document,session.End,session.End),true,"pre append context");
        var key=Us(c); int end=session.End; session.Append(key,layout,document);
        KeyboardConversion.TryConvert(key,layout,out var source); document=document.Insert(end,source.ToString());
Eq(session.Matches(document,session.End,session.End),true,"post append context");
    }
}
Type("ghbdtn ",KeyboardLayout.English);
Eq(session.MatchesDocument(document),true,"session document matches while selection can change");
Eq(session.Matches(document,0,document.Length),false,"selection cannot be accepted");
Eq(session.MatchesDocument(document),true,"Ctrl+A selection preserves session document");
Eq(session.Preview,"привет","space retains preview"); Eq(session.Replacement,"привет ","sentence replacement");
Type("vbh",KeyboardLayout.Russian);
Eq(session.Replacement,"ghbdtn vbh","only the current Russian section is converted"); Eq(session.Segments.Count,2,"layout segments");
Eq(session.Preview,"","previous section no longer contributes a hint");
Type(" pf,e[fk",KeyboardLayout.English);
Eq(session.Preview,"забухал","latest word"); Eq(session.Replacement,"ghbdtn мир забухал","previous layout sections remain unchanged");
Eq(session.Start,initial,"old text outside range");
Eq(session.Matches(document,session.End-1,session.End-1),false,"moved caret invalid");
Eq(session.Matches(document,session.Start,session.End),false,"selection invalid");
Eq(session.Matches("X"+document[1..],session.End,session.End),false,"external edit invalid");
int before=session.End; Eq(session.Backspace(document),true,"backspace"); document=document.Remove(before-1,1);
Eq(session.Preview,"забуха","backspace preview"); Eq(session.Matches(document,session.End,session.End),true,"backspace context");
var empty = new InputSession("old",3,KeyboardLayout.English); Eq(empty.Backspace("old"),false,"cannot delete pre session");
Eq(Marshal.SizeOf<Native.INPUT>(),40,"x64 INPUT ABI");
Eq(NotepadEditor.NormalizeText("old\r\nline\r\n", "RichEditD2DPT"), "old\rline\r", "RichEdit positions");
Eq(NotepadEditor.NormalizeText("old\r\nline", "Edit"), "old\r\nline", "legacy Edit positions");
var multiline = new InputSession("old\rline\r",9,KeyboardLayout.English);
multiline.Append(Us('p'),KeyboardLayout.English,"old\rline\r");
Eq(multiline.Matches("old\rline\rp",10,10),true,"session after multiple paragraphs");
Eq(multiline.Replacement,"з","multiline conversion");
var correctlyTypedEnglish = new InputSession("",0,KeyboardLayout.English);
string englishDocument = "";
foreach (char c in "hello") { int end=correctlyTypedEnglish.End; var key=Us(c); correctlyTypedEnglish.Append(key,KeyboardLayout.English,englishDocument); KeyboardConversion.TryConvert(key,KeyboardLayout.English,out var typed); englishDocument=englishDocument.Insert(end,typed.ToString()); }
Eq(correctlyTypedEnglish.Preview,"","correct English stays quiet");
var wrongEnglishLayout = new InputSession("",0,KeyboardLayout.English);
string wrongEnglishDocument = "";
foreach (char c in "ghbdtn") { int end=wrongEnglishLayout.End; var key=Us(c); wrongEnglishLayout.Append(key,KeyboardLayout.English,wrongEnglishDocument); KeyboardConversion.TryConvert(key,KeyboardLayout.English,out var typed); wrongEnglishDocument=wrongEnglishDocument.Insert(end,typed.ToString()); }
Eq(wrongEnglishLayout.Preview,"привет","obvious Russian alternative is suggested");
var correctlyTypedRussian = new InputSession("",0,KeyboardLayout.Russian);
string russianDocument = "";
foreach (char c in "ghbdtn") { int end=correctlyTypedRussian.End; var key=Us(c); correctlyTypedRussian.Append(key,KeyboardLayout.Russian,russianDocument); KeyboardConversion.TryConvert(key,KeyboardLayout.Russian,out var typed); russianDocument=russianDocument.Insert(end,typed.ToString()); }
Eq(correctlyTypedRussian.Preview,"","correct Russian stays quiet");
var wrongRussianLayout = new InputSession("",0,KeyboardLayout.Russian);
string wrongRussianDocument = "";
foreach (char c in "hello") { int end=wrongRussianLayout.End; var key=Us(c); wrongRussianLayout.Append(key,KeyboardLayout.Russian,wrongRussianDocument); KeyboardConversion.TryConvert(key,KeyboardLayout.Russian,out var typed); wrongRussianDocument=wrongRussianDocument.Insert(end,typed.ToString()); }
Eq(wrongRussianDocument,"руддщ","Russian layout produces the expected wrong-layout text");
Eq(wrongRussianLayout.Preview,"hello","obvious English alternative from Russian is suggested");
Eq(LanguagePlausibility.IsClearlyMorePlausible("ghbdtn rfr ltkf","привет как дела",KeyboardLayout.English),true,"whole session supports Russian alternative");
Eq(LanguagePlausibility.IsClearlyMorePlausible("the quick brown fox","еур йгшсл игщты ашч",KeyboardLayout.English),false,"whole correct English session stays quiet");
var guard = new AcceptedInputGuard();
string accepted = "Старое. Привет";
guard.Arm(accepted,accepted.Length,KeyboardLayout.Russian);
var space = new PhysicalKey(0x20,false,false);
Eq(guard.ShouldSuppress(accepted,accepted.Length,accepted.Length,KeyboardLayout.Russian,space,false),true,"accepted session suppresses following space");
accepted += " ";
var ruM = new PhysicalKey(0x42,false,false);
Eq(guard.ShouldSuppress(accepted,accepted.Length,accepted.Length,KeyboardLayout.Russian,ruM,false),true,"accepted session suppresses next word first letter");
KeyboardConversion.TryConvert(ruM,KeyboardLayout.Russian,out var ruMCharacter);
accepted += ruMCharacter;
var ruI = new PhysicalKey(0x44,false,false);
Eq(guard.ShouldSuppress(accepted,accepted.Length,accepted.Length,KeyboardLayout.Russian,ruI,false),true,"accepted session suppresses next word continuation");
KeyboardConversion.TryConvert(ruI,KeyboardLayout.Russian,out var ruICharacter);
accepted += ruICharacter;
Eq(guard.ShouldSuppress(accepted,accepted.Length-1,accepted.Length-1,KeyboardLayout.Russian,ruI,false),true,"mouse caret move keeps accepted guard");
Eq(guard.IsArmed,true,"mouse caret move preserves guard");
guard.Arm(accepted,accepted.Length,KeyboardLayout.Russian);
Eq(guard.ShouldSuppress(accepted,accepted.Length,accepted.Length,KeyboardLayout.English,Us('g'),false),false,"manual layout change rearms previews");
guard.Arm(accepted,accepted.Length,KeyboardLayout.Russian);
Eq(guard.ShouldSuppress(accepted,accepted.Length,accepted.Length,KeyboardLayout.Russian,space,true),false,"explicit new line rearms previews");
guard.Arm(accepted,accepted.Length,KeyboardLayout.Russian);
Eq(guard.ShouldSuppress(accepted,accepted.Length,accepted.Length,KeyboardLayout.Russian,new PhysicalKey(0x08,false,false),false),true,"backspace keeps accepted guard armed");
accepted=accepted.Remove(accepted.Length-1,1);
Eq(guard.ShouldSuppress(accepted,accepted.Length,accepted.Length,KeyboardLayout.Russian,ruM,false),true,"typing after Backspace stays suppressed");
KeyboardConversion.TryConvert(ruM,KeyboardLayout.Russian,out ruMCharacter);
accepted=accepted.Insert(accepted.Length,ruMCharacter.ToString());
guard.Arm(accepted,accepted.Length,KeyboardLayout.Russian);
Eq(guard.ShouldSuppress(accepted,0,accepted.Length,KeyboardLayout.Russian,new PhysicalKey(0x08,false,false),false),true,"Ctrl+A then Backspace keeps accepted guard");
accepted="";
Eq(guard.ShouldSuppress(accepted,0,0,KeyboardLayout.Russian,ruM,false),true,"Russian typing after deleting selection stays suppressed");
guard.Arm(accepted,accepted.Length,KeyboardLayout.Russian);
Eq(guard.ShouldSuppress(accepted+"!",accepted.Length+1,accepted.Length+1,KeyboardLayout.Russian,ruM,false),false,"external text edit rearms previews");
var burst = new InputSession("old",3,KeyboardLayout.English);
var gKey = new QueuedInput(0x47,true,false,KeyboardLayout.English);
burst.Append(new PhysicalKey(gKey.VirtualKey,gKey.Shift,gKey.CapsLock),gKey.Layout,"old");
var burstKeys = new[]
{
    new QueuedInput(0x48,false,false,KeyboardLayout.English),
    new QueuedInput(0x42,false,false,KeyboardLayout.English),
    new QueuedInput(0x44,false,false,KeyboardLayout.English),
    new QueuedInput(0x08,false,false,KeyboardLayout.English)
};
burst.ReplayPending(burstKeys);
Eq(burst.Source,"Ghb","queued typing and Backspace stay in one session");
Eq(burst.Reconcile("oldGhb",6,6),true,"rapid Backspace reconciles against the editor");
Eq(burst.Preview,"При","rapid Backspace keeps the live preview");
// A delayed first UIA snapshot must keep the pre-key boundary, including a
// nonempty document. Failed partial snapshots must not consume early keys.
string startupBaseline = "Старый текст. ";
var startup = new InputSession(startupBaseline, startupBaseline.Length, KeyboardLayout.English);
var startupKeys = "ghbdtn".Select(c => new QueuedInput(Us(c).VirtualKey, false, false, KeyboardLayout.English)).ToArray();
var earlyStartup = startup.Copy();
earlyStartup.ReplayPending(startupKeys);
Eq(earlyStartup.Reconcile(startupBaseline + "ghb", startupBaseline.Length + 3, startupBaseline.Length + 3), false,
    "partial first snapshot cannot advance the session boundary");
Eq(startup.Source, "", "failed snapshot leaves the original startup untouched");
var completeStartup = startup.Copy();
completeStartup.ReplayPending(startupKeys);
Eq(completeStartup.Reconcile(startupBaseline + "ghbdtn", startupBaseline.Length + 6, startupBaseline.Length + 6), true,
    "delayed full first word matches original boundary");
Eq(completeStartup.Start, startupBaseline.Length, "preexisting text remains outside startup session");
Eq(completeStartup.Source, "ghbdtn", "first three characters are retained in startup");
Eq(completeStartup.Replacement, "привет", "replacement covers the complete first word");
Eq(startupBaseline + completeStartup.Replacement, "Старый текст. привет", "accept preserves prior text and converts first letter");
Eq(UiaTextEditor.TryCanonicalDocument("Placeholder text\n", "", 16, 16, out var canonical, out var canonicalStart, out var canonicalEnd), true, "empty UIA value excludes placeholder");
Eq(canonical, "", "placeholder is not entered document text");
Eq(canonicalStart, 0, "placeholder caret maps to logical zero");
Eq(canonicalEnd, 0, "placeholder selection end maps to logical zero");
Eq(UiaTextEditor.TryCanonicalDocument("ghbdtn\n", "ghbdtn", 6, 6, out canonical, out canonicalStart, out canonicalEnd), true, "UIA implicit paragraph mark is excluded");
Eq(canonical, "ghbdtn", "complete first word remains intact");
Eq(UiaTextEditor.TryCanonicalDocument("line\n\n", "line\n", 5, 5, out canonical, out canonicalStart, out canonicalEnd), true, "real trailing newline remains distinct from provider suffix");
Eq(canonical, "line\n", "user-entered newline is preserved");
Eq(UiaTextEditor.TryCanonicalDocument("old", "new", 3, 3, out _, out _, out _), false, "inconsistent UIA value and text fail closed");
Eq(UiaTextEditor.TryCanonicalDocument("ghbdtn\n", "ghbdtn", 7, 7, out _, out _, out _), false, "caret outside actual value is rejected");
Eq(UiaTextEditor.TryCanonicalDocument("\n", "\n", 0, 0, out canonical, out canonicalStart, out canonicalEnd), true, "empty provider paragraph mark is supported");
Eq(canonical, "", "empty provider paragraph mark is not baseline text");
Eq(UiaTextEditor.TryCanonicalDocument("g\n", "g\n", 1, 1, out canonical, out _, out _), true, "paragraph mark in both UIA patterns is supported");
Eq(canonical, "g", "first key remains the entire logical document");
Eq(UiaTextEditor.TryCanonicalDocument("g\n", "g\n", 2, 2, out canonical, out _, out _), true, "caret after a real line break remains valid");
Eq(canonical, "g\n", "real line break after typed text is preserved");
var placeholderStartup = new InputSession("Provider placeholder", 20, KeyboardLayout.English);
placeholderStartup.Append(Us('g'), KeyboardLayout.English, "Provider placeholder");
Eq(placeholderStartup.TryRebaseUnconfirmedStartup("g", 1, 1, out var firstKeyStartup), true,
    "first physical key replaces provider placeholder without losing the key");
Eq(firstKeyStartup.Start, 0, "first physical key retains offset zero");
Eq(firstKeyStartup.Source, "g", "first key remains in session source");
foreach (char c in "hbdtn")
{
    string current = firstKeyStartup.Source;
    firstKeyStartup.Append(Us(c), KeyboardLayout.English, current);
}
Eq(firstKeyStartup.Matches("ghbdtn", 6, 6), true, "all letters reconcile after placeholder replacement");
Eq(firstKeyStartup.Replacement, "привет", "accept includes first letter after placeholder replacement");
Eq(placeholderStartup.TryRebaseUnconfirmedStartup("Provider placeholderg", 21, 21, out _), false,
    "existing text is never adopted as freshly typed input");
Eq(placeholderStartup.TryRebaseUnconfirmedStartup("g", 0, 0, out _), false,
    "startup rebasing refuses a moved caret");
var middleStartup = new InputSession("old", 1, KeyboardLayout.English);
middleStartup.Append(Us('g'), KeyboardLayout.English, "old");
Eq(middleStartup.TryRebaseUnconfirmedStartup("g", 1, 1, out _), false,
    "startup rebasing refuses a session begun inside preexisting text");
var pendingPlaceholderStartup = placeholderStartup.Copy();
pendingPlaceholderStartup.ReplayPending(startupKeys.Skip(1).ToArray());
Eq(pendingPlaceholderStartup.TryRebaseUnconfirmedStartup("ghbdtn", 6, 6, out var rebasedPending), true,
    "first delivery can confirm an entire queued word");
Eq(rebasedPending.Replacement, "привет", "queued first delivery preserves every physical key");
string heldBackspaceBaseline = "Old text stays. ";
var heldBackspaceSession = new InputSession(heldBackspaceBaseline, heldBackspaceBaseline.Length, KeyboardLayout.English);
var repeatedKeys = Enumerable.Repeat(new QueuedInput(0x47, false, false, KeyboardLayout.English), 100).ToArray();
heldBackspaceSession.ReplayPending(repeatedKeys);
Eq(heldBackspaceSession.Reconcile(heldBackspaceBaseline + new string('g', 100), heldBackspaceBaseline.Length + 100, heldBackspaceBaseline.Length + 100), true, "long typed session is confirmed before autorepeat");
var deletionBatch = heldBackspaceSession.Copy();
deletionBatch.ReplayPending(Enumerable.Repeat(new QueuedInput(0x08, false, false, KeyboardLayout.English), 60).ToArray());
Eq(deletionBatch.Reconcile(heldBackspaceBaseline + new string('g', 40), heldBackspaceBaseline.Length + 40, heldBackspaceBaseline.Length + 40), true, "held Backspace reconciles an entire repeat batch");
Eq(deletionBatch.Source.Length, 40, "all repeated native deletions remain accounted for");
Eq(deletionBatch.Start, heldBackspaceBaseline.Length, "held Backspace preserves session boundary");
deletionBatch.ReplayPending(Enumerable.Repeat(new QueuedInput(0x08, false, false, KeyboardLayout.English), 40).ToArray());
Eq(deletionBatch.Reconcile(heldBackspaceBaseline, heldBackspaceBaseline.Length, heldBackspaceBaseline.Length), true, "deleting all typed characters preserves the earlier document");
Eq(deletionBatch.Source, "", "empty session remains valid after deletion batch");
var retypedRussian = InputSession.FromSelectionReplacement("руддщ", 0, 5, Us('h'), KeyboardLayout.Russian);
Eq(retypedRussian.Matches("р", 1, 1), true, "first RU key replacing Ctrl+A selection is captured");
Eq(retypedRussian.Replacement, "h", "first RU key retains its English alternative");
string retypedDocument = "р";
foreach (char c in "ello")
{
    retypedRussian.Append(Us(c), KeyboardLayout.Russian, retypedDocument);
    KeyboardConversion.TryConvert(Us(c), KeyboardLayout.Russian, out var typedCharacter);
    retypedDocument += typedCharacter;
}
Eq(retypedDocument, "руддщ", "retyping selected Russian-layout text produces all five letters");
Eq(retypedRussian.Matches(retypedDocument, 5, 5), true, "retyped RU word matches the entire new document");
Eq(retypedRussian.Preview, "hello", "Ctrl+A retyping keeps first preview letter");
Eq(retypedRussian.Replacement, "hello", "accept converts every retyped letter");
var retypedEnglish = InputSession.FromSelectionReplacement("ghbdtn", 0, 6, Us('g'), KeyboardLayout.English);
retypedEnglish.ReplayPending(startupKeys.Skip(1).ToArray());
Eq(retypedEnglish.Reconcile("ghbdtn", 6, 6), true, "Ctrl+A EN replacement supports rapid queued continuation");
Eq(retypedEnglish.Replacement, "привет", "EN selected replacement retains first converted letter");
var partialReplacement = InputSession.FromSelectionReplacement("OLD руддщ TAIL", 4, 9, Us('h'), KeyboardLayout.Russian);
Eq(partialReplacement.Matches("OLD р TAIL", 5, 5), true, "selection replacement preserves unselected prefix and suffix");
Eq(partialReplacement.Start, 4, "selection replacement range begins at actual first inserted character");
Eq(partialReplacement.Matches("OLD руддщр TAIL", 10, 10), false, "failed selection prediction cannot validate appended text");
Eq(UiaTextEditor.FontPointsToDip(12), 16.0, "UIA point font size converts to WPF DIP independently of caret height");
Eq(UiaTextEditor.FontPointsToDip(18), 24.0, "larger text uses its declared font size");
Eq(UiaTextEditor.FontPointsToDip(double.NaN), 0.0, "invalid UIA font size requests stable fallback");
Eq(UiaTextEditor.FontPointsToDip(double.PositiveInfinity), 0.0, "infinite font size cannot enlarge preview");
Eq(UiaTextEditor.FontPointsToDip(-1), 0.0, "negative font size cannot enlarge preview");
var nativeForGeometry = new EditorSnapshot((IntPtr)1, (IntPtr)2, 3, (IntPtr)0x04090409,
    "oldg", 4, 4, default, 96);
var cachedGeometry = nativeForGeometry with
{
    Caret = new Native.RECT { Left = 120, Right = 121, Top = 50, Bottom = 70 },
    CapturedAt = 100, FontSizeDip = 16
};
Eq(UiaTextEditor.TryMergeNativeGeometry(nativeForGeometry, cachedGeometry, 110, out var enrichedGeometry), true,
    "matching background geometry enriches native text without COM calls");
Eq(enrichedGeometry.Caret.Left, 120, "native snapshot receives verified background coordinates");
Eq(enrichedGeometry.FontSizeDip, 16.0, "native snapshot receives declared background font size");
Eq(UiaTextEditor.TryMergeNativeGeometry(nativeForGeometry with { Text = "oldh" }, cachedGeometry, 110, out _), false,
    "background geometry from different text cannot be reused");
Eq(UiaTextEditor.TryMergeNativeGeometry(nativeForGeometry with { SelectionStart = 3, SelectionEnd = 3 }, cachedGeometry, 110, out _), false,
    "background geometry from a different caret cannot be reused");
Eq(UiaTextEditor.TryMergeNativeGeometry(nativeForGeometry with { Dpi = 144 }, cachedGeometry, 110, out _), false,
    "background coordinates cannot cross a DPI change");
Eq(UiaTextEditor.TryMergeNativeGeometry(nativeForGeometry, cachedGeometry, 301, out _), false,
    "stale native geometry is rejected");
Eq(UiaTextEditor.TryMergeNativeGeometry(nativeForGeometry with { Editor = (IntPtr)9 }, cachedGeometry, 110, out _), false,
    "another editor cannot supply native geometry");
Eq(Marshal.SizeOf<Native.MSG>(), 48, "x64 Windows message-pump ABI");
var acceptTicket = new KeyboardAcceptContext(nativeForGeometry, 7);
Eq(KeyboardHookPump.CanAccept(acceptTicket, nativeForGeometry, 7), true, "confirmed visible preview can reserve Right on independent hook thread");
Eq(KeyboardHookPump.CanAccept(null, nativeForGeometry, 7), false, "Right stays native without a confirmed preview");
Eq(KeyboardHookPump.CanAccept(acceptTicket, nativeForGeometry, 8), false, "intervening physical key invalidates acceptance");
Eq(KeyboardHookPump.CanAccept(acceptTicket, nativeForGeometry with { Text = "oldh" }, 7), false, "changed document cannot consume Right");
Eq(KeyboardHookPump.CanAccept(acceptTicket, nativeForGeometry with { SelectionStart = 3, SelectionEnd = 3 }, 7), false, "moved caret cannot consume Right");
Eq(KeyboardHookPump.CanAccept(acceptTicket, nativeForGeometry with { Layout = (IntPtr)0x04190419 }, 7), false, "changed layout cannot consume Right");
Eq(KeyboardHookPump.CanAccept(acceptTicket, nativeForGeometry with { Editor = (IntPtr)9 }, 7), false, "different editor cannot consume Right");
foreach (string supportedClass in new[] { "Edit", "RICHEDIT", "RichEdit20A", "RichEdit20W", "RICHEDIT50W", "RichEditD2DPT", "WindowsForms10.EDIT.app.0.test", "WindowsForms10.RichEdit20W.app.0.test", "WindowsForms10.RichEdit50W.app.0.test" })
    Eq(NotepadEditor.IsSupportedClass(supportedClass), true, "native editable class " + supportedClass);
foreach (string unsupportedClass in new[] { "Button", "MyEditCanvas", "Chrome_RenderWidgetHostHWND", "WindowsForms10.STATIC.app.0.test" })
    Eq(NotepadEditor.IsSupportedClass(unsupportedClass), false, "unknown native class is not treated as Edit " + unsupportedClass);
Eq(NotepadEditor.NormalizeText("a\r\nb", "WindowsForms10.EDIT.app.0.test"), "a\r\nb", "WinForms Edit uses CRLF character offsets");
Eq(NotepadEditor.NormalizeText("a\r\nb", "RichEdit20W"), "a\rb", "legacy RichEdit uses paragraph offsets");
Eq(UiaTextEditor.IsWritableText(false, null), true, "writable TextPattern does not require ValuePattern or Edit role");
Eq(UiaTextEditor.IsWritableText(null, false), true, "ValuePattern can establish writability when text attribute is unsupported");
Eq(UiaTextEditor.IsWritableText(null, null), false, "unknown writability is rejected");
Eq(UiaTextEditor.IsWritableText(true, false), false, "readonly TextPattern cannot be bypassed by ValuePattern");
Eq(UiaTextEditor.IsWritableText(false, true), false, "readonly ValuePattern cannot be bypassed by TextPattern");
var fontGeometry = cachedGeometry with { FontFamilyName = "Segoe UI" };
Eq(UiaTextEditor.TryMergeNativeGeometry(nativeForGeometry, fontGeometry, 110, out var nativeFontGeometry), true, "matching native cache includes editor font family");
Eq(nativeFontGeometry.FontFamilyName, "Segoe UI", "font family is preserved across backends");
Eq(UiaTextEditor.BelongsToFocusedWindow(10, 10, 20), true, "focused child process may provide UIA text");
Eq(UiaTextEditor.BelongsToFocusedWindow(20, 10, 20), true, "foreground owner may provide hosted UIA text");
Eq(UiaTextEditor.BelongsToFocusedWindow(30, 10, 20), false, "stale UIA focus from another application is excluded");
Eq(UiaTextEditor.BelongsToFocusedWindow(0, 0, 0), false, "missing process identity cannot validate a field");
var omniboxSession = new InputSession("", 0, KeyboardLayout.English);
omniboxSession.Append(Us('g'), KeyboardLayout.English, "");
Eq(omniboxSession.MatchesWithExternalSelection("google.test", 1, 11), true, "omnibox generated selection is separate from typed prefix");
Eq(omniboxSession.TryAppendOverExternalSelection("google.test", 1, 11, Us('h'), KeyboardLayout.English), true, "typing over autocomplete retains first key");
Eq(omniboxSession.Source, "gh", "omnibox first two physical keys remain in one segment");
string omniboxDocument = "gh";
foreach (char c in "bdtn") { omniboxSession.Append(Us(c), KeyboardLayout.English, omniboxDocument); omniboxDocument += c; }
Eq(omniboxSession.Preview, "привет", "omnibox suggestion retains the first converted letter");
var protectedSuffix = new InputSession("OLD  REAL", 4, KeyboardLayout.English);
protectedSuffix.Append(Us('g'), KeyboardLayout.English, "OLD  REAL");
Eq(protectedSuffix.MatchesWithExternalSelection("OLD g REAL", 5, 10), false, "existing suffix cannot be mistaken for generated completion");
Eq(protectedSuffix.TryAppendOverExternalSelection("OLD g REAL", 5, 10, Us('h'), KeyboardLayout.English), false, "unconfirmed selected suffix is not adopted into the old segment");
Eq(PreviewMetrics.ResolveFontSize(24, 20, 96, 15, true), 16.0, "inflated omnibox font is limited by visible line height");
Eq(PreviewMetrics.ResolveFontSize(24, 30, 144, 15, true), 16.0, "font limit uses DIP at 150 percent DPI");
Eq(PreviewMetrics.ResolveFontSize(24, 40, 192, 15, true), 16.0, "font limit uses DIP at 200 percent DPI");
Eq(PreviewMetrics.ResolveFontSize(12, 20, 96, 15, true), 12.0, "valid smaller editor font is not enlarged");
Eq(PreviewMetrics.ResolveFontSize(21, 80, 96, 16, false), 16.0, "temporary paragraph-sized rectangle cannot enlarge an established hint");
Eq(PreviewMetrics.ResolveFontSize(double.NaN, 100, 96, 16, false), 16.0, "invalid metadata and large geometry retain a stable font");
Eq(PreviewMetrics.ResolveFontSize(24, 20, 0, 15, true), 16.0, "missing DPI falls back to 96 without division by zero");
Eq(PreviewMetrics.ResolveFontSize(0, 20, 96, 14, false), 14.0, "missing font metadata does not grow the existing label");
Eq(Marshal.SizeOf<Native.LOGFONT>(), 92, "Unicode LOGFONT ABI for actual Win32 font parsing");
Eq(PreviewMetrics.ResolveFontSize(14, 16, 96, 15, true, true), 14.0, "reliable glyph formatting uses actual font size rather than a line-height estimate");
Eq(PreviewMetrics.OverlayScaleRatio(144, 1.5), 1.0, "matching source and WPF DPI preserve logical font size");
Eq(PreviewMetrics.OverlayScaleRatio(96, 1.5) * 15 * 1.5, 15.0, "overlay at 150 percent does not enlarge a 96-DPI field font");
Eq(PreviewMetrics.OverlayScaleRatio(192, 1.0) * 15, 30.0, "overlay at 100 percent reproduces 200-percent source physical size");
Eq(PreviewMetrics.OverlayScaleRatio(0, double.NaN), 1.0, "unknown DPI scales have a finite fallback");
var styledGeometry = cachedGeometry with { FontWeightValue = 700, FontItalic = true, FontSource = "Win32 HFONT" };
Eq(UiaTextEditor.TryMergeNativeGeometry(nativeForGeometry, styledGeometry, 110, out var styledNative), true, "native font style is tied to the verified text and caret");
Eq(styledNative.FontWeightValue, 700, "bold font weight survives geometry enrichment");
Eq(styledNative.FontItalic, true, "italic font style survives geometry enrichment");
Eq(styledNative.FontSource, "Win32 HFONT", "font provenance remains explicit");
Eq(PreviewMetrics.ResolveFontSize(0, 22, 120, 14.666666666666666, false), 14.666666666666666,
    "missing metadata cannot shrink the confirmed Notepad font between keystrokes");
Eq(PreviewMetrics.ResolveFontSize(0, 55, 120, 37.333333333333336, false), 37.333333333333336,
    "large editor font survives intermittent missing metadata");
var newlyTyped = nativeForGeometry with { Text = "oldgh", SelectionStart = 5, SelectionEnd = 5 };
Eq(UiaTextEditor.TryMergeNativeFormatting(newlyTyped, styledGeometry, 110, out var stableFormatting), true,
    "font metadata may survive typing while geometry still requires an exact match");
Eq(stableFormatting.Text, "oldgh", "format enrichment never replaces freshly read text");
Eq(stableFormatting.SelectionStart, 5, "format enrichment never replaces freshly read selection");
Eq(stableFormatting.Caret.Bottom, newlyTyped.Caret.Bottom, "format enrichment never reuses stale caret coordinates");
Eq(stableFormatting.FontWeightValue, 700, "temporary geometry mismatch preserves font style");
Eq(UiaTextEditor.TryMergeNativeFormatting(newlyTyped with { Editor = (IntPtr)9 }, styledGeometry, 110, out _), false,
    "font cache cannot cross editors");
Eq(UiaTextEditor.TryMergeNativeFormatting(newlyTyped with { Dpi = 144 }, styledGeometry, 110, out _), false,
    "font cache cannot cross a DPI change");
Eq(UiaTextEditor.TryMergeNativeFormatting(newlyTyped, styledGeometry, 301, out _), false,
    "format enrichment rejects stale snapshots");
Eq(EditorPolling.Delay(1000, 950, true, 2), 35, "late typed text receives an active refresh");
Eq(EditorPolling.Delay(1000, 950, true, 70), 80, "slow providers cannot receive the full active polling rate");
Eq(EditorPolling.Delay(1000, 0, true, 2), 500, "idle supported fields do not poll every frame");
Eq(EditorPolling.Delay(1000, 0, false, 2), 1000, "idle unsupported fields back off");
Eq(EditorPolling.Delay(1000, 950, false, 2), 35, "first input can discover a field that appeared late");
Eq(EditorPolling.MaximumSnapshotAgeMs > EditorPolling.Delay(1000, 0, true, 2), true,
    "idle snapshots stay readable until the next normal refresh");
var discoveryBudget = new ShellDiscoveryBudget();
Eq(discoveryBudget.TryBegin("shell-focus-1", 1000), true, "shell discovery is allowed on focus");
Eq(discoveryBudget.TryBegin("shell-focus-1", 1100), false, "each key cannot restart shell traversal");
Eq(discoveryBudget.TryBegin("shell-focus-1", 1300), true, "one retry allows a late-created search field");
Eq(discoveryBudget.TryBegin("shell-focus-1", 9000), false, "failed discovery never becomes continuous tree polling");
Eq(discoveryBudget.TryBegin("shell-focus-2", 9000), true, "another focus context receives a fresh discovery budget");
Eq(ShellSearchResolver.IsSearchProcess("SearchHost"), true, "Windows search has a targeted adapter");
Eq(ShellSearchResolver.IsSearchProcess("StartMenuExperienceHost"), true, "Start host has a targeted adapter");
Eq(ShellSearchResolver.IsSearchProcess("chrome"), false, "ordinary applications cannot trigger shell subtree traversal");
Eq(NotepadEditor.TryReplace(nativeForGeometry, new InputSession("oldg", 4, KeyboardLayout.English),
    (IntPtr)0x04190419, () => false), false, "cancelled native replacement performs no document access or selection change");
Eq(UiaTextEditor.TryResolveOffsets("старый 👩‍💻 gh\n", "", "\n", true, out var suffixStart, out var suffixEnd), true,
    "suffix-based caret offsets preserve emoji UTF-16 and an implicit paragraph mark");
Eq(suffixStart, "старый 👩‍💻 gh".Length, "caret suffix read does not confuse graphemes with UTF-16 offsets");
Eq(suffixEnd, suffixStart, "collapsed caret stays collapsed");
Eq(UiaTextEditor.TryResolveOffsets("OLD gh END", "gh", "OLD ", false, out var prefixStart, out var prefixEnd), true,
    "prefix-based selection offsets are verified against the document");
Eq(prefixStart, 4, "selection starts after protected prefix");
Eq(prefixEnd, 6, "selection length determines its end without rereading the whole prefix");
Eq(UiaTextEditor.TryResolveOffsets("OLD gh END", "gh", "BAD ", false, out _, out _), false,
    "same-length changed prefix cannot yield a trusted selection");
Eq(UiaTextEditor.TryResolveOffsets("OLD gh END", "gh", " BAD", true, out _, out _), false,
    "same-length changed suffix cannot yield a trusted selection");
Eq(UiaTextEditor.TryResolveOffsets("short", "gh", "oversized boundary", true, out _, out _), false,
    "invalid provider range lengths fail closed");
// Diagnostics retain only session material in a bounded memory buffer.
var diagnostics = new SessionDiagnostics();
Eq(SessionDiagnostics.CountWords("  ghbdtn   vbh "), 2, "diagnostic counter ignores repeated whitespace");
Eq(SessionDiagnostics.CountWords(""), 0, "empty session has no diagnostic words");
diagnostics.Start("first input", 4);
Eq(diagnostics.SessionId, 1L, "first session has its own identity");
Eq(diagnostics.Active, true, "diagnostic lifecycle starts explicitly");
var finalFrame = new SessionDiagnosticFrame(1, "confirmed", "fixture", "EN → RU", 4, 10,
    1, "ghbdtn", "ghbdtn", "привет", 0, true, 6, 6);
diagnostics.End("accepted", finalFrame);
Eq(diagnostics.Active, false, "diagnostic lifecycle ends explicitly");
Eq(diagnostics.LastSession, finalFrame, "ending retains the complete last session for inspection");
Eq(diagnostics.LastEndReason.Contains("accepted"), true, "end reason remains visible");
int eventsAfterEnd = diagnostics.Events.Count;
diagnostics.End("later idle poll", null);
Eq(diagnostics.Events.Count, eventsAfterEnd, "idle resets cannot overwrite the actual end");
Eq(diagnostics.LastSession, finalFrame, "idle reset cannot erase the last session");
diagnostics.Start("next input", 10);
Eq(diagnostics.SessionId, 2L, "a subsequent session is distinguishable from the previous one");
for (int i = 0; i < 500; i++) diagnostics.Event("check", i.ToString());
Eq(diagnostics.Events.Count, SessionDiagnostics.MaximumEvents, "diagnostic event retention is bounded");
Eq(diagnostics.Events.First().Detail, "300", "oldest diagnostic events are removed first");
diagnostics.ClearHistory();
Eq(diagnostics.Events.Count, 0, "clear removes diagnostic history");
Eq(diagnostics.LastSession, null, "clear removes retained session text");
Eq(diagnostics.Active, true, "clearing history does not end the live session");
var diagnosticCandidate = heldBackspaceSession.Copy();
string originalDiagnosticSource = heldBackspaceSession.Source;
diagnosticCandidate.ReplayPending(new[] { new QueuedInput(0x08, false, false, KeyboardLayout.English) });
Eq(heldBackspaceSession.Source, originalDiagnosticSource, "pending diagnostic projection never mutates the live session");
Eq(diagnosticCandidate.Source.Length, originalDiagnosticSource.Length - 1, "pending deletion is shown in expected text");
var acceptDiagnosticListener = new AcceptDiagnosticListener();
acceptDiagnosticListener.Begin(7);
acceptDiagnosticListener.WriteLine("unrelated diagnostic text");
Eq(acceptDiagnosticListener.Drain().Count(), 0, "backend listener ignores unrelated diagnostics");
acceptDiagnosticListener.WriteLine("Accept check: verified range");
acceptDiagnosticListener.Begin(8);
var delayedDiagnostic = acceptDiagnosticListener.Drain().Single();
Eq(delayedDiagnostic.SessionId, 7L, "backend event remains assigned to its originating session");
Eq(delayedDiagnostic.Detail, "verified range", "backend event strips only the metadata prefix");
for (int i = 0; i < 500; i++) acceptDiagnosticListener.WriteLine("Accept check: " + i);
var retainedBackendEvents = acceptDiagnosticListener.Drain().ToArray();
Eq(retainedBackendEvents.Length, SessionDiagnostics.MaximumEvents, "hidden debug window cannot accumulate unbounded backend events");
Eq(retainedBackendEvents[0].Detail, "300", "backend queue retains the most recent events");
Eq(acceptDiagnosticListener.Drain().Count(), 0, "draining backend events removes consumed entries");
var startRules = new SessionStartRules();
Eq(startRules.Items.Count, 3, "the list represents all three current session creation paths");
foreach (var rule in startRules.Items) Eq(startRules.Allows(rule.Id), true, "initial policy preserves each existing start path");
int ruleNotifications = 0;
startRules.Changed += _ => ruleNotifications++;
startRules.SetEnabled(SessionStartRuleId.SelectionReplacement, false);
Eq(startRules.Allows(SessionStartRuleId.SelectionReplacement), false, "selection replacement start can be disabled");
Eq(startRules.Allows(SessionStartRuleId.DirectTyping), true, "disabling selection replacement does not disable direct input");
Eq(startRules.Allows(SessionStartRuleId.DeferredStartup), true, "disabling selection replacement does not disable delayed capture");
Eq(ruleNotifications, 1, "one effective setting change is notified once");
startRules.SetEnabled(SessionStartRuleId.SelectionReplacement, false);
Eq(ruleNotifications, 1, "an unchanged setting does not reset a live session");
foreach (var rule in startRules.Items) startRules.SetEnabled(rule.Id, false);
foreach (var rule in startRules.Items) Eq(startRules.Allows(rule.Id), false, "all session start paths can be independently disabled");
startRules.SetEnabled(SessionStartRuleId.DeferredStartup, true);
Eq(startRules.Allows(SessionStartRuleId.DeferredStartup), true, "delayed capture can be reenabled by itself");
Eq(startRules.Allows(SessionStartRuleId.DirectTyping), false, "reenabling delayed capture leaves direct input disabled");
Eq(startRules.Allows(SessionStartRuleId.SelectionReplacement), false, "reenabling delayed capture leaves selection replacement disabled");
var availability = new EditorAvailability();
Eq(availability.Waiting, false, "a fresh session is not waiting for a missing snapshot");
Eq(availability.CanWait(60000), true, "a first failure after a long idle starts a new grace period");
Eq(availability.Duration(60000), 0L, "idle time is excluded from outage duration");
Eq(availability.CanWait(61199), true, "a continuous outage can retain the session up to the grace boundary");
Eq(availability.CanWait(61200), false, "a continuous outage expires at 1200 ms");
Eq(availability.CanWait(62000), false, "repeated failures cannot extend the same outage deadline");
availability.Reset();
Eq(availability.Waiting, false, "recovery clears the outage");
Eq(availability.CanWait(90000), true, "a separate outage after recovery gets its own grace period");
Eq(availability.Duration(90010), 10L, "only time since the new first failure is counted");
availability.Reset();
Eq(availability.Duration(99999), 0L, "a session reset cannot retain old outage time");
Eq(availability.CanWait(100000), true, "a newly started session cannot inherit an expired deadline");
var typedEvents = new SessionDiagnostics();
typedEvents.Start("test", 0);
var typedLog = new SessionInputLog(typedEvents);
typedLog.Start();
typedLog.Captured(41, "g", "g", "");
Eq(typedEvents.Events.Last().Detail.Contains("t41"), true, "input event retains the physical sequence");
Eq(typedEvents.Events.Last().Detail.Contains("ещё не рассчитана"), true, "uncalculated hints are explicit");
typedLog.Confirmed("g");
typedLog.Calculated("g", "");
Eq(typedEvents.Events.Last().Detail.Contains("[нет подсказки]"), true, "computed empty hint differs from an uncomputed hint");
typedLog.Captured(42, "h", "gh", "g");
Eq(typedEvents.Events.Last().Detail.Contains("ещё не рассчитана"), true, "a previous hint never masquerades as current after typing");
typedLog.Calculated("gh", "пр");
Eq(typedEvents.Events.Last().Detail.Contains("[пр]"), true, "the calculated hint is logged in brackets");
typedLog.Captured(43, "⌫ Backspace", "g", "gh");
var endingFrame = new SessionDiagnosticFrame(1,"pending","fixture","EN RU",0,1,1,"gh","g","п",1,false,43,43);
Eq(typedLog.EndDescription(endingFrame).Contains("t43 «⌫ Backspace»"), true, "end context identifies the last edit");
Eq(typedLog.EndDescription(endingFrame).Contains("подтверждено: gh; ожидалось: g"), true, "end context distinguishes confirmed and projected text");
Eq(SessionInputLog.Visible("a \r\nb\tc"), "a␠↵b⇥c", "whitespace is visible without duplicate CRLF markers");
Eq(SessionInputLog.Describe(Us('g'), KeyboardLayout.Russian), "п", "input labels use captured source layout");
Eq(SessionInputLog.IncludedInInputFilter("Окончание"), true, "input filter preserves session boundaries");
Eq(SessionInputLog.IncludedInInputFilter("Подтверждение"), true, "input filter preserves delivery confirmation");
Eq(SessionInputLog.IncludedInInputFilter("Проверка замены"), false, "input filter hides unrelated backend noise");
typedLog.Start();
Eq(typedLog.EndDescription(endingFrame).Contains("t0 «—»"), true, "new session cannot inherit old last-input identity");
typedEvents.End("ended",endingFrame,"detailed input context");
Eq(typedEvents.LastEndReason.Contains("detailed input context"), false, "full log text cannot expand the compact window header");
Eq(typedEvents.Events.Last().Detail.Contains("detailed input context"), true, "full end context remains available in the log");
var backendChanged = nativeForGeometry with { AutomationIdentity = "42.17", Text = "secret document must not appear in metadata" };
Eq(TextEditor.SameEditor(nativeForGeometry, backendChanged), false, "different backend identities remain unsafe for replacement");
Eq(EditorContextDiagnostics.Changed(nativeForGeometry, backendChanged), "backend Native/UIA", "a backend switch is distinguished from a real HWND change");
Eq(EditorContextDiagnostics.Describe(nativeForGeometry, backendChanged).Contains(backendChanged.Text), false, "context diagnostics never record unrelated full document text");
Eq(EditorContextDiagnostics.Changed(nativeForGeometry, nativeForGeometry with { Text = "other", SelectionStart = 0, SelectionEnd = 0 }), "", "ordinary document and selection edits are not field identity changes");
Eq(EditorContextDiagnostics.Changed(nativeForGeometry, nativeForGeometry with { Editor = (IntPtr)77 }), "HWND поля", "actual field handle change is explicit");
Eq(EditorContextDiagnostics.Changed(nativeForGeometry, nativeForGeometry with { Foreground = (IntPtr)77 }), "активное окно", "actual window change is explicit");
Eq(EditorContextDiagnostics.Changed(nativeForGeometry, nativeForGeometry with { Thread = 77 }), "поток поля", "thread identity change is explicit");
Eq(EditorContextDiagnostics.Changed(backendChanged, backendChanged with { AutomationIdentity = "42.18" }), "UIA-идентификатор поля", "UIA field recreation is distinct from backend switching");
Eq(SessionInputLog.IncludedInInputFilter("Контекст изменился"), true, "input filter retains the exact cause of context-related breaks");
Eq(PreviewThemePalette.UseLightTheme(0, 1), false, "Windows app dark mode takes precedence over light shell mode");
Eq(PreviewThemePalette.UseLightTheme(1, 0), true, "Windows app light mode takes precedence over dark shell mode");
Eq(PreviewThemePalette.UseLightTheme(null, 0), false, "missing app preference falls back to Windows system preference");
Eq(PreviewThemePalette.UseLightTheme("invalid", 1), true, "invalid app preference does not override a valid fallback");
Eq(PreviewThemePalette.UseLightTheme(null, null), true, "missing theme preferences have a deterministic light fallback");
var lightPalette = PreviewThemePalette.Select(true, false, default, default);
var darkPalette = PreviewThemePalette.Select(false, false, default, default);
Eq(lightPalette.Mode, PreviewThemeMode.Light, "light theme selects a light popup");
Eq(darkPalette.Mode, PreviewThemeMode.Dark, "dark theme selects a dark popup");
Eq(lightPalette.Background.A, (byte)255, "light popup background stays opaque above document text");
Eq(darkPalette.Background.A, (byte)255, "dark popup background stays opaque above document text");
double ThemeLuminance(System.Windows.Media.Color color)
{
    double Channel(byte c) { double value = c / 255.0; return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4); }
    return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
}
Eq((ThemeLuminance(lightPalette.Background) + 0.05) / (ThemeLuminance(lightPalette.Text) + 0.05) >= 4.5, true,
    "light popup label has readable contrast against its own opaque background");
var highContrastPalette = PreviewThemePalette.Select(false, true, System.Windows.Media.Colors.Black, System.Windows.Media.Colors.Yellow);
Eq(highContrastPalette.Mode, PreviewThemeMode.HighContrast, "high contrast overrides the normal light/dark palette");
Eq(highContrastPalette.Background, System.Windows.Media.Colors.Black, "high contrast respects the selected system background");
Eq(highContrastPalette.Text, System.Windows.Media.Colors.Yellow, "high contrast respects the selected system foreground");
Eq(highContrastPalette.Border, highContrastPalette.Text, "high contrast border remains visible");
Eq(UiaTextEditor.IsOmniboxFormatting("OmniboxViewViews", ""), true, "Chromium address view is identified without localized names");
Eq(UiaTextEditor.IsOmniboxFormatting("", "Ctrl + L"), true, "omnibox keyboard shortcut survives whitespace formatting");
Eq(UiaTextEditor.IsOmniboxFormatting("Textfield", "Control+L"), true, "omnibox can be identified when UIA exposes only a generic class");
Eq(UiaTextEditor.IsOmniboxFormatting("Textfield", ""), false, "ordinary webpage text fields do not receive omnibox metrics");
Eq(PreviewMetrics.HasReliableFormatting("UIA omnibox"), false, "omnibox metadata cannot bypass glyph limits in the actual renderer path");
Eq(PreviewMetrics.HasReliableFormatting("UIA character"), true, "other UIA glyph formatting remains unchanged");
Eq(PreviewMetrics.HasReliableFormatting("Win32 HFONT"), true, "native font metrics remain authoritative");
foreach (uint dpi in new uint[] { 96, 144, 192 })
    Eq(PreviewMetrics.ResolveFontSize(24, (int)(20 * dpi / 96), dpi, 15, true,
        PreviewMetrics.HasReliableFormatting("UIA omnibox")), 16.0,
        "production omnibox path limits inflated attributes at multiple DPI scales");
Eq(PreviewMetrics.ResolveFontSize(12, 20, 96, 15, true, PreviewMetrics.HasReliableFormatting("UIA omnibox")), 12.0,
    "omnibox metadata smaller than the limit is never enlarged");
Eq(PreviewMetrics.HasReliableFormatting("UIA omnibox; focusedClass=OmniboxViewViews"), false,
    "diagnostic metadata cannot disable the omnibox font bound");
Eq(PreviewMetrics.HasReliableFormatting("UIA character; focusedClass=Textfield"), true,
    "metadata reporting preserves other fields' existing font treatment");
const string missingOmniboxFont = "UIA omnibox; focusedClass=OmniboxViewViews";
Eq(PreviewMetrics.ResolveFontSize(0, 30, 120, 15, true, false, missingOmniboxFont), 14.0,
    "reported Chrome case uses its standard desktop fallback instead of padded 30px caret height");
foreach (uint dpi in new uint[] { 96, 120, 144, 192 })
    Eq(PreviewMetrics.ResolveFontSize(0, (int)(24 * dpi / 96), dpi, 19.2, false, false, missingOmniboxFont), 14.0,
        "missing omnibox font remains 14 DIP across DPI and cannot retain the inflated prior size");
Eq(PreviewMetrics.ResolveFontSize(0, 0, 120, 19.2, true, false, missingOmniboxFont), 14.0,
    "missing geometry does not restore the padded-line font estimate");
Eq(PreviewMetrics.ResolveFontSize(12, 30, 120, 19.2, true, false, missingOmniboxFont), 12.0,
    "available smaller font metadata takes precedence over omnibox fallback");
Eq(PreviewMetrics.ResolveFontSize(0, 22, 120, 14.666666666666666, false, true, "UIA character"), 14.666666666666666,
    "missing native or other UIA metadata still preserves the established size");
foreach (uint dpi in new uint[] { 96, 120, 144, 192 })
{
    const string longText="ghbdtn", shortText="tn";
    double scale=dpi/96.0;
    double longWidth=UiaRangeFont.Width(longText,"Segoe UI",400,false,14)*scale;
    double shortWidth=UiaRangeFont.Width(shortText,"Segoe UI",400,false,14)*scale;
    var longBounds=new System.Windows.Rect(100,100,longWidth,24*scale);
    var shortBounds=new System.Windows.Rect(100+longWidth-shortWidth,100,shortWidth,24*scale);
    Eq(Math.Abs(UiaRangeFont.Estimate(longText,longBounds,shortText,shortBounds,dpi,"Segoe UI",400,false)-14)<0.01,true,
        "range-width estimator returns logical size at multiple DPI scales");
    Eq(UiaRangeFont.Estimate(longText,longBounds,shortText,longBounds,dpi,"Segoe UI",400,false),0.0,
        "a provider returning the whole field for both ranges is rejected");
    Eq(UiaRangeFont.Estimate(longText,longBounds,shortText,shortBounds with { Y=200 },dpi,"Segoe UI",400,false),0.0,
        "ranges on different lines cannot authorize a font estimate");
}
Eq(UiaRangeFont.Estimate("hello",new System.Windows.Rect(0,0,40,double.NaN),"lo",new System.Windows.Rect(28,0,12,20),96,"Segoe UI",400,false),0.0,
    "nonfinite provider geometry is rejected");
Eq(UiaRangeFont.Estimate("hello",System.Windows.Rect.Empty,"lo",new System.Windows.Rect(28,0,12,20),96,"Segoe UI",400,false),0.0,
    "empty provider geometry is rejected");
foreach (uint dpi in new uint[] {96,120,144,192})
{
    double scale=dpi/96.0;
    double wide=UiaRangeFont.Width("gh","Segoe UI",400,false,14)*scale;
    double narrow=UiaRangeFont.Width("h","Segoe UI",400,false,14)*scale;
    Eq(Math.Abs(UiaRangeFont.Estimate("gh",new System.Windows.Rect(100,100,wide,24*scale),"h",new System.Windows.Rect(100+wide-narrow,100,narrow,24*scale),dpi,"Segoe UI",400,false)-14)<0.01,true,
        "two-character bootstrap estimates size from two agreeing ranges");
}
var bootstrap=new UiaRangeFont();
var bootstrapSnapshot=nativeForGeometry with { AutomationIdentity="test", FontSource="UIA character", FontSizeDip=0, FontFamilyName="", SelectionStart=2, SelectionEnd=2 };
Eq(bootstrap.NeedsInitialProbe(bootstrapSnapshot),true,"two characters trigger a worker bootstrap without the formatting timer");
Eq(bootstrap.NeedsInitialProbe(bootstrapSnapshot with {SelectionStart=1, SelectionEnd=1}),false,"one character cannot spend a range attempt");
Eq(bootstrap.NeedsInitialProbe(bootstrapSnapshot with {FontSource="UIA omnibox"}),false,"accepted omnibox formatting has no bootstrap probe");
Eq(bootstrap.NeedsInitialProbe(bootstrapSnapshot with {SelectionStart=0}),false,"selected text does not trigger bootstrap measurement");
var reportedFont=bootstrapSnapshot with {FontSizeDip=16,FontFamilyName="Arial"};
Eq(UiaRangeFont.HasReportedFont(reportedFont),true,"complete UIA font attributes are authoritative");
Eq(bootstrap.NeedsInitialProbe(reportedFont),false,"known UIA font avoids extra bootstrap queries");
Eq(bootstrap.Enrich(reportedFont,null!,0),reportedFont,"short prefixes cannot override complete font metadata or query the range");
Eq(UiaRangeFont.HasReportedFont(reportedFont with {FontSizeDip=0}),false,"missing size permits range fallback");
Eq(UiaRangeFont.HasReportedFont(reportedFont with {FontFamilyName=""}),false,"missing family marks approximate formatting");
Eq(UiaRangeFont.HasReportedFont(reportedFont with {FontSizeDip=double.NaN}),false,"invalid size is not authoritative");
Eq(UiaRangeFont.HasReportedFont(reportedFont with {FontSource="UIA range-width estimate"}),false,"an estimate cannot masquerade as real attributes");
Eq(UiaRangeFont.HasReportedFont(reportedFont with {FontSource="UIA omnibox"}),false,"omnibox retains its existing special metric limits");
Eq(bootstrap.NeedsInitialProbe(reportedFont with {SelectionStart=5,SelectionEnd=5}),true,"reported UIA font can be validated at five characters");
Eq(UiaRangeFont.ShouldCorrectReported(16,14,14.1),true,"three agreeing ranges can correct the reported Chrome size");
Eq(UiaRangeFont.ShouldCorrectReported(16,15.5,15.6),false,"small metric differences retain the reported ChatGPT font");
Eq(UiaRangeFont.ShouldCorrectReported(16,14,16),false,"disagreeing range estimates cannot override metadata");
Eq(UiaRangeFont.ShouldCorrectReported(16,14,0),false,"missing third range preserves complete metadata");
Eq(UiaRangeFont.ShouldCorrectReported(16,double.NaN,14),false,"nonfinite estimates cannot authorize correction");
Eq(UiaRangeFont.ShouldCorrectReported(16,8,8.8),false,"unstable estimates cannot authorize correction");
var available=PreviewFontResolver.Resolve("Segoe UI",400,false,"hello привет");
Eq(available.Substituted,false,"available bilingual font keeps its real family");
var missing=PreviewFontResolver.Resolve("LayoutPreview-Nonexistent-Font-123",400,false,"hello привет");
Eq(missing.Used,"Segoe UI","missing application font selects explicit shared fallback");
Eq(missing.Reason,"family-unavailable","diagnostics explain unavailable families");
Eq(UiaRangeFont.HasReportedFont(reportedFont with {FontFamilyName="LayoutPreview-Nonexistent-Font-123"}),false,"size of an unavailable family is not authoritative for fallback");
Eq(bootstrap.NeedsInitialProbe(reportedFont with {FontFamilyName="LayoutPreview-Nonexistent-Font-123"}),true,"unavailable family gets early range fitting");
Eq(Math.Abs(UiaRangeFont.Width("ghbdtn","LayoutPreview-Nonexistent-Font-123",400,false,16)-UiaRangeFont.Width("ghbdtn",missing.Used,400,false,16))<0.001,true,"measurement and rendering choose the same fallback family");
var composite=PreviewFontResolver.Resolve("Global User Interface",400,false,"hello привет");
Eq(composite.Composite,true,"registered composite fonts are not mistaken for missing physical files");
Eq(composite.Substituted,false,"registered composite family remains usable");
Eq(PreviewFontResolver.Resolve("Segoe UI",400,false,char.ConvertFromUtf32(0x10FFFF)).Reason.StartsWith("glyphs-unavailable"),true,"missing code points trigger explicit composite fallback");
int probeReads=0, probeReports=0;
var oneProbe=new UiaRangeFont((_,_) => { probeReads++; return null; });
oneProbe.StatusChanged += _ => probeReports++;
var measurable=reportedFont with {SelectionStart=5,SelectionEnd=5};
oneProbe.Enrich(measurable,null!,1);
oneProbe.Enrich(measurable,null!,1);
Eq(probeReads,1,"failed font probe is never repeated in the same context");
Eq(probeReports,1,"failed font probe produces one diagnostic report");
Eq(oneProbe.NeedsInitialProbe(measurable),false,"failure cannot trigger another early bootstrap");
oneProbe.Enrich(measurable with {Caret=new Native.RECT {Top=0,Bottom=26}},null!,1);
Eq(probeReads,1,"caret rectangle jitter does not reset the one-attempt budget");
oneProbe.Enrich(measurable,null!,2);
Eq(probeReads,2,"independent focus context receives one new attempt");
Eq(LanguagePlausibility.IsClearlyMorePlausible("rfr ns levftim","как ты думаешь",KeyboardLayout.English),true,"conversation phrase remains recognized after final soft sign");
Eq(LanguagePlausibility.IsClearlyMorePlausible("levftim","думаешь",KeyboardLayout.English),true,"conversational inflection is learned from independent corpus");
Eq(LanguagePlausibility.IsClearlyMorePlausible("ns pyftim","ты знаешь",KeyboardLayout.English),true,"other conversational inflections improve without a whitelist");
Eq(LanguagePlausibility.IsClearlyMorePlausible("как ты думаешь","rfr ns levftim",KeyboardLayout.Russian),false,"proper Russian phrase does not suggest English corruption");
int backspaceSnapshotReads=0;
EditorSnapshot? ReadForBackspaceTest() { backspaceSnapshotReads++; return measurable; }
Eq(KeyboardHookPump.SnapshotForKey(0x08,ReadForBackspaceTest),null,"Backspace never needs an editor snapshot on the hook thread");
Eq(backspaceSnapshotReads,0,"Backspace bypasses all provider focus checks");
Eq(KeyboardHookPump.SnapshotForKey(0x27,ReadForBackspaceTest),measurable,"Right retains checked acceptance snapshot");
Eq(backspaceSnapshotReads,1,"only keys needing context invoke the snapshot reader");
var sectionSession=new InputSession("OLD ",4,KeyboardLayout.English);
string sectionDocument="OLD ";
void SectionType(string keys,KeyboardLayout layout) {
 foreach(char c in keys) {var k=Us(c);int end=sectionSession.End;sectionSession.Append(k,layout,sectionDocument);KeyboardConversion.TryConvert(k,layout,out char typed);sectionDocument=sectionDocument.Insert(end,typed.ToString());}
}
SectionType("ghbdtn",KeyboardLayout.English);
Eq(sectionSession.Preview,"привет","initial section preview");
sectionSession.ChangeLayout(KeyboardLayout.Russian);
Eq(sectionSession.Preview,"","layout switch clears previous preview before new characters");
Eq(sectionSession.Source,"ghbdtn","manual switch does not discard session text");
sectionSession.ChangeLayout(KeyboardLayout.English);
Eq(sectionSession.Preview,"","switching back cannot resurrect an earlier hint");
SectionType(" ghbdtn",KeyboardLayout.English);
Eq(sectionSession.Replacement,"ghbdtn привет","switch back preserves earlier same-layout section");
var activeSection=sectionSession.CreateActiveReplacement(sectionDocument,sectionDocument.Length,sectionDocument.Length)!;
Eq(activeSection.Start,10,"physical replacement begins at current section boundary");
Eq(activeSection.Source," ghbdtn","replacement range excludes previous source");
Eq(activeSection.Matches(sectionDocument,sectionDocument.Length,sectionDocument.Length),true,"transaction validates complete document including previous sections");
Eq(activeSection.Matches("BAD "+sectionDocument[4..],sectionDocument.Length,sectionDocument.Length),false,"changed prefix fails transaction validation");
Eq(sectionSession.CreateActiveReplacement(sectionDocument,0,0),null,"moved caret cannot create replacement transaction");
for(int n=0;n<7;n++){sectionSession.Backspace(sectionDocument);sectionDocument=sectionDocument[..^1];}
Eq(sectionSession.Preview,"","deleting current section cannot reactivate an old section");
sectionSession.Backspace(sectionDocument);sectionDocument=sectionDocument[..^1];
Eq(sectionSession.ActiveStart,sectionSession.End,"deletion across boundary leaves empty current section");
Eq(sectionSession.Matches(sectionDocument,sectionDocument.Length,sectionDocument.Length),true,"cross-boundary Backspace retains checked document");
Eq(sectionSession.Copy().ActiveStart,sectionSession.ActiveStart,"copy preserves current boundary");
var pendingSections=new InputSession("",0,KeyboardLayout.Russian);
pendingSections.ReplayPending(new[]{new QueuedInput((uint)'G',false,false,KeyboardLayout.Russian),new QueuedInput((uint)'H',false,false,KeyboardLayout.Russian),new QueuedInput((uint)'B',false,false,KeyboardLayout.Russian),new QueuedInput((uint)'G',false,false,KeyboardLayout.English),new QueuedInput((uint)'H',false,false,KeyboardLayout.English),new QueuedInput((uint)'B',false,false,KeyboardLayout.English)});
Eq(pendingSections.Reconcile("приghb",6,6),true,"queued input across a switch confirms one complete session");
Eq(pendingSections.SourceLayout,KeyboardLayout.English,"queued section uses latest captured layout");
Eq(pendingSections.ActiveStart,3,"queued switch records exact section boundary");
Eq(pendingSections.Replacement,"припри","queued replacement preserves the earlier section");
Eq(pendingSections.CreateActiveReplacement("приghb",6,6)!.Source,"ghb","queued transaction selects only the current section");
int experimentChanges=0;
RangeFontExperiment.Changed += _ => experimentChanges++;
RangeFontExperiment.SetEnabled(false);
Eq(RangeFontExperiment.Enabled,false,"experiment can be disabled live");
RangeFontExperiment.SetEnabled(false);
Eq(experimentChanges,1,"unchanged experiment settings do not reset sessions");
RangeFontExperiment.SetEnabled(true);
Eq(RangeFontExperiment.Enabled,true,"experiment can be reenabled live");
Console.WriteLine($"PASS: {checks} regression checks");
