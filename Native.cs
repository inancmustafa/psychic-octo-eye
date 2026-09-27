using System;
using System.Runtime.InteropServices;
using System.Text;

// Win32 yardımcıları: odak çalmayan pencere, imleç, Alt tuşu ve tam ekran algılama.
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);

    public static bool AltDown()
    {
        return (GetAsyncKeyState(0x12) & 0x8000) != 0;
    }

    // Tıklanınca odağı almayan ve Alt+Tab listesinde görünmeyen pencere
    public static void MakeToolWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        int style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    // Aynı monitörde ön plandaki pencere tam ekran mı? Ekranı taşan büyütülmüş pencereler sayılmaz.
    public static bool IsForegroundFullscreen(IntPtr self)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == self || foreground == GetShellWindow() || foreground == GetDesktopWindow()) return false;
        var name = new StringBuilder(64);
        GetClassName(foreground, name, name.Capacity);
        string cls = name.ToString();
        if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return false;
        IntPtr monitor = MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero || monitor != MonitorFromWindow(self, MONITOR_DEFAULTTONEAREST)) return false;
        int state;
        if (SHQueryUserNotificationState(out state) == 0 && (state == 3 || state == 4)) return true; // D3D tam ekran, sunum modu
        RECT rect;
        if (!GetWindowRect(foreground, out rect)) return false;
        var info = new MONITORINFO();
        info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetMonitorInfo(monitor, ref info)) return false;
        return rect.Left == info.rcMonitor.Left && rect.Top == info.rcMonitor.Top && rect.Right == info.rcMonitor.Right && rect.Bottom == info.rcMonitor.Bottom;
    }
}
