using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace DesktopPop;

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
			nint num = MonitorFromPoint(default, 1u);
			MONITORINFO mi = new MONITORINFO
			{
				cbSize = Marshal.SizeOf<MONITORINFO>()
			};
			GetMonitorInfo(num, ref mi);
			result.Add(new MonitorInfo(num, "Primary", mi.rcMonitor, mi.rcWork, IsPrimary: true));
		}
		return (from m in result
			orderby m.IsPrimary descending, m.Bounds.Left, m.Bounds.Top
			select m).ToList();
	}

	public static MonitorInfo MonitorAt(POINT point)
	{
		nint handle = MonitorFromPoint(point, 2u);
		IReadOnlyList<MonitorInfo> readOnlyList = Monitors();
		return readOnlyList.FirstOrDefault((MonitorInfo m) => m.Handle == handle) ?? readOnlyList[0];
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
		double num = DipScale();
		return new Rect((double)pixels.Left / num, (double)pixels.Top / num, (double)(pixels.Right - pixels.Left) / num, (double)(pixels.Bottom - pixels.Top) / num);
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
		bool flag = IsAtMonitorEdge(cursor, bounds, side, thickness);
		int seam = ((side == Side.Left) ? bounds.Left : bounds.Right);
		bool flag2 = seamThickness > 0 && cursor.X >= (long)seam - (long)seamThickness && cursor.X < (long)seam + (long)seamThickness && displays.Any((RECT other) => other.Left < other.Right && ((side == Side.Left) ? (other.Right == seam) : (other.Left == seam)) && cursor.Y >= other.Top && cursor.Y < other.Bottom && (Contains(bounds, cursor) || Contains(other, cursor)));
		if (!flag && !flag2)
		{
			return false;
		}
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

	public static bool IsDesktopAt(POINT p)
	{
		nint num = WindowFromPoint(p);
		if (num == IntPtr.Zero)
		{
			return false;
		}
		nint num2 = GetAncestor(num, 2u);
		if (num2 == IntPtr.Zero)
		{
			num2 = num;
		}
		StringBuilder stringBuilder = new StringBuilder(32);
		GetClassName(num2, stringBuilder, 32);
		string text = stringBuilder.ToString();
		if (text == "Progman" || text == "WorkerW")
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

	public static bool IsDesktopExposed(POINT point)
	{
		nint num = WindowFromPoint(point);
		if (num == IntPtr.Zero)
		{
			return false;
		}
		nint num2 = GetAncestor(num, 2u);
		if (num2 == IntPtr.Zero)
		{
			num2 = num;
		}
		GetWindowThreadProcessId(num2, out var pid);
		if (pid != (uint)Environment.ProcessId)
		{
			StringBuilder stringBuilder = new StringBuilder(64);
			GetClassName(num2, stringBuilder, stringBuilder.Capacity);
			string text = stringBuilder.ToString();
			if (text == "Progman" || text == "WorkerW")
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
				GetWindowThreadProcessId(window, out var pid2);
				if (pid2 == (uint)Environment.ProcessId)
				{
					return true;
				}
				if (GetWindowCloaked(window, 14u, out var value, 4) == 0 && value != 0)
				{
					return true;
				}
				if (IsOverlayWindow(window))
				{
					return true;
				}
				StringBuilder stringBuilder2 = new StringBuilder(64);
				GetClassName(window, stringBuilder2, stringBuilder2.Capacity);
				string text2 = stringBuilder2.ToString();
				bool flag = ((text2 == "Progman" || text2 == "WorkerW") ? true : false);
				bool flag2 = flag;
				RECT r = default;
				if ((flag2 || GetWindowFrame(window, 9u, out r, Marshal.SizeOf<RECT>()) != 0 || r.Right <= r.Left || r.Bottom <= r.Top) && !GetWindowRect(window, out r))
				{
					return true;
				}
				if (r.Right <= r.Left || r.Bottom <= r.Top)
				{
					return true;
				}
				desktopProbeWindows.Add(new DesktopProbeWindow(r, flag2));
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
		StringBuilder stringBuilder = new StringBuilder(32);
		GetClassName(foregroundWindow, stringBuilder, 32);
		string text = stringBuilder.ToString();
		if (text == "Progman" || text == "WorkerW")
		{
			return true;
		}
		return false;
	}

	public static double DipScale()
	{
		RECT rECT = PrimaryMonitorRect();
		double num = (double)(rECT.Right - rECT.Left) / SystemParameters.PrimaryScreenWidth;
		if (!(num > 0.0))
		{
			return 1.0;
		}
		return num;
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

	public static void MakeToolWindow(nint hwnd)
	{
		long num = ((IntPtr)GetWindowLongPtr(hwnd, -20)).ToInt64();
		num |= 0x80;
		num &= -262145;
		SetWindowLongPtr(hwnd, -20, new IntPtr(num));
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
		nint num = FindWindow("Shell_TrayWnd", null);
		if (num != IntPtr.Zero && IsWindowVisible(num))
		{
			return GetWindowRect(num, out r);
		}
		return false;
	}

	public static bool TryGetTaskbarRect(MonitorInfo monitor, out RECT r)
	{
		string[] array = new string[2] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };
		foreach (string cls in array)
		{
			nint num = IntPtr.Zero;
			while ((num = FindWindowEx(IntPtr.Zero, num, cls, null)) != IntPtr.Zero)
			{
				if (IsWindowVisible(num) && MonitorFromWindow(num, 2u) == monitor.Handle && GetWindowRect(num, out r))
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
		nint num = WindowFromPoint(p);
		if (num == IntPtr.Zero)
		{
			return false;
		}
		nint ancestor = GetAncestor(num, 2u);
		StringBuilder stringBuilder = new StringBuilder(64);
		GetClassName((ancestor == IntPtr.Zero) ? num : ancestor, stringBuilder, stringBuilder.Capacity);
		string text = stringBuilder.ToString();
		if (text == "Shell_TrayWnd" || text == "Shell_SecondaryTrayWnd")
		{
			return true;
		}
		return false;
	}

	public static bool ShouldBlock(RECT mon, bool blockFullscreen, bool blockMaximized)
	{
		bool flag = blockFullscreen && SHQueryUserNotificationState(out var state) == 0 && (state == 2 || state == 3 || state == 4);
		nint foregroundWindow = GetForegroundWindow();
		if (foregroundWindow == IntPtr.Zero)
		{
			return false;
		}
		StringBuilder stringBuilder = new StringBuilder(64);
		GetClassName(foregroundWindow, stringBuilder, 64);
		bool flag2;
		switch (stringBuilder.ToString())
		{
		case "Progman":
		case "WorkerW":
		case "Shell_TrayWnd":
		case "Shell_SecondaryTrayWnd":
			flag2 = true;
			break;
		default:
			flag2 = false;
			break;
		}
		if (flag2)
		{
			return false;
		}
		GetWindowThreadProcessId(foregroundWindow, out var pid);
		if (pid == (uint)Environment.ProcessId)
		{
			return false;
		}
		if (!GetWindowRect(foregroundWindow, out var r))
		{
			return false;
		}
		if (r.Left >= mon.Right || r.Right <= mon.Left || r.Top >= mon.Bottom || r.Bottom <= mon.Top)
		{
			return false;
		}
		if (blockFullscreen)
		{
			if (r.Left <= mon.Left && r.Top <= mon.Top && r.Right >= mon.Right && r.Bottom >= mon.Bottom)
			{
				return true;
			}
			if (flag)
			{
				long num = (long)(mon.Right - mon.Left) * (long)(mon.Bottom - mon.Top);
				long num2 = Math.Max(0, Math.Min(r.Right, mon.Right) - Math.Max(r.Left, mon.Left));
				long num3 = Math.Max(0, Math.Min(r.Bottom, mon.Bottom) - Math.Max(r.Top, mon.Top));
				if (num > 0 && num2 * num3 >= num * 9 / 10)
				{
					return true;
				}
			}
		}
		nint num4 = MonitorFromPoint(new POINT
		{
			X = mon.Left + (mon.Right - mon.Left) / 2,
			Y = mon.Top + (mon.Bottom - mon.Top) / 2
		}, 2u);
		if (blockMaximized && MonitorFromWindow(foregroundWindow, 2u) == num4)
		{
			return IsZoomed(foregroundWindow);
		}
		return false;
	}
}
