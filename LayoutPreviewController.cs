using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace LayoutPreviewPrototype;

public sealed class LayoutPreviewController : IDisposable
{
    private readonly PreviewWindow _preview = new();
    private readonly List<uint> _wordKeys = new();
    private Native.LowLevelKeyboardProc? _proc;
    private IntPtr _hook;
    private string _previewText = string.Empty;
    private IntPtr _alternativeLayout;
    private bool _targetRussian;
    private bool _visible;
    private bool _accepting;

    public void Start()
    {
        _preview.AcceptRequested += (_, _) =>
        {
            if (_visible)
                AcceptPreview();
        };

        _proc = HookCallback;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc,
            Native.GetModuleHandle(module.ModuleName), 0);

        if (_hook == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

        Debug.WriteLine("[LayoutPreview] Started. Open Notepad and type with EN/RU layout.");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || (wParam != (IntPtr)Native.WM_KEYDOWN && wParam != (IntPtr)Native.WM_SYSKEYDOWN))
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
        if ((data.flags & Native.LLKHF_INJECTED) != 0 || _accepting)
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);

        int vk = (int)data.vkCode;

        // Never reinterpret normal editor shortcuts (Ctrl+A/C/X/V/Z/Y, Alt shortcuts, Win shortcuts).
        // They remain completely native and also end the current tracked session.
        bool ctrl = Native.IsKeyDown(Native.VK_CONTROL);
        bool alt = Native.IsKeyDown(Native.VK_MENU);
        bool win = Native.IsKeyDown(Native.VK_LWIN) || Native.IsKeyDown(Native.VK_RWIN);
        if (ctrl || alt || win)
        {
            Reset();
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // Accept only while a valid live preview exists.
        if (vk == Native.VK_RIGHT && _visible && _wordKeys.Count > 0 && _previewText.Length > 0)
        {
            Application.Current.Dispatcher.BeginInvoke(AcceptPreview);
            return (IntPtr)1; // swallow only this one Right Arrow
        }

        // Navigation means "user is editing/navigating", so dismiss and preserve native behavior.
        if (vk is Native.VK_LEFT or Native.VK_UP or Native.VK_DOWN or Native.VK_HOME or Native.VK_END
            or Native.VK_PRIOR or Native.VK_NEXT or Native.VK_DELETE or Native.VK_ESCAPE)
        {
            Reset();
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (vk == Native.VK_BACK)
        {
            if (_wordKeys.Count > 0)
            {
                _wordKeys.RemoveAt(_wordKeys.Count - 1);
                Application.Current.Dispatcher.BeginInvoke(UpdatePreview);
            }
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        if (vk is Native.VK_SPACE or Native.VK_RETURN or Native.VK_TAB)
        {
            Reset();
            return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // v0.1 intentionally limits live word tracking to common printable letter/number VKs.
        if ((vk >= 0x30 && vk <= 0x5A) || (vk >= 0xBA && vk <= 0xDE))
        {
            _wordKeys.Add(data.vkCode);
            Application.Current.Dispatcher.BeginInvoke(UpdatePreview);
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void UpdatePreview()
    {
        if (_wordKeys.Count == 0)
        {
            Reset();
            return;
        }

        var fg = Native.GetForegroundWindow();
        if (fg == IntPtr.Zero)
        {
            Reset();
            return;
        }

        uint thread = Native.GetWindowThreadProcessId(fg, IntPtr.Zero);
        IntPtr current = Native.GetKeyboardLayout(thread);
        _alternativeLayout = FindAlternativeLayout(current);
        if (_alternativeLayout == IntPtr.Zero)
        {
            Reset();
            return;
        }

        _targetRussian = IsRussianLayout(_alternativeLayout);
        var converted = ConvertVirtualKeys(_wordKeys, _targetRussian);
        if (string.IsNullOrWhiteSpace(converted))
        {
            Reset();
            return;
        }

        if (!TryGetCaretScreenRect(thread, out var caretRect))
        {
            Reset();
            return;
        }

        uint dpi = Native.GetDpiForWindow(fg);
        if (dpi == 0) dpi = 96;

        _previewText = converted;
        Debug.WriteLine($"[LayoutPreview] Preview: {_previewText} caret=({caretRect.Left},{caretRect.Top})-({caretRect.Right},{caretRect.Bottom}) dpi={dpi}");
        bool first = !_visible;
        _visible = true;
        _preview.ShowPreview(converted, caretRect, dpi, first);
    }

    private static IntPtr FindAlternativeLayout(IntPtr current)
    {
        int count = Native.GetKeyboardLayoutList(0, null);
        if (count <= 1) return IntPtr.Zero;
        var list = new IntPtr[count];
        Native.GetKeyboardLayoutList(count, list);
        return list.FirstOrDefault(hkl => hkl != current);
    }

    private static string ConvertVirtualKeys(IEnumerable<uint> keys, bool toRussian) =>
        KeyboardConversion.Convert(keys.ToArray(), toRussian);

    private static bool IsRussianLayout(IntPtr hkl) =>
        unchecked((ushort)hkl.ToInt64()) == 0x0419;

    private static bool TryGetCaretScreenRect(uint thread, out Native.RECT screenRect)
    {
        screenRect = default;
        var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
        if (!Native.GetGUIThreadInfo(thread, ref info) || info.hwndCaret == IntPtr.Zero)
            return false;

        var topLeft = new Native.POINT { X = info.rcCaret.Left, Y = info.rcCaret.Top };
        var bottomRight = new Native.POINT { X = info.rcCaret.Right, Y = info.rcCaret.Bottom };
        if (!Native.ClientToScreen(info.hwndCaret, ref topLeft) ||
            !Native.ClientToScreen(info.hwndCaret, ref bottomRight))
            return false;

        screenRect = new Native.RECT
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = bottomRight.X,
            Bottom = bottomRight.Y
        };
        return true;
    }

    private async void AcceptPreview()
    {
        if (!_visible || _previewText.Length == 0 || _wordKeys.Count == 0)
            return;

        _accepting = true;
        Debug.WriteLine($"[LayoutPreview] Accept: {_previewText}");
        try
        {
            // Start the same visual contract as the HTML prototype: the framed suggestion
            // moves left toward the typed word and is fully dissolved at the midpoint.
            var animation = _preview.PlayAcceptAnimationAsync();
            await System.Threading.Tasks.Task.Delay(PreviewWindow.AcceptMidpointDelayMs);

            // At the midpoint the moving frame is already invisible. Replace the source
            // text now so the final plain accepted word appears underneath it.
            var inputs = new List<Native.INPUT>();
            for (int i = 0; i < _wordKeys.Count; i++)
            {
                inputs.Add(KeyInput((ushort)Native.VK_BACK, 0));
                inputs.Add(KeyInput((ushort)Native.VK_BACK, Native.KEYEVENTF_KEYUP));
            }

            // Insert the preview text as Unicode so the prototype does not depend on key synthesis.
            foreach (char ch in _previewText)
            {
                inputs.Add(UnicodeInput(ch, 0));
                inputs.Add(UnicodeInput(ch, Native.KEYEVENTF_KEYUP));
            }

            Native.SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Native.INPUT>());

            // Ask the focused foreground window to switch to the accepted layout.
            var fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && _alternativeLayout != IntPtr.Zero)
                Native.SendMessage(fg, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, _alternativeLayout);

            await animation;
        }
        finally
        {
            Reset();
            _accepting = false;
        }
    }

    private static Native.INPUT KeyInput(ushort vk, uint flags) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion { ki = new Native.KEYBDINPUT { wVk = vk, dwFlags = flags } }
    };

    private static Native.INPUT UnicodeInput(char ch, uint flags) => new()
    {
        type = Native.INPUT_KEYBOARD,
        U = new Native.InputUnion
        {
            ki = new Native.KEYBDINPUT { wVk = 0, wScan = ch, dwFlags = Native.KEYEVENTF_UNICODE | flags }
        }
    };

    private void Reset()
    {
        _wordKeys.Clear();
        _previewText = string.Empty;
        _alternativeLayout = IntPtr.Zero;
        _targetRussian = false;
        _visible = false;
        Application.Current.Dispatcher.BeginInvoke(_preview.HidePreview, DispatcherPriority.Background);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
            Native.UnhookWindowsHookEx(_hook);
        _preview.Close();
    }
}
