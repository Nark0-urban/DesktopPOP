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
		string? text = Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 찾지 못했어요.");
		ProcessStartInfo processStartInfo = new ProcessStartInfo(text)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = AppContext.BaseDirectory
		};
		if (string.Equals(Path.GetFileNameWithoutExtension(text), "dotnet", StringComparison.OrdinalIgnoreCase))
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
					double num = ((args[1] == "properties") ? 15 : 3);
					if ((sawWindow && (utcNow - lastWindowAt).TotalMilliseconds >= 750.0) || (!sawWindow && (utcNow - completedAt).TotalSeconds >= num))
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
		bool flag = path.StartsWith("::", StringComparison.Ordinal);
		if (!flag && !Path.IsPathFullyQualified(path))
		{
			throw new ArgumentException("전체 파일 경로가 필요해요.", "path");
		}
		int num = SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0u, out var _);
		if (num < 0)
		{
			Marshal.ThrowExceptionForHR(num);
		}
		if (pidl == IntPtr.Zero)
		{
			throw new IOException("파일 정보를 읽지 못했어요.");
		}
		try
		{
			if (!flag)
			{
				StringBuilder stringBuilder = new StringBuilder(32768);
				if (!SHGetPathFromIDListEx(pidl, stringBuilder, (uint)stringBuilder.Capacity, 0u) || !SameFilePath(path, stringBuilder.ToString()))
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
		nint num = ParseExactItem(path);
		try
		{
			if (action == "locate")
			{
				Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(num, 0u, IntPtr.Zero, 0u));
				return;
			}
			ShellExecuteInfo info = new ShellExecuteInfo
			{
				cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
				fMask = (uint)(((action == "properties") ? 12 : 4) | 0x4000 | 0x400 | 0x100),
				lpVerb = ((action == "properties") ? "properties" : null),
				lpIDList = num,
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
			Marshal.FreeCoTaskMem(num);
		}
	}
}
