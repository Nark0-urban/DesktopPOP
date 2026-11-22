using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DesktopPop;

/// <summary>빈 바탕화면에서 마우스로 끌어 판·가방 아이콘을 여러 개 고르는 선택 네모 (마우스 감시 훅).</summary>
internal static class DesktopBand
{
	private readonly record struct Snap(Native.POINT A, Native.POINT B, ISelHost? BaseHost, List<string> BaseKeys);

	private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

	private static BandOverlay? overlay;

	private static bool buttonDown;

	private static bool down;

	private static bool active;

	private static Native.POINT start;

	private static Native.POINT cur;

	private static Action? onClick;

	private static ISelHost? baseHost;

	private static List<string> baseKeys = new List<string>();

	private static Snap? finishSnap;

	private static bool renderQueued;

	private static bool overlayShown;

	private static nint hook;

	private static LowLevelMouseProc? proc;

	private static Dispatcher? dispatcher;

	public static bool IsActive => down;

	private static bool KeyDown(int vk)
	{
		return (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
	}

	private static bool AddKeys()
	{
		if (!KeyDown(17))
		{
			return KeyDown(16);
		}
		return true;
	}

	public static void Begin(Native.POINT p, bool addToSelection, Action? clickAction)
	{
		if (!((hook != IntPtr.Zero) ? buttonDown : KeyDown(1)))
		{
			clickAction?.Invoke();
		}
		else if (!down)
		{
			Start(p, addToSelection, clickAction);
		}
	}

	public static void End()
	{
		buttonDown = false;
		if (down)
		{
			OnUp();
		}
	}

	private static void Start(Native.POINT p, bool add, Action? clickAction)
	{
		down = true;
		active = false;
		start = (cur = p);
		onClick = clickAction;
		baseHost = (add ? Sel.Host : null);
		baseKeys = ((baseHost != null) ? Sel.Ordered(baseHost) : new List<string>());
	}

	private static void OnMove(Native.POINT p)
	{
		if (down)
		{
			cur = p;
			if (active || Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) >= 6)
			{
				active = true;
				QueueRender();
			}
		}
	}

	private static void OnUp()
	{
		down = false;
		if (active)
		{
			finishSnap = new Snap(start, cur, baseHost, baseKeys);
			active = false;
			QueueRender();
		}
		else
		{
			Action click = onClick;
			dispatcher?.InvokeAsync(() =>
			{
				click?.Invoke();
			}, DispatcherPriority.Input);
		}
	}

	private static void QueueRender()
	{
		if (!renderQueued && dispatcher != null)
		{
			renderQueued = true;
			dispatcher.InvokeAsync(Render, DispatcherPriority.Input);
		}
	}

	private static void Render()
	{
		renderQueued = false;
		try
		{
			Snap? snap = finishSnap;
			if (snap.HasValue)
			{
				Snap current = snap.GetValueOrDefault();
				finishSnap = null;
				ApplySelection(current);
			}
			if (down && active)
			{
				if (overlay == null)
				{
					overlay = new BandOverlay();
				}
				if (!overlayShown)
				{
					overlay.ShowBand();
					overlayShown = true;
				}
				overlay.SetBand(start, cur);
				ApplySelection(new Snap(start, cur, baseHost, baseKeys));
			}
			else if (overlayShown)
			{
				overlay?.HideBand();
				overlayShown = false;
			}
		}
		catch (Exception ex)
		{
			App.Log(ex);
		}
	}

	private static void ApplySelection(Snap s)
	{
		Rect band = new Rect(new Point(s.A.X, s.A.Y), new Point(s.B.X, s.B.Y));
		ISelHost selHost = null;
		List<string> list = new List<string>();
		foreach (ISelHost host in App.Instance.SelHosts())
		{
			if (!host.BandSelectable)
			{
				continue;
			}
			List<string> inThisHost = host.OrderedKeys.Where((string k) =>
			{
				Border border = host.TileOf(k);
				if (border != null)
				{
					Rect? rect = ScreenRect(border);
					if (rect.HasValue)
					{
						return rect.GetValueOrDefault().IntersectsWith(band);
					}
				}
				return false;
			}).ToList();
			if (inThisHost.Count > list.Count)
			{
				selHost = host;
				list = inThisHost;
			}
		}
		if (selHost != null)
		{
			ISelHost h = selHost;
			IEnumerable<string> ks;
			if (selHost != s.BaseHost)
			{
				IEnumerable<string> enumerable = list;
				ks = enumerable;
			}
			else
			{
				ks = s.BaseKeys.Concat(list);
			}
			Sel.Set(h, ks);
		}
		else if (s.BaseHost != null)
		{
			Sel.Set(s.BaseHost, s.BaseKeys);
		}
		else
		{
			Sel.Clear();
		}
	}

	private static Rect? ScreenRect(FrameworkElement el)
	{
		if (!el.IsVisible || PresentationSource.FromVisual(el) == null)
		{
			return null;
		}
		try
		{
			Point point = el.PointToScreen(new Point(0.0, 0.0));
			Point point2 = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
			return new Rect(point, point2);
		}
		catch
		{
			return null;
		}
	}

	[DllImport("user32.dll")]
	private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc fn, nint hMod, uint threadId);

	[DllImport("user32.dll")]
	private static extern bool UnhookWindowsHookEx(nint hhk);

	[DllImport("user32.dll")]
	private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
	private static extern nint GetModuleHandle(string? name);

	public static void Install()
	{
		if (hook == IntPtr.Zero)
		{
			dispatcher = Dispatcher.CurrentDispatcher;
			proc = HookProc;
			hook = SetWindowsHookEx(14, proc, GetModuleHandle(null), 0u);
			if (hook == IntPtr.Zero)
			{
				App.Log($"마우스 감시 설치 실패 ({Marshal.GetLastWin32Error()})");
			}
			buttonDown = KeyDown(1);
		}
	}

	public static void Uninstall()
	{
		if (hook != IntPtr.Zero)
		{
			UnhookWindowsHookEx(hook);
		}
		hook = IntPtr.Zero;
	}

	private static nint HookProc(int nCode, nint wParam, nint lParam)
	{
		if (nCode >= 0)
		{
			try
			{
				switch (((IntPtr)wParam).ToInt32())
				{
				case 513:
				{
					Native.POINT cursor = Marshal.PtrToStructure<Native.POINT>(lParam); // MSLLHOOKSTRUCT 맨 앞이 화면 좌표
					App instance = App.Instance;
					bool dismissed = instance.ObserveGlobalPointerInput(cursor);
					buttonDown = true;
					if (!dismissed && !down && instance.SelHosts().Any((ISelHost h) => h.BandSelectable) && Native.IsDesktopAt(cursor))
					{
						Start(cursor, AddKeys(), () =>
						{
							Sel.Clear();
						});
					}
					break;
				}
				case 512:
					if (down)
					{
						OnMove(Marshal.PtrToStructure<Native.POINT>(lParam));
					}
					break;
				case 514:
					buttonDown = false;
					if (down)
					{
						OnUp();
					}
					break;
				case 516:
				case 519:
				case 522:
				case 523:
				case 526:
					if (App.Instance.AllPanelsOpen)
					{
						App.Instance.ObserveGlobalPointerInput(Marshal.PtrToStructure<Native.POINT>(lParam));
					}
					break;
				case 515:
				case 517:
				case 518:
				case 520:
				case 521:
				case 524:
				case 525:
					break;
				}
			}
			catch
			{
			}
		}
		return CallNextHookEx(hook, nCode, wParam, lParam);
	}
}
