using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace DesktopPop;

/// <summary>
/// 원래 바탕화면에서 아이콘 우클릭할 때 나오는 Windows 메뉴를 그대로 띄움
/// (열기, 연결 프로그램, 압축 풀기·압축하기, 보내기, 공유, 속성 …). DesktopPop 전용 항목은 맨 위에 붙음.
///
/// 이 메뉴를 만들 때 Windows 가 다른 프로그램들의 메뉴 확장(압축 프로그램, 클라우드, 백신 등)을 불러오는데,
/// 그중 하나가 고장 나 있으면 그걸 불러온 프로그램째로 튕겨 꺼짐 (1.1.3 에서 메뉴를 뺀 이유).
/// 그래서 메뉴는 따로 띄운 작은 도우미 프로세스(DesktopPop.exe --shell-menu)에서만 띄우고,
/// 도우미가 튕기면 DesktopPop 은 그대로 둔 채 간단 메뉴를 대신 띄움.
/// </summary>
internal static class NativeShellMenu
{
	[ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IShellFolder
	{
		[PreserveSig] int ParseDisplayName(nint hwnd, nint pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, nint eaten, out nint pidl, nint attrs);
		[PreserveSig] int EnumObjects(nint hwnd, int flags, out nint enumList);
		[PreserveSig] int BindToObject(nint pidl, nint pbc, ref Guid riid, out nint ppv);
		[PreserveSig] int BindToStorage(nint pidl, nint pbc, ref Guid riid, out nint ppv);
		[PreserveSig] int CompareIDs(nint lParam, nint pidl1, nint pidl2);
		[PreserveSig] int CreateViewObject(nint hwndOwner, ref Guid riid, out nint ppv);
		[PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] nint[] apidl, ref uint inOut);
		[PreserveSig] int GetUIObjectOf(nint hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] nint[] apidl, ref Guid riid, nint reserved, out nint ppv);
		[PreserveSig] int GetDisplayNameOf(nint pidl, uint flags, nint name);
		[PreserveSig] int SetNameOf(nint hwnd, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint pidlOut);
	}

	[ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IContextMenu
	{
		[PreserveSig] int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
		[PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
		[PreserveSig] int GetCommandString(nuint idCmd, uint type, nint reserved, nint name, uint cchMax);
	}

	[ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IContextMenu2
	{
		[PreserveSig] int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
		[PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
		[PreserveSig] int GetCommandString(nuint idCmd, uint type, nint reserved, nint name, uint cchMax);
		[PreserveSig] int HandleMenuMsg(uint msg, nint wParam, nint lParam);
	}

	[ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IContextMenu3
	{
		[PreserveSig] int QueryContextMenu(nint hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
		[PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
		[PreserveSig] int GetCommandString(nuint idCmd, uint type, nint reserved, nint name, uint cchMax);
		[PreserveSig] int HandleMenuMsg(uint msg, nint wParam, nint lParam);
		[PreserveSig] int HandleMenuMsg2(uint msg, nint wParam, nint lParam, out nint result);
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct CMINVOKECOMMANDINFOEX
	{
		public int cbSize;
		public uint fMask;
		public nint hwnd;
		public nint lpVerb;
		[MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
		[MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
		public int nShow;
		public uint dwHotKey;
		public nint hIcon;
		[MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
		public nint lpVerbW;
		[MarshalAs(UnmanagedType.LPWStr)] public string? lpParametersW;
		[MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectoryW;
		[MarshalAs(UnmanagedType.LPWStr)] public string? lpTitleW;
		public Native.POINT ptInvoke;
	}

	/// <summary>도우미에게 넘기는 DesktopPop 전용 항목 (글자와 하위 메뉴만; 실행은 본체가 함).</summary>
	private sealed class Spec
	{
		public string T { get; set; } = "";
		public bool S { get; set; }
		public List<Spec>? C { get; set; }
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint pbc, out nint pidl, uint sfgaoIn, out uint sfgaoOut);
	[DllImport("shell32.dll")] private static extern int SHBindToParent(nint pidl, [In] ref Guid riid, out nint ppv, out nint pidlLast);
	[DllImport("ole32.dll")] private static extern void CoTaskMemFree(nint pv);
	[DllImport("ole32.dll")] private static extern int OleInitialize(nint reserved);
	[DllImport("ole32.dll")] private static extern void OleUninitialize();
	[DllImport("user32.dll")] private static extern nint CreatePopupMenu();
	[DllImport("user32.dll")] private static extern bool DestroyMenu(nint h);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool InsertMenu(nint h, uint pos, uint flags, nuint id, string? text);
	[DllImport("user32.dll")] private static extern int TrackPopupMenuEx(nint h, uint flags, int x, int y, nint hwnd, nint tpm);
	[DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint h);
	[DllImport("user32.dll")] private static extern bool PostMessage(nint h, uint msg, nint w, nint l);
	[DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);

	private const uint MF_STRING = 0, MF_POPUP = 0x10, MF_SEPARATOR = 0x800, MF_BYPOSITION = 0x400;
	private const uint CMF_NORMAL = 0, CMF_CANRENAME = 0x10, CMF_EXTENDEDVERBS = 0x100;
	private const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
	private const uint ShellFirst = 1, ShellLast = 0x7FFF, OurFirst = 0x8000;

	private const string HelperFlag = "--shell-menu";

	/// <summary>도우미가 알려 주는 결과 (결과 줄 / 종료 코드).</summary>
	internal const int CodeDone = 0, CodeRename = 2, CodeFailed = 3, CodeExtraBase = 1000;

	private static IContextMenu2? cm2;
	private static IContextMenu3? cm3;

	/// <summary>본체 → 도우미 요청 한 줄 (JSON 기본 설정이 한글을 \uXXXX 로 바꿔서 글자 깨질 일 없음).</summary>
	private sealed class Request
	{
		public bool E { get; set; } // 확장 메뉴 (Shift+우클릭)
		public bool R { get; set; } // 이름 바꾸기 가능
		public List<Spec> X { get; set; } = new List<Spec>();
		public List<string> P { get; set; } = new List<string>();
		public string? A { get; set; } // 메뉴 대신 바로 할 일: open / properties / locate (아이콘 열기 등)
	}

	/// <summary>띄워 둔 도우미 하나.</summary>
	private sealed class Helper
	{
		public Process Proc = null!;
		public bool Requested;
		public Action<int>? OnResult; // 결과 줄을 받았을 때 (UI 스레드)
		public Action<int>? OnExit;   // 도우미가 끝났을 때, 종료 코드 (UI 스레드)
	}

	private static Helper? standby;
	private static Dispatcher? uiDispatcher;
	private static int helperCrashes;
	private static int warmFailures;
	private static bool closing;

	// ───────────────────────── 본체 쪽 ─────────────────────────

	/// <summary>
	/// 다음 우클릭·아이콘 열기를 위해 도우미를 미리 띄워 둠. Windows 메뉴를 쓰면 메뉴 확장(압축 프로그램 등)까지
	/// 미리 불러 놓고 기다려서 우클릭하면 메뉴가 바로 뜸. 아이콘을 열 때도 이 도우미가 바로 열어 줌.
	/// </summary>
	public static void Prewarm()
	{
		App? app = App.Instance;
		if (closing || app == null)
		{
			return;
		}
		if (standby != null && !standby.Proc.HasExited)
		{
			return;
		}
		uiDispatcher ??= app.Dispatcher;
		// 메뉴 확장을 미리 불러오다 두 번 튕겼으면 미리 불러오기만 건너뜀 (열기용으로는 계속 대기)
		bool warmMenus = app.Config.UseWindowsMenu && warmFailures < 2;
		standby = StartHelper(warmMenus);
	}

	/// <summary>
	/// 미리 띄워 둔 도우미에게 아이콘 열기·속성·파일 위치 열기를 맡김. 도우미가 없으면 false (그때는 새로 띄워서 함).
	/// 여는 일을 별도 프로세스에서 하는 이유: 파일을 열 때도 셸 확장이 불려 와서, 고장 난 확장이 있으면 DesktopPop 이 튕길 수 있음.
	/// </summary>
	public static bool TryRunAction(string action, string path)
	{
		Helper? h = standby;
		if (h == null || h.Proc.HasExited)
		{
			return false;
		}
		standby = null;
		h.Requested = true;
		h.OnResult = (int _) => Prewarm(); // 다음 열기를 위해 바로 새로 준비
		h.OnExit = (int code) =>
		{
			if (code != CodeDone && code != CodeFailed)
			{
				App.Log($"'{action}' 을 하던 도우미가 비정상 종료됨 (코드 0x{code:X8}): {path}");
			}
			Prewarm();
		};
		try
		{
			AllowSetForegroundWindow(h.Proc.Id); // 열린 프로그램·속성 창이 맨 앞에 뜨게
			Request req = new Request { A = action, P = new List<string> { path } };
			h.Proc.StandardInput.WriteLine(JsonSerializer.Serialize(req));
			h.Proc.StandardInput.Flush();
			return true;
		}
		catch (Exception ex)
		{
			App.Log(ex);
			return false; // 도우미가 막 꺼진 것: 새로 띄워서 함
		}
	}

	/// <summary>대기 중인 도우미를 내려 놓음 (설정을 껐을 때).</summary>
	public static void DropStandby()
	{
		Helper? h = standby;
		standby = null;
		if (h != null)
		{
			try
			{
				h.Proc.StandardInput.Close(); // 요청 없이 입력이 닫히면 도우미는 조용히 끝남
			}
			catch
			{
			}
		}
	}

	/// <summary>DesktopPop 이 꺼질 때.</summary>
	public static void Shutdown()
	{
		closing = true;
		DropStandby();
	}

	private static Helper? StartHelper(bool warm)
	{
		try
		{
			ProcessStartInfo psi = SelfStartInfo();
			psi.ArgumentList.Add(HelperFlag);
			psi.ArgumentList.Add(warm ? "warm" : "cold");
			psi.RedirectStandardInput = true;
			psi.RedirectStandardOutput = true;
			Process? p = Process.Start(psi);
			if (p == null)
			{
				return null;
			}
			Helper h = new Helper { Proc = p };
			Dispatcher d = uiDispatcher!;
			p.OutputDataReceived += (object s, DataReceivedEventArgs e) =>
			{
				string? line = e.Data?.Trim();
				if (line != null && int.TryParse(line, out var code))
				{
					d.InvokeAsync(() => h.OnResult?.Invoke(code));
				}
			};
			p.EnableRaisingEvents = true;
			p.Exited += (object? s, EventArgs e) =>
			{
				int code;
				try
				{
					p.WaitForExit(); // 남은 출력까지 다 받은 뒤
					code = p.ExitCode;
				}
				catch
				{
					code = -1;
				}
				d.InvokeAsync(() =>
				{
					if (standby == h)
					{
						standby = null;
					}
					if (!h.Requested && code != 0)
					{
						// 대기하며 메뉴 확장을 미리 불러오다 튕김. 두 번이면 미리 띄우기는 그만 (우클릭할 때만 띄움)
						warmFailures++;
						App.Log($"Windows 메뉴 도우미가 준비 중 비정상 종료됨 (코드 0x{code:X8}, {warmFailures}회)");
					}
					h.OnExit?.Invoke(code);
					p.Dispose();
				});
			};
			p.BeginOutputReadLine();
			return h;
		}
		catch (Exception ex)
		{
			App.Log(ex);
			return null;
		}
	}

	/// <summary>
	/// 도우미에게 Windows 메뉴를 띄우게 함. 바로 돌아오고, 고른 결과는 나중에 처리함.
	/// 도우미가 메뉴를 못 띄우거나 튕기면 fallback(간단 메뉴)을 부름.
	/// </summary>
	public static void ShowAsync(Window owner, IReadOnlyList<string> paths, List<MenuEntry> extras, Action? onRename, Action fallback, bool extended)
	{
		uiDispatcher ??= owner.Dispatcher;
		List<Action?> leaves = new List<Action?>();
		List<Spec> specs = extras.Select((MenuEntry e) => ToSpec(e, leaves)).ToList();
		Helper? h = standby;
		standby = null;
		if (h == null || h.Proc.HasExited)
		{
			h = StartHelper(warm: false);
		}
		if (h == null)
		{
			fallback();
			return;
		}
		// 메뉴가 떠 있는 동안 가장자리 판이 닫히지 않게
		App.OpenMenus++;
		bool released = false;
		void Release()
		{
			if (!released)
			{
				released = true;
				App.OpenMenus = Math.Max(0, App.OpenMenus - 1);
			}
		}
		// 혹시 도우미가 멈춰 있어도 판이 영영 열려 있지 않게
		DispatcherTimer guard = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2.0) };
		guard.Tick += (object? s, EventArgs e) =>
		{
			guard.Stop();
			Release();
		};
		guard.Start();
		bool handled = false;
		void Handle(int code)
		{
			if (handled)
			{
				return;
			}
			handled = true;
			guard.Stop();
			Release();
			HandleResult(code, leaves, onRename, fallback);
			Prewarm(); // 메뉴가 닫혔으니 다음 우클릭 준비 (이 도우미는 고른 명령을 마저 실행하는 중일 수 있음)
		}
		h.OnResult = Handle;
		h.OnExit = (int code) =>
		{
			if (!handled)
			{
				Handle(code);
			}
			else if (code != CodeDone && code != CodeRename && code != CodeFailed && code < CodeExtraBase)
			{
				// 결과를 알린 뒤 명령을 실행하다 튕김: 본체는 멀쩡하니 기록만
				App.Log($"Windows 메뉴 명령을 실행하던 도우미가 비정상 종료됨 (코드 0x{code:X8})");
			}
			Prewarm(); // 다음 우클릭 준비
		};
		h.Requested = true;
		try
		{
			AllowSetForegroundWindow(h.Proc.Id); // 도우미가 메뉴를 맨 앞에 띄울 수 있게
			Request req = new Request { E = extended, R = onRename != null, X = specs, P = paths.ToList() };
			h.Proc.StandardInput.WriteLine(JsonSerializer.Serialize(req));
			h.Proc.StandardInput.Flush();
		}
		catch (Exception ex)
		{
			App.Log(ex); // 도우미가 막 꺼진 것: 종료 처리에서 간단 메뉴로 넘어감
		}
	}

	private static void HandleResult(int code, List<Action?> leaves, Action? onRename, Action fallback)
	{
		if (code == CodeDone)
		{
			// 압축 풀기 같은 명령은 끝나는 데 시간이 걸려도 파일 감시가 알아서 새로고침함
			App.Instance?.ScheduleRefresh();
			return;
		}
		if (code == CodeRename)
		{
			onRename?.Invoke();
			return;
		}
		if (code >= CodeExtraBase && code - CodeExtraBase < leaves.Count)
		{
			leaves[code - CodeExtraBase]?.Invoke();
			return;
		}
		if (code == CodeFailed)
		{
			fallback();
			return;
		}
		// 그 밖의 코드 = 도우미가 튕김 (어떤 프로그램의 메뉴 확장이 고장 남). DesktopPop 은 멀쩡하니 간단 메뉴로.
		helperCrashes++;
		App.Log($"Windows 메뉴 도우미가 비정상 종료됨 (코드 0x{code:X8}, 이번 실행 {helperCrashes}회)");
		App? app = App.Instance;
		if (helperCrashes >= 2 && app != null && app.Config.UseWindowsMenu)
		{
			app.Config.UseWindowsMenu = false;
			app.Config.Save();
			DropStandby();
			app.NotifyWindowsMenuDisabled();
		}
		fallback();
	}

	private static Spec ToSpec(MenuEntry e, List<Action?> leaves)
	{
		if (e.IsSeparator)
		{
			return new Spec { S = true };
		}
		if (e.Children != null)
		{
			return new Spec { T = e.Text, C = e.Children.Select((MenuEntry c) => ToSpec(c, leaves)).ToList() };
		}
		leaves.Add(e.Run);
		return new Spec { T = e.Text };
	}

	private static ProcessStartInfo SelfStartInfo()
	{
		string exe = Environment.ProcessPath ?? throw new InvalidOperationException("실행 파일 경로를 찾지 못했어요.");
		ProcessStartInfo psi = new ProcessStartInfo(exe)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			WorkingDirectory = AppContext.BaseDirectory
		};
		if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
		{
			psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, (Assembly.GetEntryAssembly()?.GetName().Name ?? "DesktopPop") + ".dll"));
		}
		return psi;
	}

	// ───────────────────────── 도우미 쪽 ─────────────────────────

	/// <summary>
	/// DesktopPop.exe --shell-menu warm|cold
	/// 메뉴 주인 창을 만들고 (warm 이면 메뉴 확장까지 미리 불러 놓고) 표준 입력으로 요청 한 줄을 기다림.
	/// 메뉴를 띄워 결과를 한 줄로 알리고 끝남. 요청 없이 입력이 닫히면 (본체가 꺼짐) 조용히 끝남.
	/// </summary>
	internal static bool TryRunHelper(string[] args)
	{
		if (args.Length == 0 || args[0] != HelperFlag)
		{
			return false;
		}
		bool warm = args.Length > 1 && args[1] == "warm";
		int code = CodeDone;
		bool reported = false;
		void Report(int c)
		{
			code = c;
			if (!reported)
			{
				reported = true;
				try
				{
					Console.Out.WriteLine(c.ToString(System.Globalization.CultureInfo.InvariantCulture));
					Console.Out.Flush();
				}
				catch
				{
				}
			}
		}
		try
		{
			OleInitialize(IntPtr.Zero);
			try
			{
				// 화면에 안 보이는 작은 창 하나를 메뉴 주인으로 씀
				HwndSourceParameters hp = new HwndSourceParameters("DesktopPop 메뉴")
				{
					WindowStyle = unchecked((int)0x80000000), // WS_POPUP (보이지 않음)
					ExtendedWindowStyle = 0x80, // WS_EX_TOOLWINDOW: 작업 표시줄에 안 나옴
					Width = 1,
					Height = 1
				};
				using HwndSource source = new HwndSource(hp);
				Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
				if (warm)
				{
					WarmUp(source.Handle);
				}
				// 요청은 다른 스레드에서 읽고 메뉴는 이 스레드(창 주인)에서 띄움.
				// 기다리는 동안에도 이 스레드가 메시지를 처리해야 다른 프로그램이 모든 창에 보내는 알림이 막히지 않음.
				Thread reader = new Thread(() =>
				{
					string? line = null;
					try
					{
						line = Console.In.ReadLine();
					}
					catch
					{
					}
					dispatcher.InvokeAsync(() =>
					{
						if (line == null)
						{
							dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal); // 본체가 꺼졌거나 대기 취소
							return;
						}
						Request? req = null;
						try
						{
							req = JsonSerializer.Deserialize<Request>(line);
						}
						catch (Exception ex)
						{
							App.Log(ex);
						}
						if (req == null || req.P.Count == 0)
						{
							Report(CodeFailed);
							dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
							return;
						}
						bool linger = false;
						TimeSpan waitIfNoWindow = TimeSpan.FromSeconds(3.0);
						try
						{
							if (req.A != null)
							{
								// 아이콘 열기 / 속성 / 파일 위치 열기
								linger = ShellOperations.RunAction(req.A, req.P[0]);
								Report(linger ? CodeDone : CodeFailed);
								if (req.A == "properties")
								{
									waitIfNoWindow = TimeSpan.FromSeconds(15.0); // 속성 창은 늦게 뜰 때가 있음
								}
							}
							else
							{
								linger = RunMenu(source, req.P, req.X, req.R, req.E, Report);
							}
						}
						catch (Exception ex2)
						{
							App.Log(ex2);
							Report(CodeFailed);
						}
						if (linger)
						{
							ShutdownWhenWindowsClosed(dispatcher, waitIfNoWindow); // 속성 창·확인 창이 닫힐 때까지 살아 있음
						}
						else
						{
							dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
						}
					});
				})
				{
					IsBackground = true,
					Name = "메뉴 요청 읽기"
				};
				reader.Start();
				if (warm)
				{
					Console.Out.WriteLine("READY");
					Console.Out.Flush();
				}
				Dispatcher.Run();
			}
			finally
			{
				OleUninitialize(); // 복사·잘라내기 한 내용이 클립보드에 남게 정리
			}
		}
		catch (Exception ex3)
		{
			App.Log(ex3);
			Report(CodeFailed);
		}
		Environment.Exit(code);
		return true;
	}

	/// <summary>
	/// 바탕화면 폴더와 그 안 파일 하나로 메뉴를 한 번 만들어 봄 → 압축 프로그램 같은 메뉴 확장이 미리 불러와져서
	/// 실제 우클릭 때 메뉴가 바로 뜸.
	/// </summary>
	private static void WarmUp(nint hwnd)
	{
		try
		{
			string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
			string? file = Directory.EnumerateFiles(desk).FirstOrDefault((string f) => !string.Equals(Path.GetFileName(f), "desktop.ini", StringComparison.OrdinalIgnoreCase));
			foreach (string p in new string?[2] { desk, file }.OfType<string>())
			{
				BuildAndDiscard(hwnd, p);
			}
		}
		catch (Exception ex)
		{
			App.Log(ex);
		}
	}

	private static void BuildAndDiscard(nint hwnd, string path)
	{
		nint pidl = IntPtr.Zero;
		IShellFolder? parent = null;
		IContextMenu? cm = null;
		nint hMenu = IntPtr.Zero;
		try
		{
			if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0u, out _) != 0 || pidl == IntPtr.Zero)
			{
				return;
			}
			Guid iidFolder = typeof(IShellFolder).GUID;
			if (SHBindToParent(pidl, ref iidFolder, out var ppv, out var child) != 0)
			{
				return;
			}
			parent = (IShellFolder)Marshal.GetObjectForIUnknown(ppv);
			Marshal.Release(ppv);
			Guid iidMenu = typeof(IContextMenu).GUID;
			if (parent.GetUIObjectOf(hwnd, 1u, new nint[1] { child }, ref iidMenu, IntPtr.Zero, out var pcm) != 0)
			{
				return;
			}
			cm = (IContextMenu)Marshal.GetObjectForIUnknown(pcm);
			Marshal.Release(pcm);
			hMenu = CreatePopupMenu();
			cm.QueryContextMenu(hMenu, 0u, ShellFirst, ShellLast, CMF_NORMAL);
		}
		finally
		{
			if (hMenu != IntPtr.Zero)
			{
				DestroyMenu(hMenu);
			}
			if (cm != null)
			{
				Marshal.ReleaseComObject(cm);
			}
			if (parent != null)
			{
				Marshal.ReleaseComObject(parent);
			}
			if (pidl != IntPtr.Zero)
			{
				CoTaskMemFree(pidl);
			}
		}
	}

	/// <summary>이 도우미가 띄운 창(속성, 삭제 확인 등)이 다 닫히면 끝냄. 창이 안 뜨는 명령이면 waitIfNoWindow 뒤 끝.</summary>
	private static void ShutdownWhenWindowsClosed(Dispatcher dispatcher, TimeSpan waitIfNoWindow)
	{
		DateTime start = DateTime.UtcNow;
		DateTime lastWindowAt = DateTime.MinValue;
		bool sawWindow = false;
		DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(250.0) };
		timer.Tick += (object? s, EventArgs e) =>
		{
			DateTime now = DateTime.UtcNow;
			if (HasVisibleOwnWindow())
			{
				sawWindow = true;
				lastWindowAt = now;
				return;
			}
			if ((sawWindow && (now - lastWindowAt).TotalMilliseconds >= 750.0) || (!sawWindow && now - start >= waitIfNoWindow) || (now - start).TotalHours >= 2.0)
			{
				timer.Stop();
				dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
			}
		};
		timer.Start();
	}

	private static bool HasVisibleOwnWindow()
	{
		bool found = false;
		Native.EnumWindows((nint h, nint _) =>
		{
			if (!Native.IsWindowVisible(h))
			{
				return true;
			}
			Native.GetWindowThreadProcessId(h, out var pid);
			if (pid != (uint)Environment.ProcessId)
			{
				return true;
			}
			found = true;
			return false;
		}, IntPtr.Zero);
		return found;
	}

	/// <summary>
	/// 메뉴를 띄우고 고른 결과를 report 로 알림. Windows 명령을 골랐으면 여기서 바로 실행하고 true
	/// (명령이 띄운 창이 닫힐 때까지 기다려야 함).
	/// </summary>
	private static bool RunMenu(HwndSource source, IReadOnlyList<string> paths, List<Spec> extras, bool canRename, bool extended, Action<int> report)
	{
		nint hwnd = source.Handle;
		if (hwnd == IntPtr.Zero || paths.Count == 0)
		{
			report(CodeFailed);
			return false;
		}
		// 같은 폴더에 있는 것끼리만 한 메뉴로 (내 바탕화면 / 공용 바탕화면이 섞이면 우클릭한 쪽 기준)
		static string ParentOf(string p) => p.StartsWith("::", StringComparison.Ordinal) ? "::" : (Path.GetDirectoryName(p.TrimEnd('\\')) ?? "");
		string parentDir = ParentOf(paths[0]);
		List<string> group = paths.Where((string p) => string.Equals(ParentOf(p), parentDir, StringComparison.OrdinalIgnoreCase)).ToList();
		List<nint> absPidls = new List<nint>();
		IShellFolder? parent = null;
		IContextMenu? cm = null;
		nint hMenu = IntPtr.Zero;
		Dictionary<uint, int> ourLeaves = new Dictionary<uint, int>();
		bool hooked = false;
		try
		{
			List<nint> children = new List<nint>();
			Guid iidFolder = typeof(IShellFolder).GUID;
			foreach (string p in group)
			{
				if (SHParseDisplayName(p, IntPtr.Zero, out var pidl, 0u, out _) != 0 || pidl == IntPtr.Zero)
				{
					continue;
				}
				absPidls.Add(pidl);
				if (SHBindToParent(pidl, ref iidFolder, out var ppv, out var child) != 0)
				{
					continue;
				}
				if (parent == null)
				{
					parent = (IShellFolder)Marshal.GetObjectForIUnknown(ppv);
				}
				Marshal.Release(ppv);
				children.Add(child); // 부모 pidl 안을 가리키는 포인터라 따로 해제 안 함
			}
			if (parent == null || children.Count == 0)
			{
				report(CodeFailed);
				return false;
			}
			Guid iidMenu = typeof(IContextMenu).GUID;
			if (parent.GetUIObjectOf(hwnd, (uint)children.Count, children.ToArray(), ref iidMenu, IntPtr.Zero, out var pcm) != 0)
			{
				report(CodeFailed);
				return false;
			}
			cm = (IContextMenu)Marshal.GetObjectForIUnknown(pcm);
			Marshal.Release(pcm);
			cm2 = cm as IContextMenu2;
			cm3 = cm as IContextMenu3;
			hMenu = CreatePopupMenu();
			uint flags = CMF_NORMAL | ((children.Count == 1 && canRename) ? CMF_CANRENAME : 0u);
			if (extended)
			{
				flags |= CMF_EXTENDEDVERBS; // Shift+우클릭 = 확장 메뉴
			}
			if (cm.QueryContextMenu(hMenu, 0u, ShellFirst, ShellLast, flags) < 0)
			{
				report(CodeFailed);
				return false;
			}
			// DesktopPop 전용 항목을 맨 위에
			uint nextId = OurFirst;
			uint pos = 0;
			int leaf = 0;
			foreach (Spec e in extras)
			{
				InsertEntry(hMenu, ref pos, e, ref nextId, ref leaf, ourLeaves);
			}
			if (extras.Count > 0)
			{
				InsertMenu(hMenu, pos, MF_BYPOSITION | MF_SEPARATOR, 0u, null);
			}
			// "보내기", "연결 프로그램" 같은 하위 메뉴가 채워지려면 메뉴 메시지를 넘겨 줘야 함
			source.AddHook(Hook);
			hooked = true;
			Native.GetCursorPos(out var pt);
			SetForegroundWindow(hwnd);
			int cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, hwnd, IntPtr.Zero);
			PostMessage(hwnd, 0u, IntPtr.Zero, IntPtr.Zero); // WM_NULL: 메뉴가 바로 닫히게
			if (cmd <= 0)
			{
				report(CodeDone);
				return false;
			}
			if (ourLeaves.TryGetValue((uint)cmd, out var index))
			{
				report(CodeExtraBase + index);
				return false;
			}
			string? verb = VerbOf(cm, (uint)cmd - ShellFirst);
			if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase))
			{
				report(CodeRename); // 탐색기 창이 아니라서 Windows 가 직접 못 함 → 본체가 이름 바꾸기 창
				return false;
			}
			bool ctrl = (Native.GetAsyncKeyState(0x11) & 0x8000) != 0;
			bool shift = (Native.GetAsyncKeyState(0x10) & 0x8000) != 0;
			CMINVOKECOMMANDINFOEX info = new CMINVOKECOMMANDINFOEX
			{
				cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
				fMask = 0x4000u /*UNICODE*/ | 0x20000000u /*PTINVOKE*/ | 0x100u /*NOASYNC: 끝날 때까지 기다림*/
					| (ctrl ? 0x40000000u : 0u)
					| (shift ? 0x10000000u : 0u),
				hwnd = hwnd,
				lpVerb = new IntPtr(cmd - (int)ShellFirst),
				lpVerbW = new IntPtr(cmd - (int)ShellFirst),
				nShow = 1,
				ptInvoke = pt
			};
			report(CodeDone); // 고른 명령은 이제 이 도우미가 실행
			cm.InvokeCommand(ref info);
			return true;
		}
		finally
		{
			if (hooked)
			{
				source.RemoveHook(Hook);
			}
			if (hMenu != IntPtr.Zero)
			{
				DestroyMenu(hMenu);
			}
			cm2 = null;
			cm3 = null;
			if (cm != null)
			{
				Marshal.ReleaseComObject(cm);
			}
			if (parent != null)
			{
				Marshal.ReleaseComObject(parent);
			}
			foreach (nint p in absPidls)
			{
				CoTaskMemFree(p);
			}
		}
	}

	private static void InsertEntry(nint menu, ref uint pos, Spec e, ref uint nextId, ref int leaf, Dictionary<uint, int> leaves)
	{
		if (e.S)
		{
			InsertMenu(menu, pos++, MF_BYPOSITION | MF_SEPARATOR, 0u, null);
			return;
		}
		if (e.C != null)
		{
			nint sub = CreatePopupMenu(); // 부모 메뉴를 없앨 때 같이 없어짐
			uint subPos = 0;
			foreach (Spec c in e.C)
			{
				InsertEntry(sub, ref subPos, c, ref nextId, ref leaf, leaves);
			}
			InsertMenu(menu, pos++, MF_BYPOSITION | MF_POPUP | MF_STRING, (nuint)(ulong)sub, e.T);
			return;
		}
		uint id = nextId++;
		leaves[id] = leaf++;
		InsertMenu(menu, pos++, MF_BYPOSITION | MF_STRING, id, e.T);
	}

	private static string? VerbOf(IContextMenu cm, uint offset)
	{
		nint buf = Marshal.AllocHGlobal(520);
		try
		{
			return (cm.GetCommandString(offset, 4u /*GCS_VERBW*/, IntPtr.Zero, buf, 260u) == 0) ? Marshal.PtrToStringUni(buf) : null;
		}
		catch
		{
			return null;
		}
		finally
		{
			Marshal.FreeHGlobal(buf);
		}
	}

	private static nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		// WM_INITMENUPOPUP, WM_DRAWITEM, WM_MEASUREITEM, WM_MENUCHAR
		if (msg is not (0x117 or 0x2B or 0x2C or 0x120))
		{
			return IntPtr.Zero;
		}
		try
		{
			if (cm3 != null && cm3.HandleMenuMsg2((uint)msg, wParam, lParam, out var r) == 0)
			{
				handled = true;
				return r;
			}
			if (cm2 != null && msg != 0x120 && cm2.HandleMenuMsg((uint)msg, wParam, lParam) == 0)
			{
				handled = true;
				return IntPtr.Zero;
			}
		}
		catch
		{
		}
		return IntPtr.Zero;
	}
}
