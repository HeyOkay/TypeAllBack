using System.Runtime.InteropServices;

namespace LayoutPreviewPrototype;

internal static class AutoEnterOptions
{
    private static int _enabled;
    public static bool Enabled => Volatile.Read(ref _enabled) != 0;
    public static event Action<bool>? Changed;
    public static void SetEnabled(bool enabled)
    {
        int value=enabled?1:0;
        if(Interlocked.Exchange(ref _enabled,value)!=value)Changed?.Invoke(enabled);
    }
}

internal enum EnterDisposition { Native, Intercept, Suppress }
// Held state belongs to the hook; completion can arrive from the dispatcher.
internal sealed class EnterReservation
{
    private bool _held;
    private int _busy;
    internal EnterDisposition Handle(uint key,bool down,bool up,bool plain,bool eligible)
    {
        if(key!=0x0D)return EnterDisposition.Native;
        if(up && _held){_held=false;return EnterDisposition.Suppress;}
        if(!down || !plain)return EnterDisposition.Native;
        if(_held || Volatile.Read(ref _busy)!=0){_held=true;return EnterDisposition.Suppress;}
        if(!eligible)return EnterDisposition.Native;
        _held=true;Interlocked.Exchange(ref _busy,1);return EnterDisposition.Intercept;
    }
    internal void Complete()=>Interlocked.Exchange(ref _busy,0);
    internal void Abandon(){_held=false;Complete();}
}

internal static class EnterReplay
{
    internal static bool Matches(EditorSnapshot expected,EditorSnapshot actual)=>
        TextEditor.SameEditor(expected,actual) && expected.Layout==actual.Layout &&
        expected.Text==actual.Text && actual.SelectionStart==actual.SelectionEnd &&
        expected.SelectionStart==actual.SelectionStart && expected.SelectionEnd==actual.SelectionEnd;
    internal static Native.INPUT[] Inputs()=>new[]{Key(false),Key(true)};
    private static Native.INPUT Key(bool up)=>new(){type=Native.INPUT_KEYBOARD,U=new Native.InputUnion
        {ki=new Native.KEYBDINPUT{wVk=0x0D,dwFlags=up?Native.KEYEVENTF_KEYUP:0,dwExtraInfo=Native.LayoutPreviewInput}}};
    internal static bool Send()=>Native.SendInput(2,Inputs(),Marshal.SizeOf<Native.INPUT>())==2;
}
