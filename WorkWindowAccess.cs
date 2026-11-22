using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPop;

/// <summary>맨 앞 창 알아내기, 접을 때 하던 창으로 돌아가기, 가방을 프로그램 창들 뒤로 내리기.</summary>
internal static class WorkWindowAccess
{
	[DllImport("user32.dll")]
	private static extern bool IsWindow(nint window);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(nint window);

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint window);

	[DllImport("user32.dll")]
	private static extern nint GetTopWindow(nint parent);

	[DllImport("user32.dll")]
	private static extern nint GetWindow(nint window, uint command);

	[DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
	private static extern int GetWindowCloaked(nint window, uint attribute, out uint value, int size);

	[DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
	private static extern int GetWindowFrame(nint window, uint attribute, out Native.RECT value, int size);

	public static ForegroundSnapshot Foreground()
	{
		return Describe(Native.GetForegroundWindow());
	}

	private static ForegroundSnapshot Describe(nint window)
	{
		if (window == IntPtr.Zero || !IsWindow(window))
		{
			return default;
		}
		nint ancestor = Native.GetAncestor(window, 2u);
		if (ancestor != IntPtr.Zero)
		{
			window = ancestor;
		}
		Native.GetWindowThreadProcessId(window, out var pid);
		if (pid == 0)
		{
			return default;
		}
		if (pid == (uint)Environment.ProcessId)
		{
			return new ForegroundSnapshot(window, pid, ForegroundKind.Own);
		}
		StringBuilder className = new StringBuilder(128);
		Native.GetClassName(window, className, className.Capacity);
		ForegroundKind foregroundKind;
		switch (className.ToString())
		{
		case "Progman":
		case "WorkerW":
			foregroundKind = ForegroundKind.Desktop;
			break;
		case "Shell_TrayWnd":
		case "Shell_SecondaryTrayWnd":
		case "NotifyIconOverflowWindow":
			foregroundKind = ForegroundKind.Taskbar;
			break;
		default:
			foregroundKind = ForegroundKind.Work;
			break;
		}
		ForegroundKind foregroundKind2 = foregroundKind;
		if (foregroundKind2 == ForegroundKind.Work && (!Native.IsWindowVisible(window) || IsIconic(window) || IsCloaked(window)))
		{
			return default;
		}
		return new ForegroundSnapshot(window, pid, foregroundKind2);
	}

	private static bool ValidWork(ForegroundSnapshot window)
	{
		if (window.Kind == ForegroundKind.Work)
		{
			return Describe(window.Handle) == window;
		}
		return false;
	}

	private static bool IsCloaked(nint window)
	{
		if (GetWindowCloaked(window, 14u, out var value, 4) == 0)
		{
			return value != 0;
		}
		return false;
	}

	public static void Apply(PanelClosePlan plan, IEnumerable<BagWindowPlacement> bagWindows)
	{
		if (ValidWork(plan.Restore) && PanelWorkReturn.MayRestore(plan, Foreground()))
		{
			SetForegroundWindow(plan.Restore.Handle);
		}
		LowerBags(bagWindows);
	}

	private static bool Intersects(Native.RECT first, Native.RECT second)
	{
		if (first.Left < second.Right && first.Right > second.Left && first.Top < second.Bottom)
		{
			return first.Bottom > second.Top;
		}
		return false;
	}

	internal static ForegroundSnapshot LowestWorkOnMonitor(IEnumerable<WorkStackEntry> topToBottom, Native.RECT monitor)
	{
		ForegroundSnapshot result = default;
		foreach (WorkStackEntry item in topToBottom)
		{
			if (Intersects(item.Bounds, monitor))
			{
				if (item.Window.Kind == ForegroundKind.Desktop)
				{
					break;
				}
				if (item.Window.Kind == ForegroundKind.Work && !item.Topmost)
				{
					result = item.Window;
				}
			}
		}
		return result;
	}

	internal static IReadOnlyList<WorkStackEntry> ReadStack()
	{
		List<WorkStackEntry> list = new List<WorkStackEntry>();
		// 맨 위 창부터 아래로 (GW_HWNDNEXT), 같은 창이 다시 나오거나 4096 개를 넘으면 멈춤
		HashSet<nint> seen = new HashSet<nint>();
		nint current = GetTopWindow(IntPtr.Zero);
		int count = 0;
		while (current != IntPtr.Zero && count < 4096 && seen.Add(current))
		{
			nint next = GetWindow(current, 2u);
			if (Native.IsWindowVisible(current) && !IsIconic(current) && !IsCloaked(current) && !Native.IsOverlayWindow(current))
			{
				ForegroundSnapshot described = Describe(current);
				ForegroundKind kind = described.Kind;
				if ((uint)(kind - 1) <= 1u)
				{
					Native.RECT r = default;
					if (described.Kind == ForegroundKind.Desktop || GetWindowFrame(current, 9u, out r, Marshal.SizeOf<Native.RECT>()) != 0 || r.Right <= r.Left || r.Bottom <= r.Top)
					{
						Native.GetWindowRect(current, out r);
					}
					if (r.Right > r.Left && r.Bottom > r.Top)
					{
						list.Add(new WorkStackEntry(described, r, (((IntPtr)Native.GetWindowLongPtr(current, -20)).ToInt64() & 8) != 0));
					}
				}
			}
			current = next;
			count++;
		}
		return list;
	}

	public static void LowerBags(IEnumerable<BagWindowPlacement> bagWindows)
	{
		IReadOnlyList<WorkStackEntry> topToBottom = ReadStack();
		foreach (BagWindowPlacement bagWindow in bagWindows)
		{
			if (bagWindow.Handle != IntPtr.Zero && IsWindow(bagWindow.Handle))
			{
				Native.SetWindowPos(bagWindow.Handle, new IntPtr(-2), 0, 0, 0, 0, 531u);
				ForegroundSnapshot window = LowestWorkOnMonitor(topToBottom, Native.MonitorAt(bagWindow.Center).Bounds);
				if (ValidWork(window) && (((IntPtr)Native.GetWindowLongPtr(window.Handle, -20)).ToInt64() & 8) == 0L)
				{
					Native.SetWindowPos(bagWindow.Handle, window.Handle, 0, 0, 0, 0, 531u);
				}
			}
		}
	}
}
