using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace SleekPicker.App;

internal static class NativeMethods
{
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    internal const int GWL_EXSTYLE = -20;
    internal const long WS_EX_TOOLWINDOW = 0x00000080L;

    internal const int DWMWA_CLOAKED = 14;

    internal const int GW_OWNER = 4;

    internal const uint WM_GETICON = 0x007F;
    internal const uint WM_CLOSE = 0x0010;
    internal const int ICON_SMALL = 0;
    internal const int ICON_BIG = 1;
    internal const int ICON_SMALL2 = 2;
    internal const int GCL_HICON = -14;
    internal const int GCL_HICONSM = -34;

    internal const int SW_RESTORE = 9;
    internal const int SW_SHOW = 5;

    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_LEFT = 0x25;
    private const byte VK_RIGHT = 0x27;
    private const byte VK_LWIN = 0x5B;

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW", SetLastError = true)]
    private static extern nint GetClassLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetClassLongW", SetLastError = true)]
    private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);

    internal static string GetWindowTitle(IntPtr hWnd)
    {
        var textLength = GetWindowTextLengthW(hWnd);
        if (textLength <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(textLength + 1);
        _ = GetWindowTextW(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    internal static nint GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    internal static nint GetClassLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetClassLongPtr64(hWnd, nIndex)
            : new IntPtr(unchecked((int)GetClassLong32(hWnd, nIndex)));
    }

    internal static bool IsWindowCloaked(IntPtr hWnd)
    {
        var result = DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out var cloaked, sizeof(int));
        return result == 0 && cloaked != 0;
    }

    internal static bool TryGetCursorPosition(out POINT point)
    {
        return GetCursorPos(out point);
    }

    internal static WorkArea GetWorkAreaForPoint(POINT point)
    {
        var monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO
        {
            cbSize = Marshal.SizeOf<MONITORINFO>(),
        };

        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref monitorInfo))
        {
            return new WorkArea(
                monitorInfo.rcWork.Left,
                monitorInfo.rcWork.Top,
                monitorInfo.rcWork.Right,
                monitorInfo.rcWork.Bottom);
        }

        var fallback = SystemParameters.WorkArea;
        return new WorkArea(
            (int)fallback.Left,
            (int)fallback.Top,
            (int)fallback.Right,
            (int)fallback.Bottom);
    }

    internal static void SendVirtualDesktopSwitch(bool moveRight)
    {
        var arrowKey = moveRight ? VK_RIGHT : VK_LEFT;

        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(arrowKey, 0, 0, UIntPtr.Zero);
        keybd_event(arrowKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;

        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;

        public int Top;

        public int Right;

        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    internal struct MONITORINFO
    {
        public int cbSize;

        public RECT rcMonitor;

        public RECT rcWork;

        public uint dwFlags;
    }
}

internal readonly record struct WorkArea(int Left, int Top, int Right, int Bottom)
{
    public int Width => Math.Max(1, Right - Left);

    public int Height => Math.Max(1, Bottom - Top);
}
