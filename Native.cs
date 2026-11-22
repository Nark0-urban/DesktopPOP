using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace DesktopPop;

/// <summary>Windows API 모음과 화면·모니터·창 관련 계산 (가장자리 판정, 바탕화면이 보이는지, 전체화면 판정 등).</summary>
internal static class Native
{
	public struct POINT
	{
		public int X;

		public int Y;
	}

	public struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	public struct SIZE
	{
		public int cx;

		public int cy;
	}

	public struct MONITORINFO
	{
		public int cbSize;

		public RECT rcMonitor;

		public RECT rcWork;

		public uint dwFlags;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct MONITORINFOEX
	{
		public int cbSize;

		public RECT rcMonitor;

		public RECT rcWork;

		public uint dwFlags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string szDevice;
	}

	internal sealed record MonitorInfo(nint Handle, string DeviceName, RECT Bounds, RECT WorkArea, bool IsPrimary);

	private delegate bool MonitorEnumProc(nint monitor, nint dc, ref RECT bounds, nint data);

	public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	public struct BITMAP
	{
		public int bmType;

		public int bmWidth;

		public int bmHeight;

		public int bmWidthBytes;

		public ushort bmPlanes;

		public ushort bmBitsPixel;

		public nint bmBits;
	}

	public struct BITMAPINFO
	{
		public int biSize;

		public int biWidth;

		public int biHeight;

		public ushort biPlanes;

		public ushort biBitCount;

		public int biCompression;

		public int biSizeImage;

		public int biXPelsPerMeter;

		public int biYPelsPerMeter;

		public int biClrUsed;

		public int biClrImportant;

		public int c0;

		public int c1;

		public int c2;

		public int c3;
	}

	public struct CURSORINFO
	{
		public int cbSize;

		public int flags;

		public nint hCursor;

		public POINT pt;
	}

	private readonly record struct DesktopProbeWindow(RECT Bounds, bool IsDesktop);

	private static nint[]? normalCursors;

	private static readonly List<DesktopProbeWindow> desktopProbeWindows = new List<DesktopProbeWindow>();

	private static long desktopProbeSnapshotAt = long.MinValue;

	[DllImport("user32.dll")]
	private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorEnumProc callback, nint data);

	[DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
	private static extern bool GetMonitorInfoEx(nint monitor, ref MONITORINFOEX info);

	[DllImport("user32.dll")]
	public static extern bool GetCursorPos(out POINT p);

	[DllImport("user32.dll")]
	public static extern nint MonitorFromPoint(POINT pt, uint flags);

	[DllImport("user32.dll")]
	public static extern bool GetMonitorInfo(nint hMon, ref MONITORINFO mi);

	[DllImport("user32.dll")]
	public static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	public static extern bool GetWindowRect(nint hWnd, out RECT r);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern int GetClassName(nint hWnd, StringBuilder sb, int max);

	[DllImport("user32.dll")]
	public static extern short GetAsyncKeyState(int vk);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern nint FindWindow(string? cls, string? name);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	public static extern nint FindWindowEx(nint parent, nint after, string? cls, string? name);

	[DllImport("user32.dll")]
	public static extern bool EnumWindows(EnumWindowsProc cb, nint lParam);

	[DllImport("user32.dll")]
	public static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll")]
	public static extern nint SendMessage(nint hWnd, uint msg, nint w, nint l);

	[DllImport("user32.dll")]
	public static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
	public static extern nint GetWindowLongPtr(nint h, int idx);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
	public static extern nint SetWindowLongPtr(nint h, int idx, nint v);

	[DllImport("user32.dll")]
	public static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);

	[DllImport("gdi32.dll")]
	public static extern int GetObject(nint h, int size, out BITMAP bm);

	[DllImport("gdi32.dll")]
	public static extern int GetDIBits(nint hdc, nint hbm, uint start, uint lines, byte[] bits, ref BITMAPINFO bi, uint usage);

	[DllImport("gdi32.dll")]
	public static extern bool DeleteObject(nint h);

	[DllImport("user32.dll")]
	public static extern nint GetDC(nint h);

	[DllImport("user32.dll")]
	public static extern int ReleaseDC(nint h, nint dc);

	public static RECT PrimaryMonitorRect()
	{
		nint hMon = MonitorFromPoint(new POINT
		{
			X = 0,
			Y = 0
		}, 1u);
		MONITORINFO mi = new MONITORINFO
		{
			cbSize = Marshal.SizeOf<MONITORINFO>()
		};
		GetMonitorInfo(hMon, ref mi);
		return mi.rcMonitor;
	}

	public static IReadOnlyList<MonitorInfo> Monitors()
	{
		List<MonitorInfo> result = new List<MonitorInfo>();
		EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(nint handle, nint dc, ref RECT bounds, nint data)
		{
			MONITORINFOEX info = new MONITORINFOEX
			{
				cbSize = Marshal.SizeOf<MONITORINFOEX>(),
				szDevice = ""
			};
			if (GetMonitorInfoEx(handle, ref info))
			{
				result.Add(new MonitorInfo(handle, info.szDevice, info.rcMonitor, info.rcWork, (info.dwFlags & 1) != 0));
			}
			return true;
		}, IntPtr.Zero);
		if (result.Count == 0)
		{
			// 목록을 못 읽었으면 주 모니터 하나만
			nint primary = MonitorFromPoint(default, 1u);
			MONITORINFO mi = new MONITORINFO
			{
				cbSize = Marshal.SizeOf<MONITORINFO>()
			};
			GetMonitorInfo(primary, ref mi);
			result.Add(new MonitorInfo(primary, "Primary", mi.rcMonitor, mi.rcWork, IsPrimary: true));
		}
		return (from m in result
			orderby m.IsPrimary descending, m.Bounds.Left, m.Bounds.Top
			select m).ToList();
	}

	public static MonitorInfo MonitorAt(POINT point)
	{
		nint handle = MonitorFromPoint(point, 2u);
		IReadOnlyList<MonitorInfo> monitors = Monitors();
		return monitors.FirstOrDefault((MonitorInfo m) => m.Handle == handle) ?? monitors[0];
	}

	public static RECT VirtualScreenRect()
	{
		IReadOnlyList<MonitorInfo> source = Monitors();
		return new RECT
		{
			Left = source.Min((MonitorInfo m) => m.Bounds.Left),
			Top = source.Min((MonitorInfo m) => m.Bounds.Top),
			Right = source.Max((MonitorInfo m) => m.Bounds.Right),
			Bottom = source.Max((MonitorInfo m) => m.Bounds.Bottom)
		};
	}

	public static Rect ToDipRect(RECT pixels)
	{
		double scale = DipScale();
		return new Rect((double)pixels.Left / scale, (double)pixels.Top / scale, (double)(pixels.Right - pixels.Left) / scale, (double)(pixels.Bottom - pixels.Top) / scale);
	}

	public static bool Contains(RECT bounds, POINT point)
	{
		if (point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top)
		{
			return point.Y < bounds.Bottom;
		}
		return false;
	}

	public static bool IsAtMonitorEdge(POINT point, RECT bounds, Side side, int thickness = 2)
	{
		if (Contains(bounds, point) && thickness > 0)
		{
			if (side != Side.Left)
			{
				return point.X >= bounds.Right - thickness;
			}
			return point.X < bounds.Left + thickness;
		}
		return false;
	}

	public static bool TryGetEdgeProbe(POINT cursor, RECT bounds, Side side, IEnumerable<RECT> displays, out POINT probe, int thickness = 2, int seamThickness = 16)
	{
		probe = default;
		if (thickness <= 0 || bounds.Left >= bounds.Right || bounds.Top >= bounds.Bottom || cursor.Y < bounds.Top || cursor.Y >= bounds.Bottom)
		{
			return false;
		}
		bool atEdge = IsAtMonitorEdge(cursor, bounds, side, thickness);
		// 두 모니터가 맞닿은 경계: 마우스가 옆 모니터로 넘어가 버리므로 경계 양쪽 seamThickness 픽셀까지 가장자리로 침
		int seam = ((side == Side.Left) ? bounds.Left : bounds.Right);
		bool atSeam = seamThickness > 0 && cursor.X >= (long)seam - (long)seamThickness && cursor.X < (long)seam + (long)seamThickness && displays.Any((RECT other) => other.Left < other.Right && ((side == Side.Left) ? (other.Right == seam) : (other.Left == seam)) && cursor.Y >= other.Top && cursor.Y < other.Bottom && (Contains(bounds, cursor) || Contains(other, cursor)));
		if (!atEdge && !atSeam)
		{
			return false;
		}
		// 그 가장자리에 무엇이 보이는지 확인할 점 (모니터 맨 끝 픽셀)
		probe = new POINT
		{
			X = ((side == Side.Left) ? bounds.Left : (bounds.Right - 1)),
			Y = cursor.Y
		};
		return true;
	}

	[DllImport("user32.dll")]
	public static extern bool GetCursorInfo(ref CURSORINFO ci);

	[DllImport("user32.dll")]
	public static extern nint LoadCursor(nint hInst, nint id);

	public static bool LooksLikeDragCursor()
	{
		if (normalCursors == null)
		{
			normalCursors = new int[11]
			{
				32512, 32513, 32514, 32515, 32642, 32643, 32644, 32645, 32646, 32649,
				32650
			}.Select((int id) => LoadCursor(IntPtr.Zero, new IntPtr(id))).ToArray();
		}
		CURSORINFO ci = new CURSORINFO
		{
			cbSize = Marshal.SizeOf<CURSORINFO>()
		};
		if (!GetCursorInfo(ref ci) || (ci.flags & 1) == 0 || ci.hCursor == IntPtr.Zero)
		{
			return false;
		}
		return Array.IndexOf(normalCursors, ci.hCursor) < 0;
	}

	[DllImport("user32.dll")]
	public static extern nint WindowFromPoint(POINT p);

	[DllImport("user32.dll")]
	public static extern nint GetAncestor(nint h, uint flags);

	/// <summary>이 화면 위치에서 마우스가 닿는 창이 바탕화면인지.</summary>
	public static bool IsDesktopAt(POINT p)
	{
		nint hit = WindowFromPoint(p);
		if (hit == IntPtr.Zero)
		{
			return false;
		}
		nint topLevel = GetAncestor(hit, 2u);
		if (topLevel == IntPtr.Zero)
		{
			topLevel = hit;
		}
		StringBuilder className = new StringBuilder(32);
		GetClassName(topLevel, className, 32);
		string name = className.ToString();
		if (name == "Progman" || name == "WorkerW")
		{
			return true;
		}
		return false;
	}

	[DllImport("user32.dll")]
	private static extern bool IsIconic(nint hwnd);

	[DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
	private static extern int GetWindowCloaked(nint hwnd, uint attribute, out uint value, int size);

	[DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
	private static extern int GetWindowFrame(nint hwnd, uint attribute, out RECT value, int size);

	/// <summary>
	/// 이 화면 위치에 실제 바탕화면이 보이는지 (프로그램 창이 덮고 있지 않은지).
	/// 그 자리를 DesktopPop 창(판·가방)이 덮고 있으면 그 아래를 봐야 해서, 창 목록을 위에서부터 훑어 처음 걸리는 창으로 판단함
	/// (50ms 동안은 목록을 다시 읽지 않고 재사용).
	/// </summary>
	public static bool IsDesktopExposed(POINT point)
	{
		nint hit = WindowFromPoint(point);
		if (hit == IntPtr.Zero)
		{
			return false;
		}
		nint topLevel = GetAncestor(hit, 2u);
		if (topLevel == IntPtr.Zero)
		{
			topLevel = hit;
		}
		GetWindowThreadProcessId(topLevel, out var pid);
		if (pid != (uint)Environment.ProcessId)
		{
			StringBuilder className = new StringBuilder(64);
			GetClassName(topLevel, className, className.Capacity);
			string name = className.ToString();
			if (name == "Progman" || name == "WorkerW")
			{
				return true;
			}
			return false;
		}
		long tickCount = Environment.TickCount64;
		if (desktopProbeSnapshotAt == long.MinValue || tickCount - desktopProbeSnapshotAt >= 50)
		{
			desktopProbeWindows.Clear();
			EnumWindows((nint window, nint _) =>
			{
				if (!IsWindowVisible(window) || IsIconic(window))
				{
					return true;
				}
				GetWindowThreadProcessId(window, out var ownerPid);
				if (ownerPid == (uint)Environment.ProcessId)
				{
					return true;
				}
				if (GetWindowCloaked(window, 14u, out var cloaked, 4) == 0 && cloaked != 0) // 다른 가상 데스크톱 등에 숨은 창
				{
					return true;
				}
				if (IsOverlayWindow(window))
				{
					return true;
				}
				StringBuilder windowClass = new StringBuilder(64);
				GetClassName(window, windowClass, windowClass.Capacity);
				string windowClassName = windowClass.ToString();
				bool isDesktop = windowClassName == "Progman" || windowClassName == "WorkerW";
				// 그림자를 뺀 실제 창 테두리 (DWMWA_EXTENDED_FRAME_BOUNDS), 못 읽으면 창 크기
				RECT r = default;
				if ((isDesktop || GetWindowFrame(window, 9u, out r, Marshal.SizeOf<RECT>()) != 0 || r.Right <= r.Left || r.Bottom <= r.Top) && !GetWindowRect(window, out r))
				{
					return true;
				}
				if (r.Right <= r.Left || r.Bottom <= r.Top)
				{
					return true;
				}
				desktopProbeWindows.Add(new DesktopProbeWindow(r, isDesktop));
				return true;
			}, IntPtr.Zero);
			desktopProbeSnapshotAt = tickCount;
		}
		foreach (DesktopProbeWindow desktopProbeWindow in desktopProbeWindows)
		{
			if (Contains(desktopProbeWindow.Bounds, point))
			{
				return desktopProbeWindow.IsDesktop;
			}
		}
		return false;
	}

	public static bool IsDesktopForeground()
	{
		nint foregroundWindow = GetForegroundWindow();
		if (foregroundWindow == IntPtr.Zero)
		{
			return true;
		}
		StringBuilder className = new StringBuilder(32);
		GetClassName(foregroundWindow, className, 32);
		string name = className.ToString();
		if (name == "Progman" || name == "WorkerW")
		{
			return true;
		}
		return false;
	}

	/// <summary>화면 픽셀 → WPF 좌표(DIP) 배율 (예: 150% 배율이면 1.5).</summary>
	public static double DipScale()
	{
		RECT primary = PrimaryMonitorRect();
		double scale = (double)(primary.Right - primary.Left) / SystemParameters.PrimaryScreenWidth;
		if (!(scale > 0.0))
		{
			return 1.0;
		}
		return scale;
	}

	/// <summary>
	/// 화면 전체를 덮지만 실제로는 투명한 오버레이 창인지 (NVIDIA GeForce 오버레이, 게임·녹화 오버레이 등).
	/// 클릭이 통과하는 창(WS_EX_TRANSPARENT)이나, 활성화되지 않는 반투명 창(WS_EX_LAYERED + WS_EX_NOACTIVATE)은
	/// "바탕화면을 가린 창"으로 치지 않음. 안 그러면 패널이 열리자마자 닫혔다 열렸다를 반복함.
	/// </summary>
	public static bool IsOverlayWindow(nint hwnd)
	{
		long ex = ((IntPtr)GetWindowLongPtr(hwnd, -20)).ToInt64();
		const long WS_EX_TRANSPARENT = 0x20, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x8000000;
		return (ex & WS_EX_TRANSPARENT) != 0 || ((ex & WS_EX_LAYERED) != 0 && (ex & WS_EX_NOACTIVATE) != 0);
	}

	/// <summary>작업 표시줄·Alt+Tab 에 안 나오는 창으로 (WS_EX_TOOLWINDOW 켜고 WS_EX_APPWINDOW 끔).</summary>
	public static void MakeToolWindow(nint hwnd)
	{
		long exStyle = ((IntPtr)GetWindowLongPtr(hwnd, -20)).ToInt64();
		exStyle |= 0x80;
		exStyle &= -262145;
		SetWindowLongPtr(hwnd, -20, new IntPtr(exStyle));
	}

	public static void SetTopmost(nint hwnd)
	{
		SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 19u);
	}

	[DllImport("user32.dll")]
	public static extern bool IsZoomed(nint h);

	[DllImport("user32.dll")]
	public static extern nint MonitorFromWindow(nint h, uint flags);

	[DllImport("shell32.dll")]
	public static extern int SHQueryUserNotificationState(out int state);

	public static bool TryGetTaskbarRect(out RECT r)
	{
		r = default;
		nint taskbar = FindWindow("Shell_TrayWnd", null);
		if (taskbar != IntPtr.Zero && IsWindowVisible(taskbar))
		{
			return GetWindowRect(taskbar, out r);
		}
		return false;
	}

	public static bool TryGetTaskbarRect(MonitorInfo monitor, out RECT r)
	{
		// 주 모니터 작업 표시줄 + 보조 모니터 작업 표시줄들 중 이 모니터에 있는 것
		foreach (string cls in new string[2] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
		{
			nint taskbar = IntPtr.Zero;
			while ((taskbar = FindWindowEx(IntPtr.Zero, taskbar, cls, null)) != IntPtr.Zero)
			{
				if (IsWindowVisible(taskbar) && MonitorFromWindow(taskbar, 2u) == monitor.Handle && GetWindowRect(taskbar, out r))
				{
					return true;
				}
			}
		}
		r = default;
		return false;
	}

	public static bool InTaskbar(POINT p)
	{
		nint hit = WindowFromPoint(p);
		if (hit == IntPtr.Zero)
		{
			return false;
		}
		nint ancestor = GetAncestor(hit, 2u);
		StringBuilder className = new StringBuilder(64);
		GetClassName((ancestor == IntPtr.Zero) ? hit : ancestor, className, className.Capacity);
		string name = className.ToString();
		if (name == "Shell_TrayWnd" || name == "Shell_SecondaryTrayWnd")
		{
			return true;
		}
		return false;
	}

	/// <summary>
	/// 이 모니터에서 게임·영상 같은 전체화면 프로그램이 앞에 있는지 (그때는 새 파일 알림으로 가장자리를 열지 않음).
	/// 바탕화면·작업 표시줄·DesktopPop 자신이 앞에 있으면 아님.
	/// </summary>
	public static bool IsFullscreenAppOn(RECT mon)
	{
		nint foreground = GetForegroundWindow();
		if (foreground == IntPtr.Zero)
		{
			return false;
		}
		StringBuilder className = new StringBuilder(64);
		GetClassName(foreground, className, 64);
		if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
		{
			return false;
		}
		GetWindowThreadProcessId(foreground, out var pid);
		if (pid == (uint)Environment.ProcessId || !GetWindowRect(foreground, out var r))
		{
			return false;
		}
		if (r.Left >= mon.Right || r.Right <= mon.Left || r.Top >= mon.Bottom || r.Bottom <= mon.Top)
		{
			return false; // 다른 모니터에 있는 창
		}
		if (r.Left <= mon.Left && r.Top <= mon.Top && r.Right >= mon.Right && r.Bottom >= mon.Bottom)
		{
			return true; // 모니터를 꽉 채움
		}
		// Windows 가 "전체화면 앱 실행 중 / 발표 중"이라고 할 때는 모니터의 90% 이상만 덮어도 전체화면으로 침
		bool windowsSaysFullscreen = SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;
		if (!windowsSaysFullscreen)
		{
			return false;
		}
		long monitorArea = (long)(mon.Right - mon.Left) * (mon.Bottom - mon.Top);
		long overlapWidth = Math.Max(0, Math.Min(r.Right, mon.Right) - Math.Max(r.Left, mon.Left));
		long overlapHeight = Math.Max(0, Math.Min(r.Bottom, mon.Bottom) - Math.Max(r.Top, mon.Top));
		return monitorArea > 0 && overlapWidth * overlapHeight >= monitorArea * 9 / 10;
	}
}
