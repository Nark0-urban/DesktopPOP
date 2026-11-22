using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace DesktopPop;

/// <summary>프로그램 본체: 시작·종료, 원래 바탕화면 아이콘 숨기기, 가장자리 판·가방 관리, 파일 옮기기·삭제·이름 바꾸기, 바탕화면 폴더 감시.</summary>
public sealed class App : Application
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct SHFILEOPSTRUCT
	{
		public nint hwnd;

		public uint wFunc;

		public string pFrom;

		public string? pTo;

		public ushort fFlags;

		public bool fAnyOperationsAborted;

		public nint hNameMappings;

		public string? lpszProgressTitle;
	}

	internal static int OpenMenus;

	internal static object? DragSource;

	internal AppConfig Config;

	internal ItemStore Store;

	internal EdgePanel LeftPanel;

	internal EdgePanel RightPanel;

	internal readonly List<EdgePanel> EdgePanels = new List<EdgePanel>();

	internal readonly List<BagWindow> BagWindows = new List<BagWindow>();

	private readonly AllPanelsDismissal allPanelsDismissal = new AllPanelsDismissal();

	private int queuedDismissVersion = -1;

	private readonly PanelWorkReturn panelWorkReturn = new PanelWorkReturn();

	private Tray? tray;

	private DispatcherTimer? edgeTimer;

	private DispatcherTimer? fsDebounce;

	private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();

	private bool refreshQueued;

	private readonly Stopwatch bootClock = Stopwatch.StartNew();

	internal bool ShuttingDown;

	private readonly List<DateTime> bagLosses = new List<DateTime>();

	private int tickCount;

	private readonly Dictionary<string, (PanelConfig panel, int? index, bool fly)> pendingPlace = new Dictionary<string, (PanelConfig, int?, bool)>(StringComparer.OrdinalIgnoreCase);

	public static App Instance => (App)Application.Current;

	internal bool AllPanelsOpen { get; private set; }

	private bool KeepAllPanelsDuringInteraction
	{
		get
		{
			if (DragSource == null && !KeyboardHook.IsSuspended)
			{
				return DesktopBand.IsActive;
			}
			return true;
		}
	}

	internal static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

	[STAThread]
	public static void Main(string[] args)
	{
		if (DesktopRecovery.TryRunHelper(args) || Shell.TryRunHelper(args) || NativeShellMenu.TryRunHelper(args))
		{
			return;
		}
		if (args.Length > 0 && args[0] == QuitFlag)
		{
			Environment.Exit(QuitRunningInstance());
			return;
		}
		bool createdNew;
		using (new Mutex(initiallyOwned: true, "DesktopPop.SingleInstance.v1", out createdNew))
		{
			if (!createdNew)
			{
				MessageBox.Show("DesktopPop이 이미 켜져 있어요.\n작업 표시줄 오른쪽 아래(시계 옆) 아이콘을 확인해 주세요.", "DesktopPop");
				return;
			}
			try
			{
				new App().Run();
			}
			finally
			{
				DesktopRecovery.Restore();
			}
		}
	}

	/// <summary>DesktopPop.exe --quit : 켜져 있는 DesktopPop 을 트레이 메뉴의 "종료"와 똑같이 끔 (업데이트 설치용).</summary>
	private const string QuitFlag = "--quit";

	private const string QuitEventName = "Local\\DesktopPop.Quit.v1";

	private RegisteredWaitHandle? quitWait;

	/// <summary>종료 코드: 0 = 껐음, 1 = 켜져 있지 않음, 2 = 신호는 보냈는데 15초 안에 안 꺼짐.</summary>
	private static int QuitRunningInstance()
	{
		EventWaitHandle signal;
		try
		{
			signal = EventWaitHandle.OpenExisting(QuitEventName);
		}
		catch (WaitHandleCannotBeOpenedException)
		{
			return 1;
		}
		using (signal)
		{
			signal.Set();
		}
		for (int i = 0; i < 150; i++)
		{
			try
			{
				using (Mutex.OpenExisting("DesktopPop.SingleInstance.v1"))
				{
				}
			}
			catch (WaitHandleCannotBeOpenedException)
			{
				return 0;
			}
			Thread.Sleep(100);
		}
		return 2;
	}

	/// <summary>--quit 신호를 기다림.</summary>
	private void ListenForQuit()
	{
		try
		{
			EventWaitHandle signal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, QuitEventName);
			quitWait = ThreadPool.RegisterWaitForSingleObject(signal, (object? _, bool _) =>
			{
				Dispatcher.InvokeAsync(() =>
				{
					Diag("  --quit 신호로 끔 (업데이트 설치)");
					if (Config is null)
					{
						Shutdown(); // 아직 켜는 중이라 아이콘을 숨기기 전
					}
					else
					{
						Quit();
					}
				});
			}, null, -1, executeOnlyOnce: true);
		}
		catch (Exception ex)
		{
			Log(ex);
		}
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		ListenForQuit();
		ShutdownMode = ShutdownMode.OnExplicitShutdown;
		DispatcherUnhandledException += (object s, DispatcherUnhandledExceptionEventArgs ev) =>
		{
			Log(ev.Exception);
			DesktopRecovery.Restore();
			ev.Handled = true;
			MessageBox.Show("오류가 발생해 원래 바탕화면 아이콘을 복구했어요.\nDesktopPop을 다시 실행해 주세요.", "DesktopPop");
			Shutdown(1);
		};
		AppDomain.CurrentDomain.UnhandledException += (object s, UnhandledExceptionEventArgs ev) =>
		{
			Log(ev.ExceptionObject.ToString() ?? "알 수 없는 오류");
			DesktopRecovery.Restore();
		};
		Config = AppConfig.Load();
		IReadOnlyList<Native.MonitorInfo> monitors = Native.Monitors();
		Config.EnsurePanelLayout(monitors[0].DeviceName, monitors.Select((Native.MonitorInfo m) => m.DeviceName));
		Theme.Apply(Config.AccentColor, Config.PanelColor);
		UiFont.Apply(); // data\fonts 에 넣은 폰트가 있으면 (창을 만들기 전에)
		Diag($"시작: 설정 폴더='{AppConfig.Dir}'{(AppConfig.UsingFallbackDir ? " (exe 폴더에 쓸 수 없어 사용자 폴더 사용)" : "")} 가방={Config.Bags.Count}개");
		AutoStart.RepairPath();
		SessionEnding += (object s, SessionEndingCancelEventArgs ev) =>
		{
			Diag($"  윈도우 종료/로그아웃 감지 ({ev.ReasonSessionEnding})");
			ShuttingDown = true;
			if (Config.HideDesktopIcons)
			{
				DesktopShell.SetIconsVisible(visible: true);
			}
			Config.Save();
		};
		tray = new Tray(this);
		StartupOverlay overlay = null;
		if (Config.ShowStartupAnimation)
		{
			overlay = new StartupOverlay();
			overlay.Show();
			Mark("로딩 화면 표시");
		}
		long shownAt = bootClock.ElapsedMilliseconds;
		Store = new ItemStore();
		Thread thread = new Thread(() =>
		{
			try
			{
				Store.Refresh(Config);
			}
			catch (Exception ex)
			{
				Log(ex);
			}
			Dispatcher.InvokeAsync(() =>
			{
				Anim.After((overlay == null) ? 1 : Math.Max(1L, 900 - (bootClock.ElapsedMilliseconds - shownAt)), () =>
				{
					InitAfterSplash(overlay);
				});
			});
		});
		thread.IsBackground = true;
		thread.Name = "아이콘 읽기";
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
	}

	private void Mark(string what)
	{
		Diag($"  {bootClock.ElapsedMilliseconds,5}ms {what}");
	}

	private void InitAfterSplash(StartupOverlay? overlay)
	{
		Mark("아이콘 읽기 끝");
		if (!Config.LayoutImported)
		{
			try
			{
				ImportLayout();
			}
			catch (Exception ex)
			{
				Log(ex);
			}
			Config.LayoutImported = true;
		}
		Store.AssignNew(Config);
		Config.Save();
		ReconcilePanels();
		if (overlay != null)
		{
			overlay.PlayScan(EnsureIconsHidden, () =>
			{
				overlay.Close();
				Finish();
				foreach (EdgePanel edgePanel in EdgePanels)
				{
					edgePanel.Peek();
				}
			});
		}
		else
		{
			EnsureIconsHidden();
			Finish();
		}
		fsDebounce = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(700L)
		};
		fsDebounce.Tick += (object? s, EventArgs ev) =>
		{
			fsDebounce.Stop();
			ScheduleRefresh();
		};
		string[] desktopDirs = ItemStore.DesktopDirs;
		foreach (string path in desktopDirs)
		{
			try
			{
				FileSystemWatcher fileSystemWatcher = new FileSystemWatcher(path)
				{
					IncludeSubdirectories = false,
					NotifyFilter = (NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes | NotifyFilters.LastWrite)
				};
				FileSystemEventHandler value = (object s, FileSystemEventArgs ev) =>
				{
					Dispatcher.InvokeAsync(() =>
					{
						fsDebounce.Stop();
						fsDebounce.Start();
					});
				};
				fileSystemWatcher.Created += value;
				fileSystemWatcher.Deleted += value;
				fileSystemWatcher.Changed += value;
				fileSystemWatcher.Renamed += (object s, RenamedEventArgs ev) =>
				{
					Dispatcher.InvokeAsync(() =>
					{
						RemapPaths(ev.OldFullPath, ev.FullPath);
						fsDebounce.Stop();
						fsDebounce.Start();
					});
				};
				fileSystemWatcher.Error += (object s, ErrorEventArgs ev) =>
				{
					Dispatcher.InvokeAsync(ScheduleRefresh);
				};
				fileSystemWatcher.EnableRaisingEvents = true;
				watchers.Add(fileSystemWatcher);
			}
			catch (Exception ex2)
			{
				Log(ex2);
			}
		}
		SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
		void Finish()
		{
			Mark("연출 끝");
			foreach (BagConfig bag in Config.Bags)
			{
				BagWindow bagWindow = new BagWindow(bag);
				BagWindows.Add(bagWindow);
				bagWindow.Show();
				if (AllPanelsOpen)
				{
					bagWindow.ForceOpen(value: true);
				}
			}
			if (!Config.FirstRunDone)
			{
				tray?.Balloon("DesktopPop이 켜졌어요!", "화면 왼쪽/오른쪽 끝에 마우스를 대 보세요." + (string.IsNullOrWhiteSpace(Config.ToggleHotkey) ? "" : "\n어디서든 " + Config.ToggleHotkey + " = 모두 펼치기/접기") + "\n가방은 시계 옆 아이콘 → '새 가방 만들기'");
				Config.FirstRunDone = true;
				Config.Save();
			}
			edgeTimer = new DispatcherTimer(DispatcherPriority.Input)
			{
				Interval = TimeSpan.FromMilliseconds(30L)
			};
			edgeTimer.Tick += (object? s, EventArgs ev) =>
			{
				EdgeTick();
			};
			edgeTimer.Start();
			DesktopBand.Install();
			KeyboardHook.Install();
			SetToggleHotkey(Config.ToggleHotkey, notifyOnFail: true);
			// 첫 우클릭 때 Windows 메뉴가 바로 뜨게 도우미를 미리 준비 (시작 연출과 겹치지 않게 조금 뒤에)
			Anim.After(4000.0, NativeShellMenu.Prewarm);
			if (!Config.LibrarySeeded)
			{
				Anim.After(3000.0, () =>
				{
					try
					{
						BagLibrary.EnsureSeeded(Config);
					}
					catch (Exception ex3)
					{
						Log(ex3);
					}
				});
			}
			Anim.After(3400.0, () =>
			{
				try
				{
					BagLibrary.EnsureCyberSeeded(Config);
				}
				catch (Exception ex3)
				{
					Log(ex3);
				}
				try
				{
					BagLibrary.EnsureBundledSeeded(Config);
				}
				catch (Exception ex4)
				{
					Log(ex4);
				}
			});
		}
	}

	private void EnsureIconsHidden()
	{
		EnsureIconsHidden(0);
	}

	private void EnsureIconsHidden(int attempt)
	{
		if (!Config.HideDesktopIcons || ShuttingDown)
		{
			return;
		}
		try
		{
			if (!DesktopRecovery.Arm())
			{
				return;
			}
			DesktopShell.SetIconsVisible(visible: false);
		}
		catch (Exception ex)
		{
			Log(ex);
		}
		if (DesktopShell.IconsVisible() != false && attempt < 30)
		{
			Anim.After(1000.0, () =>
			{
				EnsureIconsHidden(attempt + 1);
			});
		}
	}

	private void OnDisplayChanged(object? sender, EventArgs e)
	{
		Dispatcher.InvokeAsync(() =>
		{
			if (!ShuttingDown && LeftPanel != null)
			{
				ReconcilePanels();
				ScheduleRefresh();
			}
		});
	}

	private void ReconcilePanels()
	{
		IReadOnlyList<Native.MonitorInfo> monitors = Native.Monitors();
		Config.EnsurePanelLayout(monitors[0].DeviceName, monitors.Select((Native.MonitorInfo m) => m.DeviceName));
		foreach (EdgePanel panel in EdgePanels.ToList())
		{
			Native.MonitorInfo stillConnected = monitors.FirstOrDefault((Native.MonitorInfo m) => m.DeviceName == panel.Monitor.DeviceName);
			if (stillConnected == null)
			{
				panel.DisposePanel();
				EdgePanels.Remove(panel);
			}
			else
			{
				panel.UpdateMonitor(stillConnected);
			}
		}
		foreach (Native.MonitorInfo monitor in monitors)
		{
			foreach (Side side in new Side[2] { Side.Left, Side.Right })
			{
				if (!EdgePanels.Any((EdgePanel p) => p.Monitor.DeviceName == monitor.DeviceName && p.Side == side))
				{
					EdgePanel newPanel = new EdgePanel(side, monitor);
					EdgePanels.Add(newPanel);
					if (AllPanelsOpen)
					{
						newPanel.ForceOpen(value: true);
					}
				}
			}
		}
		LeftPanel = EdgePanels.First((EdgePanel p) => p.Side == Side.Left && p.Monitor.IsPrimary);
		RightPanel = EdgePanels.First((EdgePanel p) => p.Side == Side.Right && p.Monitor.IsPrimary);
		foreach (EdgePanel panel in EdgePanels)
		{
			panel.PrepareOpen();
		}
	}

	internal EdgePanel PanelNearCursor(Side side)
	{
		Native.GetCursorPos(out var p);
		Native.MonitorInfo monitor = Native.MonitorAt(p);
		EdgePanel? onThisMonitor = EdgePanels.FirstOrDefault((EdgePanel p2) => p2.Side == side && p2.Monitor.DeviceName == monitor.DeviceName);
		return onThisMonitor ?? ((side == Side.Left) ? LeftPanel : RightPanel);
	}

	internal string PanelLabel(EdgePanel panel)
	{
		int monitorNumber = (from m in EdgePanels.Select((EdgePanel p) => p.Monitor).DistinctBy((Native.MonitorInfo m) => m.DeviceName, StringComparer.OrdinalIgnoreCase)
			orderby m.IsPrimary descending, m.Bounds.Left, m.Bounds.Top
			select m).ToList().FindIndex((Native.MonitorInfo m) => Eq(m.DeviceName, panel.Monitor.DeviceName)) + 1;
		return $"모니터 {monitorNumber}{(panel.Monitor.IsPrimary ? " (주)" : "")} · {((panel.Side == Side.Left) ? "왼쪽" : "오른쪽")}";
	}

	internal void ToggleAllPanels()
	{
		SetAllPanelsOpen(!AllPanelsOpen);
	}

	internal void SetAllPanelsOpen(bool open, bool restoreWork = true)
	{
		if (AllPanelsOpen == open)
		{
			return;
		}
		ForegroundSnapshot foreground = WorkWindowAccess.Foreground();
		PanelClosePlan plan = default;
		if (open)
		{
			panelWorkReturn.Open(foreground);
		}
		else
		{
			plan = panelWorkReturn.Close(foreground, restoreWork);
		}
		AllPanelsOpen = open;
		if (open)
		{
			allPanelsDismissal.Open(Native.GetForegroundWindow(), Environment.TickCount64);
		}
		else
		{
			allPanelsDismissal.Close();
		}
		foreach (EdgePanel edgePanel in EdgePanels)
		{
			edgePanel.ForceOpen(open);
		}
		foreach (BagWindow bagWindow in BagWindows)
		{
			bagWindow.ForceOpen(open);
		}
		int version;
		if (!open)
		{
			WorkWindowAccess.Apply(plan, BagPlacements());
			version = allPanelsDismissal.Version;
			Anim.After(80.0, SettleBagOrder);
			Anim.After(240.0, SettleBagOrder);
		}
		void SettleBagOrder()
		{
			if (!ShuttingDown && !AllPanelsOpen && allPanelsDismissal.Version == version)
			{
				WorkWindowAccess.LowerBags(BagPlacements(onlyClosed: true));
			}
		}
	}

	private IEnumerable<BagWindowPlacement> BagPlacements(bool onlyClosed = false)
	{
		double scale = Native.DipScale();
		foreach (BagWindow bagWindow in BagWindows)
		{
			if (bagWindow.IsVisible && !bagWindow.IntentionalClose && (!onlyClosed || !bagWindow.BandSelectable))
			{
				yield return new BagWindowPlacement(new WindowInteropHelper(bagWindow).Handle, new Native.POINT
				{
					X = (int)Math.Round(bagWindow.Cfg.X * scale),
					Y = (int)Math.Round(bagWindow.Cfg.Y * scale)
				});
			}
		}
	}

	internal void DismissAllPanels()
	{
		if (AllPanelsOpen)
		{
			SetAllPanelsOpen(open: false, restoreWork: false);
		}
	}

	private static bool IsOurWindow(nint window)
	{
		if (window == IntPtr.Zero)
		{
			return false;
		}
		Native.GetWindowThreadProcessId(window, out var pid);
		return pid == (uint)Environment.ProcessId;
	}

	/// <summary>
	/// 이 화면 위치가 DesktopPop 의 "눌리는 부분"인지 (모두 펼친 상태에서 바깥을 클릭하면 접기 판단용).
	/// 판·가방 창의 투명한 빈 곳은 바깥으로 침.
	/// </summary>
	private bool IsOurPointerSurface(Native.POINT point)
	{
		nint hit = Native.WindowFromPoint(point);
		nint topLevel = Native.GetAncestor(hit, 2u); // GA_ROOT
		if (topLevel == IntPtr.Zero)
		{
			topLevel = hit;
		}
		if (!IsOurWindow(topLevel))
		{
			return false;
		}
		foreach (Window window in Windows)
		{
			if (!window.IsVisible || new WindowInteropHelper(window).Handle != topLevel)
			{
				continue;
			}
			if (window is EdgePanel || window is BagWindow)
			{
				IInputElement element = window.InputHitTest(window.PointFromScreen(new Point(point.X, point.Y)));
				return element != null && element != window && (!(window is BagWindow) || element != window.Content);
			}
			return true; // 메뉴·입력 창 같은 다른 DesktopPop 창
		}
		return true;
	}

	private void QueueDismissAllPanels()
	{
		int version = allPanelsDismissal.Version;
		if (queuedDismissVersion == version)
		{
			return;
		}
		queuedDismissVersion = version;
		Dispatcher.InvokeAsync(() =>
		{
			if (queuedDismissVersion == version)
			{
				queuedDismissVersion = -1;
			}
			if (AllPanelsOpen && version == allPanelsDismissal.Version && !KeepAllPanelsDuringInteraction)
			{
				DismissAllPanels();
			}
		}, DispatcherPriority.Input);
	}

	internal bool ObserveGlobalPointerInput(Native.POINT point)
	{
		if (!AllPanelsOpen)
		{
			return false;
		}
		if (!allPanelsDismissal.ShouldDismissInput(IsOurPointerSurface(point), KeepAllPanelsDuringInteraction))
		{
			return false;
		}
		QueueDismissAllPanels();
		return true;
	}

	internal void ObserveGlobalKeyboardInput(int virtualKey)
	{
		if (!AllPanelsOpen || OpenMenus > 0 || KeepAllPanelsDuringInteraction || GlobalHotkey.IsHotkeyKey(virtualKey))
		{
			return; // 펼치기 단축키 자체는 "다른 데서 키 누름"으로 치지 않음 (그건 단축키가 직접 접음)
		}
		// Shift·Ctrl·Alt·Win 만 누른 건 무시 (단축키를 누르기 시작한 것일 수 있음)
		bool modifierOnly = virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
		if (!modifierOnly)
		{
			nint foregroundWindow = Native.GetForegroundWindow();
			bool insideOurUi = IsOurWindow(foregroundWindow) || (Native.IsDesktopForeground() && Sel.Host != null && Sel.Keys.Count > 0);
			if (foregroundWindow != IntPtr.Zero && allPanelsDismissal.ShouldDismissInput(insideOurUi, interactionActive: false))
			{
				QueueDismissAllPanels();
			}
		}
	}

	/// <summary>펼치기/접기 단축키 바꾸기 (빈 문자열 = 끔). 다른 프로그램이 이미 쓰는 키면 false.</summary>
	internal bool SetToggleHotkey(string? combo, bool notifyOnFail)
	{
		combo = combo?.Trim() ?? "";
		bool ok = combo.Length == 0 || GlobalHotkey.Register(combo, ToggleAllPanels);
		if (combo.Length == 0)
		{
			GlobalHotkey.Unregister();
		}
		if (ok)
		{
			Config.ToggleHotkey = combo;
			Config.Save();
			Diag(combo.Length == 0 ? "  단축키 끔" : "  단축키 등록: " + combo);
		}
		else if (notifyOnFail)
		{
			tray?.Balloon("단축키를 쓸 수 없어요", combo + " 는 다른 프로그램이 이미 쓰고 있어요.\n시계 옆 아이콘 → '펼치기/접기 단축키'에서 다른 키를 골라 주세요.");
		}
		return ok;
	}

	/// <summary>Windows 우클릭 메뉴 도우미가 계속 튕겨서 간단 메뉴로 바꿨다고 알림.</summary>
	internal void NotifyWindowsMenuDisabled()
	{
		Log("Windows 우클릭 메뉴가 계속 실패해서 간단 메뉴로 바꿈");
		tray?.Balloon("우클릭 메뉴를 간단 메뉴로 바꿨어요", "이 컴퓨터에 설치된 어떤 프로그램의 메뉴 확장 때문에 Windows 메뉴를 띄울 수 없었어요.\n간단 메뉴의 'Windows 메뉴 (압축·보내기 등)...'로 다시 시도할 수 있어요.");
		tray?.RefreshChecks();
	}

	internal void OnBagLost(BagWindow w)
	{
		BagWindows.Remove(w);
		if (ShuttingDown || !Config.Bags.Contains(w.Cfg))
		{
			return;
		}
		DateTime now = DateTime.UtcNow;
		bagLosses.RemoveAll((DateTime t) => (now - t).TotalSeconds > 60.0);
		bagLosses.Add(now);
		Log($"가방 창이 예기치 않게 닫힘: {w.Cfg.Name} (최근 1분 {bagLosses.Count}회)");
		bool useOwner = bagLosses.Count <= 3;
		Anim.After(500.0, () =>
		{
			if (!ShuttingDown && Config.Bags.Contains(w.Cfg) && !BagWindows.Any((BagWindow b) => b.Cfg == w.Cfg))
			{
				BagWindow bagWindow = new BagWindow(w.Cfg, useOwner);
				BagWindows.Add(bagWindow);
				bagWindow.Show();
				if (AllPanelsOpen)
				{
					bagWindow.ForceOpen(value: true);
				}
			}
		});
	}

	private void EdgeTick()
	{
		if (!AllPanelsOpen)
		{
			panelWorkReturn.ObserveClosedForeground(WorkWindowAccess.Foreground());
		}
		if (AllPanelsOpen)
		{
			nint foregroundWindow = Native.GetForegroundWindow();
			if (allPanelsDismissal.ObserveForeground(foregroundWindow, IsOurWindow(foregroundWindow), KeepAllPanelsDuringInteraction || OpenMenus > 0, Environment.TickCount64))
			{
				DismissAllPanels();
			}
		}
		if (++tickCount % 66 == 0)
		{
			foreach (BagWindow item in BagWindows.ToList())
			{
				if (!item.IsVisible && !item.IntentionalClose)
				{
					try
					{
						item.Show();
					}
					catch (Exception ex)
					{
						Log(ex);
					}
				}
			}
		}
		if (!Native.GetCursorPos(out var p))
		{
			return;
		}
		bool leftButtonDown = (Native.GetAsyncKeyState(1) & 0x8000) != 0;
		if (DesktopBand.IsActive && !leftButtonDown)
		{
			DesktopBand.End(); // 안전장치: 버튼을 뗐는데 선택 네모가 남아 있으면 끝냄
		}
		DateTime utcNow = DateTime.UtcNow;
		foreach (EdgePanel edgePanel in EdgePanels)
		{
			edgePanel.Tick(p, leftButtonDown, utcNow);
		}
	}

	internal IEnumerable<ISelHost> SelHosts()
	{
		foreach (EdgePanel edgePanel in EdgePanels)
		{
			yield return edgePanel;
		}
		foreach (BagWindow bagWindow in BagWindows)
		{
			yield return bagWindow;
		}
	}

	internal static void Track(ContextMenu cm)
	{
		cm.Opened += (object s, RoutedEventArgs e) =>
		{
			OpenMenus++;
		};
		cm.Closed += (object s, RoutedEventArgs e) =>
		{
			OpenMenus = Math.Max(0, OpenMenus - 1);
		};
	}

	internal void ScheduleRefresh()
	{
		if (refreshQueued || ShuttingDown || LeftPanel == null)
		{
			return;
		}
		refreshQueued = true;
		Dispatcher.InvokeAsync(() =>
		{
			refreshQueued = false;
			try
			{
				HashSet<string> before = new HashSet<string>(Store.DesktopKeyList, StringComparer.OrdinalIgnoreCase);
				Store.Refresh(Config);
				Dictionary<string, bool> applied = ApplyPendingPlacements();
				Store.AssignNew(Config);
				foreach (EdgePanel edgePanel in EdgePanels)
				{
					edgePanel.RefreshItems();
				}
				foreach (BagWindow bagWindow in BagWindows)
				{
					bagWindow.RefreshItems();
				}
				Config.Save();
				// 바탕화면에 새로 들어온 게 있으면 그쪽 가장자리를 열고 보여 줌
				List<string> arrived = Store.DesktopKeyList.Where((string k) => !before.Contains(k)).ToList();
				if (arrived.Count > 0 && before.Count > 0)
				{
					RevealArrivals(arrived, applied);
				}
			}
			catch (Exception ex)
			{
				Log(ex);
			}
		}, DispatcherPriority.Background);
	}

	private static bool Eq(string a, string b)
	{
		return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	}

	private void RemoveEverywhere(string key)
	{
		Config.RemoveItemEverywhere(key);
	}

	/// <summary>폴더 이름을 바꿨을 때 설정·대기 중인 자리 정보에 들어 있는 그 안쪽 경로들을 새 경로로 고침.</summary>
	private void RemapPaths(string oldPath, string newPath)
	{
		bool changed = FileOperations.RemapConfigPaths(Config, oldPath, newPath);
		foreach (KeyValuePair<string, (PanelConfig, int?, bool)> pending in pendingPlace.ToList())
		{
			string remapped = FileOperations.RemapPath(pending.Key, oldPath, newPath);
			if (!(remapped == pending.Key))
			{
				pendingPlace.Remove(pending.Key);
				pendingPlace[remapped] = pending.Value;
				changed = true;
			}
		}
		if (Sel.Keys.Any((string k) => FileOperations.RemapPath(k, oldPath, newPath) != k))
		{
			Sel.Clear();
		}
		if (changed)
		{
			Config.Save();
		}
	}

	internal void MoveToSide(string key, Side side, int? visibleIndex)
	{
		MoveManyToSide(new string[1] { key }, side, visibleIndex);
	}

	internal void MoveManyToSide(IEnumerable<string> keys, Side side, int? visibleIndex)
	{
		MoveManyToPanel(keys, PanelNearCursor(side).Placement, visibleIndex);
	}

	internal void MoveManyToPanel(IEnumerable<string> keys, PanelConfig panel, int? visibleIndex)
	{
		List<string> ks = keys.Where(Store.IsOnDesktop).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		if (ks.Count == 0)
		{
			return;
		}
		// 놓은 칸 바로 뒤에 있던 (끌고 온 것 말고) 아이콘 앞에 끼워 넣음
		List<string> visibleKeys = (from i in Store.VisibleFor(panel)
			select i.Key).ToList();
		string? beforeKey = visibleIndex is int index
			? visibleKeys.Skip(index).FirstOrDefault((string k) => !ks.Contains(k, StringComparer.OrdinalIgnoreCase))
			: null;
		Config.MoveItemsToPanel(ks, panel, beforeKey);
		ScheduleRefresh();
	}

	/// <summary>새로 생긴 파일 하나를 판의 visibleIndex 번째 자리에 넣음 (없으면 맨 끝).</summary>
	private void PlaceOnPanel(string key, PanelConfig panel, int? visibleIndex)
	{
		List<string> others = (from i in Store.VisibleFor(panel)
			select i.Key into k
			where !Eq(k, key)
			select k).ToList();
		string? beforeKey = (visibleIndex is int index && index >= 0 && index < others.Count) ? others[index] : null;
		Config.MoveItemsToPanel(new string[1] { key }, panel, beforeKey);
	}

	/// <summary>
	/// 새로 생길 파일의 자리를 미리 받아 둔 것(pendingPlace) 중 이제 바탕화면에 나타난 것들을 그 자리에 놓음.
	/// 돌려주는 값: 자리를 잡은 파일 → 마우스 자리에서 날아오는 연출을 할지.
	/// </summary>
	private Dictionary<string, bool> ApplyPendingPlacements()
	{
		Dictionary<string, bool> placed = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, (PanelConfig, int?, bool)> pending in pendingPlace.ToList())
		{
			if (Store.IsOnDesktop(pending.Key))
			{
				pendingPlace.Remove(pending.Key);
				PlaceOnPanel(pending.Key, pending.Value.Item1, pending.Value.Item2);
				placed[pending.Key] = pending.Value.Item3;
			}
		}
		return placed;
	}

	/// <summary>
	/// 새로 들어온 아이콘이 있는 판을 열고 보여 줌. 바탕화면에서 만든 것(새 폴더 등)은 마우스 자리에서 날아오게.
	/// 그 모니터에서 게임·영상 전체화면 중이면 방해하지 않음.
	/// </summary>
	private void RevealArrivals(List<string> added, Dictionary<string, bool> applied)
	{
		Native.GetCursorPos(out var cursor);
		bool desktopFg = Native.IsDesktopForeground();
		double scale = Native.DipScale();
		Point from = new Point((double)cursor.X / scale, (double)cursor.Y / scale);
		foreach (EdgePanel edgePanel in EdgePanels)
		{
			if (Native.IsFullscreenAppOn(edgePanel.Bounds))
			{
				continue;
			}
			List<string> panelItems = edgePanel.Placement.Items;
			List<(string, bool)> mine = (from k in added
				where panelItems.Any((string x) => Eq(x, k))
				select (key: k, fly: applied.TryGetValue(k, out var fly) ? fly : desktopFg)).ToList();
			if (mine.Count > 0)
			{
				edgePanel.RevealArrivals(mine, from);
			}
		}
	}

	internal void BringFilesToDesktop(string[] files, bool copy, Side side, int? index)
	{
		BringFilesToDesktop(files, copy, PanelNearCursor(side).Placement, index);
	}

	internal void BringFilesToDesktop(string[] files, bool copy, PanelConfig panel, int? index)
	{
		TransferFiles(files, DesktopDir, copy, panel, index);
	}

	/// <summary>
	/// 파일들을 destDir 폴더로 옮기거나 복사 (탐색기와 같은 진행·확인 창 사용). 바탕화면으로 가져오는 거면
	/// 판의 index 번째 자리부터 차례로 놓이게 자리를 미리 받아 둠.
	/// </summary>
	internal void TransferFiles(string[] files, string destDir, bool copy, PanelConfig? panel = null, int? index = null, bool fly = false)
	{
		using (KeyboardHook.Suspend())
		{
			destDir = destDir.TrimEnd('\\');
			bool toDesktop = Eq(destDir, DesktopDir.TrimEnd('\\'));
			int placed = 0;
			foreach (string file in files)
			{
				try
				{
					string source = file.TrimEnd('\\');
					bool alreadyThere = Eq(Path.GetDirectoryName(source) ?? "", destDir);
					if (alreadyThere && !copy)
					{
						// 이미 바탕화면에 있는 파일: 옮길 건 없고 자리만 바꿈
						if (toDesktop && panel != null)
						{
							pendingPlace[source] = (panel, index is int i ? i + placed : null, fly);
						}
						placed++;
						continue;
					}
					if (Directory.Exists(source) && (Eq(source, destDir) || destDir.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase)))
					{
						continue; // 폴더를 자기 자신이나 자기 안으로는 못 넣음
					}
					bool isFolder = Directory.Exists(source);
					// 같은 폴더에 복사하면 "이름 - 복사본" 처럼 새 이름으로
					string target = ((copy & alreadyThere) ? FileOperations.UniqueCopyPath(source, destDir, isFolder) : Path.Combine(destDir, Path.GetFileName(source)));
					if (copy)
					{
						if (isFolder)
						{
							FileSystem.CopyDirectory(source, target, UIOption.AllDialogs);
						}
						else
						{
							FileSystem.CopyFile(source, target, UIOption.AllDialogs);
						}
					}
					else if (isFolder)
					{
						FileSystem.MoveDirectory(source, target, UIOption.AllDialogs);
					}
					else
					{
						FileSystem.MoveFile(source, target, UIOption.AllDialogs);
					}
					if (!copy)
					{
						RemoveEverywhere(source);
					}
					if (toDesktop && panel != null)
					{
						pendingPlace[target] = (panel, index is int i ? i + placed : null, fly);
					}
					placed++;
				}
				catch (OperationCanceledException)
				{
					// 사용자가 확인 창에서 취소
				}
				catch (Exception ex2)
				{
					Log(ex2);
					MessageBox.Show($"{(copy ? "복사" : "이동")}하지 못했어요:\n{file}\n\n{ex2.Message}", "DesktopPop");
				}
			}
			ScheduleRefresh();
		}
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

	internal void RecycleFiles(string[] paths, bool permanent = false)
	{
		using (KeyboardHook.Suspend())
		{
			List<string> list = (from p in paths
				select p.TrimEnd('\\') into p
				where !p.StartsWith("::") && (File.Exists(p) || Directory.Exists(p))
				select p).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (list.Count == 0)
			{
				return;
			}
			SHFILEOPSTRUCT op = new SHFILEOPSTRUCT
			{
				wFunc = 3u,
				pFrom = string.Join("\0", list) + "\0\0",
				fFlags = (ushort)(((!permanent) ? 64 : 0) | 0x4000)
			};
			try
			{
				if (SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted)
				{
					foreach (string item in list.Where((string p) => !File.Exists(p) && !Directory.Exists(p)))
					{
						RemoveEverywhere(item);
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show("삭제하지 못했어요.\n" + ex.Message, "DesktopPop");
			}
			ScheduleRefresh();
		}
	}

	private static List<string> SelectedPaths(ISelHost host)
	{
		return (from k in Sel.Ordered(host)
			where !k.StartsWith("::")
			select k).ToList();
	}

	internal void DeleteSelected(ISelHost host, bool permanent)
	{
		string[] paths = SelectedPaths(host).ToArray();
		if (paths.Length != 0)
		{
			Sel.Clear();
			RecycleFiles(paths, permanent);
		}
	}

	internal void RenameSelected(ISelHost host)
	{
		List<string> selected = Sel.Ordered(host);
		if (selected.Count == 1)
		{
			DesktopItem desktopItem = Store.Get(selected[0]);
			if (desktopItem != null && !desktopItem.IsVirtual)
			{
				RenameItem(desktopItem);
			}
		}
	}

	internal void OpenSelected(ISelHost host)
	{
		List<DesktopItem?> toOpen = (from i in Sel.Ordered(host).Select(Store.Get)
			where i != null
			select i).ToList();
		Sel.Clear();
		foreach (DesktopItem item in toOpen)
		{
			Shell.Launch(item);
		}
	}

	internal void CopySelected(ISelHost host, bool cut)
	{
		List<string> paths = SelectedPaths(host);
		if (paths.Count == 0)
		{
			return;
		}
		try
		{
			StringCollection fileList = new StringCollection();
			fileList.AddRange(paths.ToArray());
			DataObject dataObject = new DataObject();
			dataObject.SetFileDropList(fileList);
			// 탐색기가 "잘라내기(2)"인지 "복사(5)"인지 알 수 있게
			dataObject.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
			Clipboard.SetDataObject(dataObject, copy: true);
		}
		catch (Exception ex)
		{
			Log(ex);
		}
	}

	private static string UniqueDesktopName(string baseName)
	{
		string desktopDir = DesktopDir;
		if (!Exists(Path.Combine(desktopDir, baseName)))
		{
			return baseName;
		}
		int n = 2;
		string candidate;
		while (true)
		{
			candidate = $"{baseName} ({n})";
			if (!Exists(Path.Combine(desktopDir, candidate)))
			{
				break;
			}
			n++;
		}
		return candidate;
		static bool Exists(string p)
		{
			if (!File.Exists(p))
			{
				return Directory.Exists(p);
			}
			return true;
		}
	}

	internal void CreateNewFolder(Side side, int? index)
	{
		CreateNewFolder(PanelNearCursor(side).Placement, index);
	}

	internal void CreateNewFolder(PanelConfig panel, int? index)
	{
		string name = InputDialog.Ask("새 폴더", "폴더 이름을 입력해 주세요", UniqueDesktopName("새 폴더"));
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}
		if (!FileOperations.TryValidateName(name, out string error))
		{
			MessageBox.Show(error, "DesktopPop");
			return;
		}
		string path = Path.Combine(DesktopDir, name);
		if (File.Exists(path) || Directory.Exists(path))
		{
			MessageBox.Show("바탕화면에 같은 이름이 이미 있어요.", "DesktopPop");
			return;
		}
		try
		{
			Directory.CreateDirectory(path);
		}
		catch (Exception ex)
		{
			MessageBox.Show("폴더를 만들지 못했어요.\n" + ex.Message, "DesktopPop");
			return;
		}
		pendingPlace[path] = (panel, index, true); // 마우스 자리에서 그 칸으로 날아가게
		ScheduleRefresh();
	}

	internal static bool ClipboardHasFiles()
	{
		try
		{
			return Clipboard.ContainsFileDropList();
		}
		catch
		{
			return false;
		}
	}

	internal void PasteToDesktop(Side side, int? index)
	{
		PasteToDesktop(PanelNearCursor(side).Placement, index);
	}

	internal void PasteToDesktop(PanelConfig panel, int? index)
	{
		bool wasCut = false;
		string[] files;
		try
		{
			if (!Clipboard.ContainsFileDropList())
			{
				return;
			}
			files = Clipboard.GetFileDropList().Cast<string>().ToArray();
			// 탐색기에서 "잘라내기" 했으면 옮기기, "복사" 했으면 복사
			if (Clipboard.GetData("Preferred DropEffect") is MemoryStream effectStream)
			{
				byte[] effect = new byte[4];
				if (effectStream.Read(effect, 0, 4) == 4)
				{
					wasCut = (BitConverter.ToInt32(effect, 0) & 2) != 0;
				}
			}
		}
		catch (Exception ex)
		{
			Log(ex);
			return;
		}
		BringFilesToDesktop(files, !wasCut, panel, index);
		if (wasCut)
		{
			try
			{
				Clipboard.Clear();
			}
			catch
			{
			}
		}
	}

	internal void OpenDesktopFolder()
	{
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "\"" + DesktopDir + "\"")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			Log(ex);
		}
	}

	internal void RenameItem(DesktopItem item)
	{
		if (item.IsVirtual)
		{
			return;
		}
		string fileName = Path.GetFileName(item.Path.TrimEnd('\\'));
		string newName = InputDialog.Ask("이름 바꾸기", "새 이름을 입력해 주세요", fileName);
		if (string.IsNullOrWhiteSpace(newName) || newName == fileName)
		{
			return;
		}
		if (!FileOperations.TryValidateName(newName, out string error))
		{
			MessageBox.Show(error, "DesktopPop");
			return;
		}
		// 바로 가기는 확장자(.lnk/.url)를 빼먹어도 알아서 붙여 줌
		string extension = Path.GetExtension(fileName);
		if ((Eq(extension, ".lnk") || Eq(extension, ".url")) && !newName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
		{
			newName += extension;
		}
		string newPath = Path.Combine(Path.GetDirectoryName(item.Path.TrimEnd('\\')), newName);
		try
		{
			if (Directory.Exists(item.Path))
			{
				Directory.Move(item.Path, newPath);
			}
			else
			{
				File.Move(item.Path, newPath);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("이름을 바꾸지 못했어요.\n" + ex.Message, "DesktopPop");
			return;
		}
		RemapPaths(item.Path, newPath); // 판·가방의 자리는 그대로
		ScheduleRefresh();
	}

	internal void DeleteItem(DesktopItem item)
	{
		if (!item.IsVirtual)
		{
			RecycleFiles(new string[1] { item.Path });
		}
	}

	internal void MoveToBag(string key, BagConfig bag)
	{
		MoveManyToBag(new string[1] { key }, bag);
	}

	internal void MoveManyToBag(IEnumerable<string> keys, BagConfig bag)
	{
		List<string> list = keys.Where((string k) => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		foreach (string item in list)
		{
			RemoveEverywhere(item);
		}
		bag.Items.AddRange(list);
		ScheduleRefresh();
	}

	internal void TakeOutManyToSide(List<string> keys, BagConfig bag, Side side, Point from)
	{
		EdgePanel panel = PanelNearCursor(side);
		List<string> desk = keys.Where(Store.IsOnDesktop).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		foreach (string k in keys.Where((string key) => !Store.IsOnDesktop(key)))
		{
			bag.Items.RemoveAll((string x) => Eq(x, k));
		}
		Config.MoveItemsToPanel(desk, panel.Placement);
		ScheduleRefresh();
		if (desk.Count == 0)
		{
			return;
		}
		Dispatcher.InvokeAsync(() =>
		{
			panel.RevealArrivals(desk.Select((string item) => (k: item, true)).ToList(), from);
		}, DispatcherPriority.ContextIdle);
	}

	internal void TakeOutOfBag(string key, BagConfig bag)
	{
		bag.Items.RemoveAll((string k) => Eq(k, key));
		if (Store.IsOnDesktop(key))
		{
			Config.MoveItemsToPanel(new string[1] { key }, PanelNearCursor(Side.Left).Placement);
		}
		ScheduleRefresh();
	}

	internal void DeleteBag(BagWindow w)
	{
		Config.Bags.Remove(w.Cfg);
		BagWindows.Remove(w);
		w.IntentionalClose = true;
		w.Close();
		ScheduleRefresh();
	}

	internal void CreateBag(IReadOnlyList<string>? firstKeys)
	{
		Native.GetCursorPos(out var p);
		Rect workArea = Native.ToDipRect(Native.MonitorAt(p).WorkArea); // 만들기를 누른 모니터 가운데쯤에
		// 가방 그림 보관함에서 그림 고르고 이름 정하기
		(string, string)? picked = BagLibraryWindow.Pick(createMode: true, "가방");
		if (!picked.HasValue)
		{
			return;
		}
		var (imagePath, bagName) = picked.Value;
		BagConfig bagConfig = new BagConfig
		{
			Name = bagName,
			ImagePath = imagePath,
			X = workArea.Left + workArea.Width / 2.0 + (double)(Config.Bags.Count % 4 * 40),
			Y = workArea.Top + workArea.Height / 2.0 + (double)(Config.Bags.Count % 4 * 40)
		};
		Config.Bags.Add(bagConfig);
		foreach (string key in firstKeys ?? Array.Empty<string>())
		{
			RemoveEverywhere(key);
			bagConfig.Items.Add(key);
		}
		Store.Refresh(Config);
		BagWindow bagWindow = new BagWindow(bagConfig);
		BagWindows.Add(bagWindow);
		bagWindow.Show();
		if (AllPanelsOpen)
		{
			bagWindow.ForceOpen(value: true);
		}
		ScheduleRefresh();
	}

	internal void SetTheme(string? accent, string? panel)
	{
		// 기본 색이면 비워 둠 (나중에 기본 색이 바뀌면 따라가게)
		bool isDefault = string.Equals(accent, "#FF6EB4", StringComparison.OrdinalIgnoreCase) && string.Equals(panel, "#1A1026", StringComparison.OrdinalIgnoreCase);
		Config.AccentColor = (isDefault ? null : accent);
		Config.PanelColor = (isDefault ? null : panel);
		Theme.Apply(Config.AccentColor, Config.PanelColor);
		Config.Save();
		if (LeftPanel == null)
		{
			return;
		}
		foreach (EdgePanel edgePanel in EdgePanels)
		{
			edgePanel.ApplyTheme();
		}
		foreach (BagWindow bagWindow in BagWindows)
		{
			bagWindow.Rebuild();
		}
		PanelNearCursor(Side.Left).Peek();
	}

	internal void SetHideDesktopIcons(bool hide)
	{
		Config.HideDesktopIcons = hide;
		Config.Save();
		if (hide)
		{
			EnsureIconsHidden();
		}
		else
		{
			DesktopShell.SetIconsVisible(visible: true);
		}
	}

	private bool ImportLayout()
	{
		List<DesktopShell.IconPosition>? positions = DesktopShell.ReadIconPositions();
		if (positions == null || positions.Count == 0)
		{
			return false;
		}
		// 전체 경로로 먼저 맞추고, 못 찾으면 (이름이 겹치지 않을 때만) 보이는 이름으로 맞춤
		Dictionary<string, Native.POINT> byPath = new Dictionary<string, Native.POINT>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, Native.POINT> byName = new Dictionary<string, Native.POINT>(StringComparer.OrdinalIgnoreCase);
		foreach (DesktopShell.IconPosition pos in positions)
		{
			if (pos.Path.Length > 0)
			{
				byPath.TryAdd(pos.Path, pos.Point);
			}
			if (pos.Name.Length > 0)
			{
				byName.TryAdd(pos.Name, pos.Point);
			}
		}
		IReadOnlyList<Native.MonitorInfo> monitors = Native.Monitors();
		Config.EnsurePanelLayout(monitors[0].DeviceName, monitors.Select((Native.MonitorInfo m) => m.DeviceName));
		HashSet<string> connected = monitors.Select((Native.MonitorInfo m) => m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
		HashSet<string> inBags = new HashSet<string>(Config.Bags.SelectMany((BagConfig b) => b.Items), StringComparer.OrdinalIgnoreCase);
		// 지금 연결 안 된 모니터의 판에 있는 것은 건드리지 않음
		HashSet<string> onDisconnectedMonitors = Config.Panels.Where((PanelConfig pc) => !connected.Contains(pc.MonitorId)).SelectMany((PanelConfig pc) => pc.Items).ToHashSet(StringComparer.OrdinalIgnoreCase);
		Dictionary<PanelConfig, List<(string, int, int)>> byPanel = new Dictionary<PanelConfig, List<(string, int, int)>>();
		HashSet<string> duplicateNames = (from g in Store.DesktopItems.GroupBy((DesktopItem i) => i.Name, StringComparer.OrdinalIgnoreCase)
			where g.Count() > 1
			select g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (DesktopItem desktopItem in Store.DesktopItems)
		{
			if (inBags.Contains(desktopItem.Key) || onDisconnectedMonitors.Contains(desktopItem.Key))
			{
				continue;
			}
			if (!byPath.TryGetValue(desktopItem.Key, out var p) && (duplicateNames.Contains(desktopItem.Name) || !byName.TryGetValue(desktopItem.Name, out p)))
			{
				continue;
			}
			Native.MonitorInfo onMonitor = monitors.FirstOrDefault((Native.MonitorInfo m) => Native.Contains(m.Bounds, p));
			if (!(onMonitor == null))
			{
				// 그 모니터의 왼쪽 절반에 있던 건 왼쪽 판, 오른쪽 절반은 오른쪽 판
				Side side = ((!((double)(p.X + 40) < (double)(onMonitor.Bounds.Left + onMonitor.Bounds.Right) / 2.0)) ? Side.Right : Side.Left);
				PanelConfig panel = Config.GetPanel(onMonitor.DeviceName, side);
				if (!byPanel.TryGetValue(panel, out var placed))
				{
					placed = (byPanel[panel] = new List<(string, int, int)>());
				}
				placed.Add((desktopItem.Key, p.X, p.Y));
			}
		}
		if (byPanel.Count == 0)
		{
			return false;
		}
		foreach (string item in from item in byPanel.Values.SelectMany((List<(string key, int x, int y)> items) => items)
			select item.key)
		{
			RemoveEverywhere(item);
		}
		foreach (var (panelConfig, source) in byPanel)
		{
			// 원래 바탕화면처럼 가장자리 쪽 줄부터, 줄 안에서는 위에서 아래로
			IOrderedEnumerable<(string, int, int)> ordered = ((panelConfig.Side == Side.Left) ? source.OrderBy(((string key, int x, int y) t) => t.x).ThenBy(((string key, int x, int y) t) => t.y) : source.OrderByDescending(((string key, int x, int y) t) => t.x).ThenBy(((string key, int x, int y) t) => t.y));
			panelConfig.Items.InsertRange(0, ordered.Select(((string key, int x, int y) t) => t.key));
		}
		return true;
	}

	internal void RereadLayout()
	{
		bool imported;
		try
		{
			imported = ImportLayout();
		}
		catch (Exception ex)
		{
			Log(ex);
			imported = false;
		}
		if (!imported)
		{
			MessageBox.Show("바탕화면 배치를 읽지 못했어요.\n'원래 바탕화면 아이콘 숨기기'를 잠깐 끄고 다시 시도해 보세요.", "DesktopPop");
			return;
		}
		ScheduleRefresh();
		MessageBox.Show("원래 바탕화면 배치를 각 모니터의 왼쪽·오른쪽 패널로 가져왔어요.\n이름이 겹쳐 구분하기 어려운 항목과 연결이 끊긴 모니터의 배치는 유지했어요.", "DesktopPop");
	}

	internal void Quit()
	{
		Diag("  종료 메뉴로 끔");
		ShuttingDown = true;
		try
		{
			if (Config.HideDesktopIcons)
			{
				DesktopShell.SetIconsVisible(visible: true);
			}
		}
		catch
		{
		}
		Shutdown();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		Diag($"  종료 (코드 {e.ApplicationExitCode}, 종료 메뉴/윈도우 종료={ShuttingDown})");
		ShuttingDown = true;
		edgeTimer?.Stop();
		fsDebounce?.Stop();
		DesktopRecovery.Restore();
		DesktopBand.Uninstall();
		KeyboardHook.Uninstall();
		GlobalHotkey.Dispose();
		NativeShellMenu.Shutdown();
		SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
		foreach (FileSystemWatcher watcher in watchers)
		{
			watcher.Dispose();
		}
		tray?.Dispose();
		Config?.Save();
		base.OnExit(e);
	}

	internal static void Diag(string msg)
	{
		try
		{
			Directory.CreateDirectory(AppConfig.Dir);
			File.AppendAllText(Path.Combine(AppConfig.Dir, "startup-log.txt"), $"[{DateTime.Now}] {msg}\n");
		}
		catch (Exception ex)
		{
			Log(ex);
		}
	}

	internal static void Log(string msg)
	{
		try
		{
			Directory.CreateDirectory(AppConfig.Dir);
			File.AppendAllText(Path.Combine(AppConfig.Dir, "error.log"), $"[{DateTime.Now}] {msg}\n");
		}
		catch
		{
		}
	}

	internal static void Log(Exception ex)
	{
		try
		{
			Directory.CreateDirectory(AppConfig.Dir);
			File.AppendAllText(Path.Combine(AppConfig.Dir, "error.log"), $"[{DateTime.Now}] {ex}\n\n");
		}
		catch
		{
		}
	}
}
