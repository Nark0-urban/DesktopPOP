using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPop;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int idx);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int idx, IntPtr v);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern IntPtr VirtualAllocEx(IntPtr hProc, IntPtr addr, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll")] public static extern bool VirtualFreeEx(IntPtr hProc, IntPtr addr, UIntPtr size, uint type);
    [DllImport("kernel32.dll")] public static extern bool ReadProcessMemory(IntPtr hProc, IntPtr addr, byte[] buf, UIntPtr size, out UIntPtr read);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr hProc, IntPtr addr, byte[] buf, UIntPtr size, out UIntPtr written);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public int c0, c1, c2, c3; // room for color masks
    }

    [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int size, out BITMAP bm);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BITMAPINFO bi, uint usage);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);

    public static RECT PrimaryMonitorRect()
    {
        var mon = MonitorFromPoint(new POINT { X = 0, Y = 0 }, 1 /*MONITOR_DEFAULTTOPRIMARY*/);
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(mon, ref mi);
        return mi.rcMonitor;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT pt; }
    [DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO ci);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr hInst, IntPtr id);

    static IntPtr[]? normalCursors;

    /// <summary>
    /// 지금 커서가 "파일 끌기" 모양인지 (화살표·글자·크기조절·손가락 같은 보통 커서가 아니면 끌기 중으로 봄).
    /// 스크롤바를 잡고 있을 때(화살표)는 가장자리가 튀어나오지 않게 하려고.
    /// </summary>
    public static bool LooksLikeDragCursor()
    {
        normalCursors ??= new[] { 32512, 32513, 32514, 32515, 32642, 32643, 32644, 32645, 32646, 32649, 32650 }
            .Select(id => LoadCursor(IntPtr.Zero, new IntPtr(id))).ToArray();
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero) return false;
        return Array.IndexOf(normalCursors, ci.hCursor) < 0;
    }

    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);

    /// <summary>그 위치에 보이는 게 (숨겨진) 원래 바탕화면인지. 다른 창이나 우리 판/가방 위면 false.</summary>
    public static bool IsDesktopAt(POINT p)
    {
        var h = WindowFromPoint(p);
        if (h == IntPtr.Zero) return false;
        var root = GetAncestor(h, 2 /*GA_ROOT*/);
        if (root == IntPtr.Zero) root = h;
        var sb = new StringBuilder(32);
        GetClassName(root, sb, 32);
        return sb.ToString() is "Progman" or "WorkerW";
    }

    /// <summary>지금 바탕화면 자체가 앞에 있는지 (바탕화면 우클릭 → 새로 만들기 같은 경우).</summary>
    public static bool IsDesktopForeground()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return true;
        var sb = new StringBuilder(32);
        GetClassName(fg, sb, 32);
        return sb.ToString() is "Progman" or "WorkerW";
    }

    /// <summary>물리 픽셀 ÷ 이 값 = WPF 좌표(DIP).</summary>
    public static double DipScale()
    {
        var mon = PrimaryMonitorRect();
        double s = (mon.Right - mon.Left) / System.Windows.SystemParameters.PrimaryScreenWidth;
        return s > 0 ? s : 1;
    }

    /// <summary>Alt+Tab, 작업 표시줄에 안 보이게.</summary>
    public static void MakeToolWindow(IntPtr hwnd)
    {
        const int GWL_EXSTYLE = -20;
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= 0x80;        // WS_EX_TOOLWINDOW
        ex &= ~0x40000L;   // WS_EX_APPWINDOW
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    public static void SetTopmost(IntPtr hwnd)
    {
        SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);

    public static bool TryGetTaskbarRect(out RECT r)
    {
        r = default;
        var tb = FindWindow("Shell_TrayWnd", null);
        return tb != IntPtr.Zero && IsWindowVisible(tb) && GetWindowRect(tb, out r);
    }

    /// <summary>커서가 작업 표시줄(시작 버튼, 검색, 바탕화면 보기 버튼 포함) 위에 있는지.</summary>
    public static bool InTaskbar(POINT p)
    {
        if (!TryGetTaskbarRect(out var r)) return false;
        return p.X >= r.Left - 2 && p.X <= r.Right + 2 && p.Y >= r.Top - 2 && p.Y <= r.Bottom + 2;
    }

    /// <summary>지금 튀어나오면 안 되는 상황인지 (게임·영상 전체화면, 또는 최대화된 창이 앞에 있음).</summary>
    public static bool ShouldBlock(RECT mon, bool blockFullscreen, bool blockMaximized)
    {
        // 윈도우가 알려 주는 "전체화면 앱 실행 중" 상태 (게임, 발표 모드 등).
        // 단, 게임이 켜져 있기만 해도(바탕화면 보기로 내려가 있어도) 계속 "실행 중"이라고 하므로
        // 이건 맨 앞 창이 진짜 화면을 거의 다 덮고 있을 때만 믿음.
        bool fullscreenAppRunning = blockFullscreen && SHQueryUserNotificationState(out int s) == 0 && (s == 2 || s == 3 || s == 4);

        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return fullscreenAppRunning;
        var sb = new StringBuilder(64);
        GetClassName(fg, sb, 64);
        var cls = sb.ToString();
        // 바탕화면·작업 표시줄이 앞에 있으면(바탕화면 보기 버튼 등) 게임이 켜져 있어도 열어 줌
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        GetWindowThreadProcessId(fg, out uint pid);
        if (pid == (uint)Environment.ProcessId) return false;

        var primary = MonitorFromPoint(new POINT(), 1);
        bool onPrimary = MonitorFromWindow(fg, 2 /*NEAREST*/) == primary;
        if (!onPrimary) return false;

        if (blockFullscreen)
        {
            GetWindowRect(fg, out var r);
            if (r.Left <= mon.Left && r.Top <= mon.Top && r.Right >= mon.Right && r.Bottom >= mon.Bottom) return true;
            // 전체화면 게임인데 창 크기를 이상하게 알려 주는 경우: 화면의 90% 이상을 덮으면 게임으로 봄
            if (fullscreenAppRunning)
            {
                long monArea = (long)(mon.Right - mon.Left) * (mon.Bottom - mon.Top);
                long w = Math.Max(0, Math.Min(r.Right, mon.Right) - Math.Max(r.Left, mon.Left));
                long h = Math.Max(0, Math.Min(r.Bottom, mon.Bottom) - Math.Max(r.Top, mon.Top));
                if (monArea > 0 && w * h >= monArea * 9 / 10) return true;
            }
        }
        return blockMaximized && IsZoomed(fg);
    }
}
