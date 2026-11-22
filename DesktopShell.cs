using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopPop;

/// <summary>진짜 바탕화면(탐색기) 아이콘 숨기기·보이기, 원래 아이콘 배치 읽기.</summary>
internal static class DesktopShell
{
	[DllImport("user32.dll", SetLastError = true)]
	private static extern nint SendMessageTimeout(nint hwnd, uint msg, nint w, nint l, uint flags, uint timeout, out nint result);

	public static nint FindProgman()
	{
		return Native.FindWindow("Progman", null);
	}

	private static nint FindDefView()
	{
		// 바탕화면 아이콘 목록을 가진 창: 보통 Progman 안, 배경 화면 슬라이드쇼 등을 쓰면 WorkerW 안
		nint inProgman = Native.FindWindowEx(FindProgman(), IntPtr.Zero, "SHELLDLL_DefView", null);
		if (inProgman != IntPtr.Zero)
		{
			return inProgman;
		}
		nint found = IntPtr.Zero;
		Native.EnumWindows((nint h, nint l) =>
		{
			StringBuilder className = new StringBuilder(32);
			Native.GetClassName(h, className, 32);
			if (className.ToString() != "WorkerW")
			{
				return true;
			}
			nint defView = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
			if (defView != IntPtr.Zero)
			{
				found = defView;
				return false;
			}
			return true;
		}, IntPtr.Zero);
		return found;
	}

	private static nint FindListView()
	{
		nint defView = FindDefView();
		if (defView != IntPtr.Zero)
		{
			return Native.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
		}
		return IntPtr.Zero;
	}

	public static bool? IconsVisible()
	{
		nint listView = FindListView();
		if (listView == IntPtr.Zero)
		{
			return null;
		}
		return Native.IsWindowVisible(listView);
	}

	public static void SetIconsVisible(bool visible)
	{
		bool? visibleNow = IconsVisible();
		if (visibleNow.HasValue && visibleNow != visible)
		{
			nint defView = FindDefView();
			if (defView != IntPtr.Zero)
			{
				// 바탕화면 우클릭 → 보기 → "바탕 화면 아이콘 표시" 와 같은 명령 (WM_COMMAND 0x7402). 탐색기가 멈춰 있어도 1초만 기다림
				SendMessageTimeout(defView, 273u, new IntPtr(29698), IntPtr.Zero, 2u, 1000u, out var _);
			}
		}
	}

	/// <summary>바탕화면 아이콘 하나의 위치. Path = 파일 전체 경로(휴지통 같은 건 "::{CLSID}"), Name = 화면에 보이는 이름.</summary>
	public readonly record struct IconPosition(string Path, string Name, Native.POINT Point);

	/// <summary>
	/// 원래 바탕화면 아이콘들의 화면 좌표 읽기 (처음 실행 때 왼쪽/오른쪽 배치를 그대로 가져오려고).
	/// 예전엔 탐색기 프로세스 메모리에 직접 쓰고 읽었는데(WriteProcessMemory), 백신이 악성코드로 오해하기 쉬워서
	/// Windows 공식 COM 방식(ShellWindows → IFolderView)으로 바꿈.
	/// </summary>
	public static List<IconPosition>? ReadIconPositions()
	{
		nint listView = FindListView();
		if (listView == IntPtr.Zero)
		{
			return null;
		}
		Native.GetWindowRect(listView, out var lvRect);
		object? shellWindows = null;
		object? dispatch = null;
		object? folderView = null;
		try
		{
			Type? type = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
			if (type == null)
			{
				return null;
			}
			shellWindows = Activator.CreateInstance(type);
			if (shellWindows is not IShellWindows sw)
			{
				return null;
			}
			object loc = 0; // CSIDL_DESKTOP
			object root = null!;
			dispatch = sw.FindWindowSW(ref loc, ref root, 8 /*SWC_DESKTOP*/, out _, 1 /*SWFO_NEEDDISPATCH*/);
			if (dispatch is not IServiceProvider sp)
			{
				return null;
			}
			Guid sid = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"); // SID_STopLevelBrowser
			Guid iid = typeof(IShellBrowser).GUID;
			sp.QueryService(ref sid, ref iid, out var browserPtr);
			if (browserPtr == IntPtr.Zero)
			{
				return null;
			}
			IShellBrowser browser;
			try
			{
				browser = (IShellBrowser)Marshal.GetObjectForIUnknown(browserPtr);
			}
			finally
			{
				Marshal.Release(browserPtr);
			}
			browser.QueryActiveShellView(out var view);
			folderView = view;
			Marshal.ReleaseComObject(browser);
			if (folderView is not IFolderView fv)
			{
				return null;
			}
			fv.ItemCount(2 /*SVGIO_ALLVIEW*/, out int count);
			var result = new List<IconPosition>(count);
			for (int i = 0; i < count; i++)
			{
				nint pidl = IntPtr.Zero;
				try
				{
					fv.Item(i, out pidl);
					if (pidl == IntPtr.Zero)
					{
						continue;
					}
					fv.GetItemPosition(pidl, out var pt);
					// 바탕화면은 셸 이름공간의 맨 위라서 바탕화면 기준 pidl = 절대 pidl
					string? path = NameOf(pidl, 0x80028000 /*SIGDN_DESKTOPABSOLUTEPARSING*/);
					string? name = NameOf(pidl, 0 /*SIGDN_NORMALDISPLAY*/);
					if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(name))
					{
						continue;
					}
					result.Add(new IconPosition(path ?? "", name ?? "", new Native.POINT
					{
						X = pt.X + lvRect.Left,
						Y = pt.Y + lvRect.Top
					}));
				}
				catch (Exception ex)
				{
					App.Log(ex);
				}
				finally
				{
					if (pidl != IntPtr.Zero)
					{
						Marshal.FreeCoTaskMem(pidl);
					}
				}
			}
			return result;
		}
		catch (Exception ex)
		{
			App.Log(ex);
			return null;
		}
		finally
		{
			if (folderView != null && Marshal.IsComObject(folderView))
			{
				Marshal.ReleaseComObject(folderView);
			}
			if (dispatch != null && Marshal.IsComObject(dispatch))
			{
				Marshal.ReleaseComObject(dispatch);
			}
			if (shellWindows != null && Marshal.IsComObject(shellWindows))
			{
				Marshal.ReleaseComObject(shellWindows);
			}
		}
	}

	private static string? NameOf(nint pidl, uint sigdn)
	{
		if (SHGetNameFromIDList(pidl, sigdn, out var p) != 0 || p == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			return Marshal.PtrToStringUni(p);
		}
		finally
		{
			Marshal.FreeCoTaskMem(p);
		}
	}

	[DllImport("shell32.dll")]
	private static extern int SHGetNameFromIDList(nint pidl, uint sigdnName, out nint ppszName);

	[ComImport]
	[Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
	[InterfaceType(ComInterfaceType.InterfaceIsDual)]
	private interface IShellWindows
	{
		void _Count();
		void _Item();
		void _NewEnum();
		void _Register();
		void _RegisterPending();
		void _Revoke();
		void _OnNavigate();
		void _OnActivated();

		[return: MarshalAs(UnmanagedType.IDispatch)]
		object FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object pvarloc, [MarshalAs(UnmanagedType.Struct)] ref object pvarlocRoot, int swClass, out int pHWND, int swfwOptions);
	}

	[ComImport]
	[Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IServiceProvider
	{
		void QueryService(ref Guid guidService, ref Guid riid, out nint ppvObject);
	}

	[ComImport]
	[Guid("000214E2-0000-0000-C000-000000000046")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IShellBrowser
	{
		void _GetWindow();
		void _ContextSensitiveHelp();
		void _InsertMenusSB();
		void _SetMenuSB();
		void _RemoveMenusSB();
		void _SetStatusTextSB();
		void _EnableModelessSB();
		void _TranslateAcceleratorSB();
		void _BrowseObject();
		void _GetViewStateStream();
		void _GetControlWindow();
		void _SendControlMsg();
		void QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object ppshv);
	}

	[ComImport]
	[Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IFolderView
	{
		void _GetCurrentViewMode();
		void _SetCurrentViewMode();
		void _GetFolder();
		void Item(int iItemIndex, out nint ppidl);
		void ItemCount(uint uFlags, out int pcItems);
		void _Items();
		void _GetSelectionMarkedItem();
		void _GetFocusedItem();
		void GetItemPosition(nint pidl, out Native.POINT ppt);
	}
}
