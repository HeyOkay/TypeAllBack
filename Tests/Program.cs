using LayoutPreviewPrototype;
using System.Runtime.InteropServices;
using System.Text;
if (args.Contains("--probe"))
{
    var fg = Native.GetForegroundWindow(); var thread = Native.GetWindowThreadProcessId(fg, out uint pid);
    var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() }; Native.GetGUIThreadInfo(thread, ref info);
    var cls = new StringBuilder(256); Native.GetClassName(info.hwndFocus, cls, 256);
    Console.WriteLine($"PID={pid} class={cls} flags={info.flags} layout=0x{Native.GetKeyboardLayout(thread).ToInt64():X} caretHeight={info.rcCaret.Bottom-info.rcCaret.Top}");
    Console.WriteLine(NotepadEditor.TryRead(out var snapshot) ? $"Adapter=OK length={snapshot.Text.Length} selection={snapshot.SelectionStart},{snapshot.SelectionEnd} dpi={snapshot.Dpi}" : "Adapter=UNSUPPORTED");
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
Eq(session.Preview,"привет","space retains preview"); Eq(session.Replacement,"привет ","sentence replacement");
Type("vbh",KeyboardLayout.Russian);
Eq(session.Replacement,"привет мир","correct segment unchanged"); Eq(session.Segments.Count,2,"layout segments");
Eq(session.Preview,"","correct last segment has no accept preview");
Type(" pf,e[fk",KeyboardLayout.English);
Eq(session.Preview,"забухал","latest word"); Eq(session.Replacement,"привет мир забухал","mixed segments");
Eq(session.Start,initial,"old text outside range");
Eq(session.Matches(document,session.End-1,session.End-1),false,"moved caret invalid");
Eq(session.Matches(document,session.Start,session.End),false,"selection invalid");
Eq(session.Matches("X"+document[1..],session.End,session.End),false,"external edit invalid");
int before=session.End; Eq(session.Backspace(document),true,"backspace"); document=document.Remove(before-1,1);
Eq(session.Preview,"забуха","backspace preview"); Eq(session.Matches(document,session.End,session.End),true,"backspace context");
var empty = new InputSession("old",3,KeyboardLayout.English); Eq(empty.Backspace("old"),false,"cannot delete pre session");
Eq(Marshal.SizeOf<Native.INPUT>(),40,"x64 INPUT ABI");
Console.WriteLine($"PASS: {checks} regression checks");