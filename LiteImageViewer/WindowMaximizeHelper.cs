using System.Runtime.InteropServices;

namespace LiteImageViewer;

internal static class WindowMaximizeHelper
{
    public static void AdjustMaximizedSize(nint hwnd, nint lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == nint.Zero) return;

        var monitorInfo = new MONITORINFO
        {
            cbSize = Marshal.SizeOf<MONITORINFO>()
        };

        if (!GetMonitorInfo(monitor, ref monitorInfo)) return;

        var minMaxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        var workArea = monitorInfo.rcWork;
        var monitorArea = monitorInfo.rcMonitor;

        minMaxInfo.ptMaxPosition.x = Math.Abs(workArea.left - monitorArea.left);
        minMaxInfo.ptMaxPosition.y = Math.Abs(workArea.top - monitorArea.top);
        minMaxInfo.ptMaxSize.x = Math.Abs(workArea.right - workArea.left);
        minMaxInfo.ptMaxSize.y = Math.Abs(workArea.bottom - workArea.top);
        minMaxInfo.ptMaxTrackSize.x = minMaxInfo.ptMaxSize.x;
        minMaxInfo.ptMaxTrackSize.y = minMaxInfo.ptMaxSize.y;

        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, int dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}
