using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace LayoutPreviewPrototype;

internal sealed class TrayNative : IDisposable
{
    private readonly HwndSource _source;
    private readonly Action _showMenu;
    private IconData _data;
    private readonly uint _taskbarCreated;
    private const int Callback = 0x8001;
    internal TrayNative(Action showMenu)
    {
        _showMenu = showMenu;
        _source = new HwndSource(new HwndSourceParameters("TypeAllBack tray") { Width = 0, Height = 0, WindowStyle = 0x00800000 });
        _source.AddHook(Hook);
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        ExtractIconEx(Environment.ProcessPath!, 0, out var large, out var small, 1);
        if (large != IntPtr.Zero) DestroyIcon(large);
        _data = new() { cbSize = (uint)Marshal.SizeOf<IconData>(), hWnd = _source.Handle, uID = 1,
            uFlags = 1 | 2 | 4, uCallbackMessage = Callback, hIcon = small, szTip = "TypeAllBack", szInfo = "", szInfoTitle = "" };
        if (small == IntPtr.Zero || !Add()) { Dispose(); throw new InvalidOperationException("Tray icon could not be created"); }
    }
    private bool Add()
    {
        bool added = Shell_NotifyIcon(0, ref _data);
        _data.uVersion = 4;
        if (added) Shell_NotifyIcon(4, ref _data);
        return added;
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == _taskbarCreated) Add();
        if (message == Callback)
        {
            int kind = (int)(lParam.ToInt64() & 0xffff);
            if (kind is 0x007B or 0x0400 or 0x0401) _showMenu();
            handled = true;
        }
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        Shell_NotifyIcon(2, ref _data);
        if (_data.hIcon != IntPtr.Zero) { DestroyIcon(_data.hIcon); _data.hIcon = IntPtr.Zero; }
        _source.Dispose();
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        internal uint cbSize; internal IntPtr hWnd; internal uint uID, uFlags, uCallbackMessage; internal IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string szTip;
        internal uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string szInfo;
        internal uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string szInfoTitle;
        internal uint dwInfoFlags; internal Guid guidItem; internal IntPtr hBalloonIcon;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIcon(uint message, ref IconData data);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
}
