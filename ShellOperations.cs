using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace DesktopPop;

/// <summary>열기·속성·파일 위치 열기를 별도 도우미 프로세스에서 실행 (셸 확장이 고장 나도 DesktopPop 이 튕기지 않게).</summary>
internal static class ShellOperations
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct ShellExecuteInfo
	{
		public int cbSize;

		public uint fMask;

		public nint hwnd;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? lpVerb;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? lpFile;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? lpParameters;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? lpDirectory;

		public int nShow;

		public nint hInstApp;

		public nint lpIDList;

		[MarshalAs(UnmanagedType.LPWStr)]
		public string? lpClass;

		public nint hkeyClass;

		public uint dwHotKey;

		public nint hIconOrMonitor;

		public nint hProcess;
	}

	private delegate bool EnumWindowsProc(nint hwnd, nint parameter);

	private const string HelperFlag = "--shell-action";

	[DllImport("ole32.dll")]
	private static extern int OleInitialize(nint reserved);

	[DllImport("ole32.dll")]
	private static extern void OleUninitialize();

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, out uint attributesOut);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ShellExecuteEx(ref ShellExecuteInfo info);

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SHGetPathFromIDListEx(nint pidl, StringBuilder path, uint length, uint flags);

	[DllImport("shell32.dll")]
	private static extern int SHOpenFolderAndSelectItems(nint pidlFolder, uint count, nint children, uint flags);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint hwnd);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

	internal static void Start(string action, string path)
	{
		if (!ValidAction(action))
		{
			throw new ArgumentException("지원하지 않는 셸 작업이에요.", "action");
		}
		// 미리 띄워 둔 도우미가 있으면 그쪽에 맡김 (새 프로세스를 띄우는 시간만큼 빨라짐)
		if (NativeShellMenu.TryRunAction(action, path))
		{
			return;
		}
		string exe = Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 찾지 못했어요.");
		ProcessStartInfo processStartInfo = new ProcessStartInfo(exe)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = AppContext.BaseDirectory
		};
		if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
		{
			processStartInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, (Assembly.GetEntryAssembly()?.GetName().Name ?? throw new InvalidOperationException("앱 경로를 찾지 못했어요.")) + ".dll"));
		}
		processStartInfo.ArgumentList.Add("--shell-action");
		processStartInfo.ArgumentList.Add(action);
		processStartInfo.ArgumentList.Add(path);
		using (Process.Start(processStartInfo) ?? throw new InvalidOperationException("셸 작업을 시작하지 못했어요."))
		{
		}
	}

	private static bool ValidAction(string action)
	{
		switch (action)
		{
		case "open":
		case "properties":
		case "locate":
			return true;
		default:
			return false;
		}
	}

	internal static bool TryRunHelper(string[] args)
	{
		if (args.Length == 0 || args[0] != "--shell-action")
		{
			return false;
		}
		if (args.Length != 3 || !ValidAction(args[1]) || string.IsNullOrWhiteSpace(args[2]))
		{
			return true;
		}
		if (OleInitialize(IntPtr.Zero) < 0)
		{
			return true;
		}
		Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
		bool started = false;
		bool sawWindow = false;
		DateTime completedAt = DateTime.MaxValue;
		DateTime lastWindowAt = DateTime.MinValue;
		DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromMilliseconds(250L)
		};
		timer.Tick += (object? _, EventArgs _) =>
		{
			if (started)
			{
				DateTime utcNow = DateTime.UtcNow;
				if (HasVisibleWindow())
				{
					sawWindow = true;
					lastWindowAt = utcNow;
				}
				else
				{
					double waitIfNoWindowSec = ((args[1] == "properties") ? 15 : 3); // 속성 창은 늦게 뜰 때가 있음
					if ((sawWindow && (utcNow - lastWindowAt).TotalMilliseconds >= 750.0) || (!sawWindow && (utcNow - completedAt).TotalSeconds >= waitIfNoWindowSec))
					{
						timer.Stop();
						dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
					}
				}
			}
		};
		dispatcher.InvokeAsync(() =>
		{
			try
			{
				ExecuteExact(args[1], args[2]);
			}
			catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
			{
			}
			catch (Exception ex2)
			{
				App.Log(ex2);
				MessageBox.Show("작업을 완료하지 못했어요.\n" + args[2] + "\n\n" + ex2.Message, "DesktopPop");
			}
			finally
			{
				started = true;
				completedAt = DateTime.UtcNow;
			}
		});
		timer.Start();
		try
		{
			Dispatcher.Run();
		}
		finally
		{
			timer.Stop();
			OleUninitialize();
		}
		return true;
	}

	/// <summary>
	/// 도우미 안에서 열기·속성·파일 위치 열기를 실행. 실행했으면 true (도우미는 그 창이 닫힐 때까지 기다려야 함).
	/// 사용자가 취소한 경우(UAC 등)는 조용히 넘어감.
	/// </summary>
	internal static bool RunAction(string action, string path)
	{
		if (!ValidAction(action) || string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		try
		{
			ExecuteExact(action, path);
			return true;
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			return true;
		}
		catch (Exception ex2)
		{
			App.Log(ex2);
			MessageBox.Show("작업을 완료하지 못했어요.\n" + path + "\n\n" + ex2.Message, "DesktopPop");
			return false;
		}
	}

	private static bool HasVisibleWindow()
	{
		bool found = false;
		EnumWindows((nint hwnd, nint _) =>
		{
			if (!IsWindowVisible(hwnd))
			{
				return true;
			}
			GetWindowThreadProcessId(hwnd, out var processId);
			if (processId != Environment.ProcessId)
			{
				return true;
			}
			found = true;
			return false;
		}, IntPtr.Zero);
		return found;
	}

	internal static nint ParseExactItem(string path)
	{
		bool isVirtual = path.StartsWith("::", StringComparison.Ordinal);
		if (!isVirtual && !Path.IsPathFullyQualified(path))
		{
			throw new ArgumentException("전체 파일 경로가 필요해요.", "path");
		}
		int hr = SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0u, out var _);
		if (hr < 0)
		{
			Marshal.ThrowExceptionForHR(hr);
		}
		if (pidl == IntPtr.Zero)
		{
			throw new IOException("파일 정보를 읽지 못했어요.");
		}
		try
		{
			if (!isVirtual)
			{
				// Windows 가 찾은 항목이 요청한 경로와 정확히 같은지 다시 확인 (다른 파일을 열지 않게)
				StringBuilder resolved = new StringBuilder(32768);
				if (!SHGetPathFromIDListEx(pidl, resolved, (uint)resolved.Capacity, 0u) || !SameFilePath(path, resolved.ToString()))
				{
					throw new IOException("요청한 항목과 Windows가 찾은 항목이 달라 실행을 중단했어요.");
				}
			}
			return pidl;
		}
		catch
		{
			Marshal.FreeCoTaskMem(pidl);
			throw;
		}
	}

	private static bool SameFilePath(string left, string right)
	{
		return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
	}

	private static void ExecuteExact(string action, string path)
	{
		nint pidl = ParseExactItem(path);
		try
		{
			if (action == "locate")
			{
				Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(pidl, 0u, IntPtr.Zero, 0u));
				return;
			}
			ShellExecuteInfo info = new ShellExecuteInfo
			{
				cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
				fMask = (uint)(((action == "properties") ? 12 : 4) | 0x4000 | 0x400 | 0x100),
				lpVerb = ((action == "properties") ? "properties" : null),
				lpIDList = pidl,
				nShow = 1
			};
			if (ShellExecuteEx(ref info))
			{
				return;
			}
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}
		finally
		{
			Marshal.FreeCoTaskMem(pidl);
		}
	}
}
