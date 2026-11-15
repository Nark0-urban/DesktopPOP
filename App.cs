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

	private PanelButton? panelButton;

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
		if (DesktopRecovery.TryRunHelper(args) || Shell.TryRunHelper(args))
		{
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

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
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
		IReadOnlyList<Native.MonitorInfo> readOnlyList = Native.Monitors();
		Config.EnsurePanelLayout(readOnlyList[0].DeviceName, readOnlyList.Select((Native.MonitorInfo m) => m.DeviceName));
		Theme.Apply(Config.AccentColor, Config.PanelColor);
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
			SetPanelButtonVisible(Config.ShowPanelButton);
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
				panelButton?.KeepOnScreen();
				ScheduleRefresh();
			}
		});
	}

	private void ReconcilePanels()
	{
		IReadOnlyList<Native.MonitorInfo> readOnlyList = Native.Monitors();
		Config.EnsurePanelLayout(readOnlyList[0].DeviceName, readOnlyList.Select((Native.MonitorInfo m) => m.DeviceName));
		foreach (EdgePanel panel in EdgePanels.ToList())
		{
			Native.MonitorInfo monitorInfo = readOnlyList.FirstOrDefault((Native.MonitorInfo m) => m.DeviceName == panel.Monitor.DeviceName);
			if (monitorInfo == null)
			{
				panel.DisposePanel();
				EdgePanels.Remove(panel);
			}
			else
			{
				panel.UpdateMonitor(monitorInfo);
			}
		}
		foreach (Native.MonitorInfo monitor in readOnlyList)
		{
			Side[] array = new Side[2]
			{
				Side.Left,
				Side.Right
			};
			foreach (Side side in array)
			{
				if (!EdgePanels.Any((EdgePanel p) => p.Monitor.DeviceName == monitor.DeviceName && p.Side == side))
				{
					EdgePanel edgePanel = new EdgePanel(side, monitor);
					EdgePanels.Add(edgePanel);
					if (AllPanelsOpen)
					{
						edgePanel.ForceOpen(value: true);
					}
				}
			}
		}
		LeftPanel = EdgePanels.First((EdgePanel p) => p.Side == Side.Left && p.Monitor.IsPrimary);
		RightPanel = EdgePanels.First((EdgePanel p) => p.Side == Side.Right && p.Monitor.IsPrimary);
		foreach (EdgePanel edgePanel2 in EdgePanels)
		{
			edgePanel2.PrepareOpen();
		}
	}

	internal EdgePanel PanelNearCursor(Side side)
	{
		Native.GetCursorPos(out var p);
		Native.MonitorInfo monitor = Native.MonitorAt(p);
		EdgePanel edgePanel = EdgePanels.FirstOrDefault((EdgePanel edgePanel2) => edgePanel2.Side == side && edgePanel2.Monitor.DeviceName == monitor.DeviceName);
		if (edgePanel == null)
		{
			if (side != Side.Left)
			{
				return RightPanel;
			}
			edgePanel = LeftPanel;
		}
		return edgePanel;
	}

	internal string PanelLabel(EdgePanel panel)
	{
		int value = (from m in EdgePanels.Select((EdgePanel p) => p.Monitor).DistinctBy((Native.MonitorInfo m) => m.DeviceName, StringComparer.OrdinalIgnoreCase)
			orderby m.IsPrimary descending, m.Bounds.Left, m.Bounds.Top
			select m).ToList().FindIndex((Native.MonitorInfo m) => Eq(m.DeviceName, panel.Monitor.DeviceName)) + 1;
		return $"모니터 {value}{(panel.Monitor.IsPrimary ? " (주)" : "")} · {((panel.Side == Side.Left) ? "왼쪽" : "오른쪽")}";
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
		panelButton?.UpdateState();
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

	private bool IsOurPointerSurface(Native.POINT point)
	{
		nint num = Native.WindowFromPoint(point);
		nint num2 = Native.GetAncestor(num, 2u);
		if (num2 == IntPtr.Zero)
		{
			num2 = num;
		}
		if (!IsOurWindow(num2))
		{
			return false;
		}
		bool flag;
		foreach (Window window in Windows)
		{
			if (!window.IsVisible || new WindowInteropHelper(window).Handle != num2)
			{
				continue;
			}
			flag = ((window is EdgePanel || window is BagWindow || window is PanelButton) ? true : false);
			if (flag)
			{
				IInputElement inputElement = window.InputHitTest(window.PointFromScreen(new Point(point.X, point.Y)));
				flag = inputElement != null && inputElement != window && (!(window is BagWindow) || inputElement != window.Content);
			}
			else
			{
				flag = true;
			}
			goto IL_00ee;
		}
		return true;
		IL_00ee:
		return flag;
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
		bool flag;
		switch (virtualKey)
		{
		case 16:
		case 17:
		case 18:
		case 91:
		case 92:
		case 160:
		case 161:
		case 162:
		case 163:
		case 164:
		case 165:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
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

	internal void SetPanelButtonVisible(bool visible)
	{
		Config.ShowPanelButton = visible;
		Config.Save();
		if (visible)
		{
			if (panelButton == null)
			{
				panelButton = new PanelButton(this);
			}
			panelButton.Show();
		}
		else
		{
			panelButton?.Hide();
		}
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
			panelButton?.EnsureTopmost();
		}
		if (!Native.GetCursorPos(out var p))
		{
			return;
		}
		bool flag = (Native.GetAsyncKeyState(1) & 0x8000) != 0;
		if (DesktopBand.IsActive && !flag)
		{
			DesktopBand.End();
		}
		DateTime utcNow = DateTime.UtcNow;
		foreach (EdgePanel edgePanel in EdgePanels)
		{
			edgePanel.Tick(p, flag, utcNow);
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
				List<string> list = Store.DesktopKeyList.Where((string k) => !before.Contains(k)).ToList();
				if (list.Count > 0 && before.Count > 0)
				{
					RevealArrivals(list, applied);
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

	private void RemapPaths(string oldPath, string newPath)
	{
		bool flag = FileOperations.RemapConfigPaths(Config, oldPath, newPath);
		foreach (KeyValuePair<string, (PanelConfig, int?, bool)> item in pendingPlace.ToList())
		{
			string text = FileOperations.RemapPath(item.Key, oldPath, newPath);
			if (!(text == item.Key))
			{
				pendingPlace.Remove(item.Key);
				pendingPlace[text] = item.Value;
				flag = true;
			}
		}
		if (Sel.Keys.Any((string k) => FileOperations.RemapPath(k, oldPath, newPath) != k))
		{
			Sel.Clear();
		}
		if (flag)
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
		List<string> source = (from i in Store.VisibleFor(panel)
			select i.Key).ToList();
		object obj;
		if (visibleIndex.HasValue)
		{
			int valueOrDefault = visibleIndex.GetValueOrDefault();
			obj = source.Skip(valueOrDefault).FirstOrDefault((string k) => !ks.Contains(k, StringComparer.OrdinalIgnoreCase));
		}
		else
		{
			obj = null;
		}
		string beforeKey = (string)obj;
		Config.MoveItemsToPanel(ks, panel, beforeKey);
		ScheduleRefresh();
	}

	private void PlaceOnPanel(string key, PanelConfig panel, int? visibleIndex)
	{
		List<string> list = (from i in Store.VisibleFor(panel)
			select i.Key into k
			where !Eq(k, key)
			select k).ToList();
		object obj;
		if (visibleIndex.HasValue)
		{
			int valueOrDefault = visibleIndex.GetValueOrDefault();
			if (valueOrDefault >= 0 && valueOrDefault < list.Count)
			{
				obj = list[valueOrDefault];
				goto IL_007c;
			}
		}
		obj = null;
		goto IL_007c;
		IL_007c:
		string beforeKey = (string)obj;
		Config.MoveItemsToPanel(new string[1] { key }, panel, beforeKey);
	}

	private Dictionary<string, bool> ApplyPendingPlacements()
	{
		Dictionary<string, bool> dictionary = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		foreach (KeyValuePair<string, (PanelConfig, int?, bool)> item in pendingPlace.ToList())
		{
			if (Store.IsOnDesktop(item.Key))
			{
				pendingPlace.Remove(item.Key);
				PlaceOnPanel(item.Key, item.Value.Item1, item.Value.Item2);
				dictionary[item.Key] = item.Value.Item3;
			}
		}
		return dictionary;
	}

	private void RevealArrivals(List<string> added, Dictionary<string, bool> applied)
	{
		Native.GetCursorPos(out var p);
		bool desktopFg = Native.IsDesktopForeground();
		double num = Native.DipScale();
		Point point = new Point((double)p.X / num, (double)p.Y / num);
		foreach (EdgePanel edgePanel in EdgePanels)
		{
			if (Native.ShouldBlock(edgePanel.Bounds, blockFullscreen: true, blockMaximized: false))
			{
				continue;
			}
			List<string> list = edgePanel.Placement.Items;
			List<(string, bool)> list2 = (from k in added
				where list.Any((string x) => Eq(x, k))
				select (key: k, fly: applied.TryGetValue(k, out var value) ? value : desktopFg)).ToList();
			if (list2.Count > 0)
			{
				edgePanel.RevealArrivals(list2, point);
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

	internal void TransferFiles(string[] files, string destDir, bool copy, PanelConfig? panel = null, int? index = null, bool fly = false)
	{
		using (KeyboardHook.Suspend())
		{
			destDir = destDir.TrimEnd('\\');
			bool flag = Eq(destDir, DesktopDir.TrimEnd('\\'));
			int num = 0;
			foreach (string text in files)
			{
				try
				{
					string text2 = text.TrimEnd('\\');
					bool flag2 = Eq(Path.GetDirectoryName(text2) ?? "", destDir);
					if (flag2 && !copy)
					{
						if (flag && panel != null)
						{
							Dictionary<string, (PanelConfig panel, int? index, bool fly)> dictionary = pendingPlace;
							int? item;
							if (index.HasValue)
							{
								int valueOrDefault = index.GetValueOrDefault();
								item = valueOrDefault + num;
							}
							else
							{
								item = null;
							}
							dictionary[text2] = (panel, item, fly);
						}
						num++;
					}
					else
					{
						if (Directory.Exists(text2) && (Eq(text2, destDir) || destDir.StartsWith(text2 + "\\", StringComparison.OrdinalIgnoreCase)))
						{
							continue;
						}
						bool flag3 = Directory.Exists(text2);
						string text3 = ((copy & flag2) ? FileOperations.UniqueCopyPath(text2, destDir, flag3) : Path.Combine(destDir, Path.GetFileName(text2)));
						if (copy)
						{
							if (flag3)
							{
								FileSystem.CopyDirectory(text2, text3, UIOption.AllDialogs);
							}
							else
							{
								FileSystem.CopyFile(text2, text3, UIOption.AllDialogs);
							}
						}
						else if (flag3)
						{
							FileSystem.MoveDirectory(text2, text3, UIOption.AllDialogs);
						}
						else
						{
							FileSystem.MoveFile(text2, text3, UIOption.AllDialogs);
						}
						if (!copy)
						{
							RemoveEverywhere(text2);
						}
						if (flag && panel != null)
						{
							Dictionary<string, (PanelConfig panel, int? index, bool fly)> dictionary2 = pendingPlace;
							int? item2;
							if (index.HasValue)
							{
								int valueOrDefault2 = index.GetValueOrDefault();
								item2 = valueOrDefault2 + num;
							}
							else
							{
								item2 = null;
							}
							dictionary2[text3] = (panel, item2, fly);
						}
						num++;
						continue;
					}
				}
				catch (OperationCanceledException)
				{
				}
				catch (Exception ex2)
				{
					Log(ex2);
					MessageBox.Show($"{(copy ? "복사" : "이동")}하지 못했어요:\n{text}\n\n{ex2.Message}", "DesktopPop");
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
		string[] array = SelectedPaths(host).ToArray();
		if (array.Length != 0)
		{
			Sel.Clear();
			RecycleFiles(array, permanent);
		}
	}

	internal void RenameSelected(ISelHost host)
	{
		List<string> list = Sel.Ordered(host);
		if (list.Count == 1)
		{
			DesktopItem desktopItem = Store.Get(list[0]);
			if (desktopItem != null && !desktopItem.IsVirtual)
			{
				RenameItem(desktopItem);
			}
		}
	}

	internal void OpenSelected(ISelHost host)
	{
		List<DesktopItem?> list = (from i in Sel.Ordered(host).Select(Store.Get)
			where i != null
			select i).ToList();
		Sel.Clear();
		foreach (DesktopItem item in list)
		{
			Shell.Launch(item);
		}
	}

	internal void CopySelected(ISelHost host, bool cut)
	{
		List<string> list = SelectedPaths(host);
		if (list.Count == 0)
		{
			return;
		}
		try
		{
			StringCollection stringCollection = new StringCollection();
			stringCollection.AddRange(list.ToArray());
			DataObject dataObject = new DataObject();
			dataObject.SetFileDropList(stringCollection);
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
		int num = 2;
		string text;
		while (true)
		{
			text = $"{baseName} ({num})";
			if (!Exists(Path.Combine(desktopDir, text)))
			{
				break;
			}
			num++;
		}
		return text;
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
		string text = InputDialog.Ask("새 폴더", "폴더 이름을 입력해 주세요", UniqueDesktopName("새 폴더"));
		if (string.IsNullOrWhiteSpace(text))
		{
			return;
		}
		if (!FileOperations.TryValidateName(text, out string error))
		{
			MessageBox.Show(error, "DesktopPop");
			return;
		}
		string text2 = Path.Combine(DesktopDir, text);
		if (File.Exists(text2) || Directory.Exists(text2))
		{
			MessageBox.Show("바탕화면에 같은 이름이 이미 있어요.", "DesktopPop");
			return;
		}
		try
		{
			Directory.CreateDirectory(text2);
		}
		catch (Exception ex)
		{
			MessageBox.Show("폴더를 만들지 못했어요.\n" + ex.Message, "DesktopPop");
			return;
		}
		pendingPlace[text2] = (panel, index, true);
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
		bool flag = false;
		string[] files;
		try
		{
			if (!Clipboard.ContainsFileDropList())
			{
				return;
			}
			files = Clipboard.GetFileDropList().Cast<string>().ToArray();
			if (Clipboard.GetData("Preferred DropEffect") is MemoryStream memoryStream)
			{
				byte[] array = new byte[4];
				if (memoryStream.Read(array, 0, 4) == 4)
				{
					flag = (BitConverter.ToInt32(array, 0) & 2) != 0;
				}
			}
		}
		catch (Exception ex)
		{
			Log(ex);
			return;
		}
		BringFilesToDesktop(files, !flag, panel, index);
		if (flag)
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
		string text = InputDialog.Ask("이름 바꾸기", "새 이름을 입력해 주세요", fileName);
		if (string.IsNullOrWhiteSpace(text) || text == fileName)
		{
			return;
		}
		if (!FileOperations.TryValidateName(text, out string error))
		{
			MessageBox.Show(error, "DesktopPop");
			return;
		}
		string extension = Path.GetExtension(fileName);
		if ((Eq(extension, ".lnk") || Eq(extension, ".url")) && !text.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
		{
			text += extension;
		}
		string text2 = Path.Combine(Path.GetDirectoryName(item.Path.TrimEnd('\\')), text);
		try
		{
			if (Directory.Exists(item.Path))
			{
				Directory.Move(item.Path, text2);
			}
			else
			{
				File.Move(item.Path, text2);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show("이름을 바꾸지 못했어요.\n" + ex.Message, "DesktopPop");
			return;
		}
		RemapPaths(item.Path, text2);
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
		Rect rect = Native.ToDipRect(Native.MonitorAt(p).WorkArea);
		(string, string)? tuple = BagLibraryWindow.Pick(createMode: true, "가방");
		if (!tuple.HasValue)
		{
			return;
		}
		(string, string) value = tuple.Value;
		string item = value.Item1;
		string item2 = value.Item2;
		BagConfig bagConfig = new BagConfig
		{
			Name = item2,
			ImagePath = item,
			X = rect.Left + rect.Width / 2.0 + (double)(Config.Bags.Count % 4 * 40),
			Y = rect.Top + rect.Height / 2.0 + (double)(Config.Bags.Count % 4 * 40)
		};
		Config.Bags.Add(bagConfig);
		foreach (string item3 in firstKeys ?? Array.Empty<string>())
		{
			RemoveEverywhere(item3);
			bagConfig.Items.Add(item3);
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
		bool flag = string.Equals(accent, "#FF6EB4", StringComparison.OrdinalIgnoreCase) && string.Equals(panel, "#1A1026", StringComparison.OrdinalIgnoreCase);
		Config.AccentColor = (flag ? null : accent);
		Config.PanelColor = (flag ? null : panel);
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
		panelButton?.UpdateState();
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
		Dictionary<string, Native.POINT> dictionary = new Dictionary<string, Native.POINT>(StringComparer.OrdinalIgnoreCase);
		foreach (DesktopShell.IconPosition pos in positions)
		{
			if (pos.Path.Length > 0)
			{
				byPath.TryAdd(pos.Path, pos.Point);
			}
			if (pos.Name.Length > 0)
			{
				dictionary.TryAdd(pos.Name, pos.Point);
			}
		}
		IReadOnlyList<Native.MonitorInfo> readOnlyList = Native.Monitors();
		Config.EnsurePanelLayout(readOnlyList[0].DeviceName, readOnlyList.Select((Native.MonitorInfo m) => m.DeviceName));
		HashSet<string> connected = readOnlyList.Select((Native.MonitorInfo m) => m.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
		HashSet<string> hashSet = new HashSet<string>(Config.Bags.SelectMany((BagConfig b) => b.Items), StringComparer.OrdinalIgnoreCase);
		HashSet<string> hashSet2 = Config.Panels.Where((PanelConfig panelConfig3) => !connected.Contains(panelConfig3.MonitorId)).SelectMany((PanelConfig panelConfig3) => panelConfig3.Items).ToHashSet(StringComparer.OrdinalIgnoreCase);
		Dictionary<PanelConfig, List<(string, int, int)>> dictionary2 = new Dictionary<PanelConfig, List<(string, int, int)>>();
		HashSet<string> hashSet3 = (from g in Store.DesktopItems.GroupBy((DesktopItem i) => i.Name, StringComparer.OrdinalIgnoreCase)
			where g.Count() > 1
			select g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (DesktopItem desktopItem in Store.DesktopItems)
		{
			if (hashSet.Contains(desktopItem.Key) || hashSet2.Contains(desktopItem.Key))
			{
				continue;
			}
			if (!byPath.TryGetValue(desktopItem.Key, out var p) && (hashSet3.Contains(desktopItem.Name) || !dictionary.TryGetValue(desktopItem.Name, out p)))
			{
				continue;
			}
			Native.MonitorInfo monitorInfo = readOnlyList.FirstOrDefault((Native.MonitorInfo m) => Native.Contains(m.Bounds, p));
			if (!(monitorInfo == null))
			{
				Side side = ((!((double)(p.X + 40) < (double)(monitorInfo.Bounds.Left + monitorInfo.Bounds.Right) / 2.0)) ? Side.Right : Side.Left);
				PanelConfig panel = Config.GetPanel(monitorInfo.DeviceName, side);
				if (!dictionary2.TryGetValue(panel, out var value))
				{
					value = (dictionary2[panel] = new List<(string, int, int)>());
				}
				value.Add((desktopItem.Key, p.X, p.Y));
			}
		}
		if (dictionary2.Count == 0)
		{
			return false;
		}
		foreach (string item in from item in dictionary2.Values.SelectMany((List<(string key, int x, int y)> items) => items)
			select item.key)
		{
			RemoveEverywhere(item);
		}
		foreach (var (panelConfig2, source) in dictionary2)
		{
			IOrderedEnumerable<(string, int, int)> source2 = ((panelConfig2.Side == Side.Left) ? source.OrderBy(((string key, int x, int y) t) => t.x).ThenBy(((string key, int x, int y) t) => t.y) : source.OrderByDescending(((string key, int x, int y) t) => t.x).ThenBy(((string key, int x, int y) t) => t.y));
			panelConfig2.Items.InsertRange(0, source2.Select(((string key, int x, int y) t) => t.key));
		}
		return true;
	}

	internal void RereadLayout()
	{
		bool flag;
		try
		{
			flag = ImportLayout();
		}
		catch (Exception ex)
		{
			Log(ex);
			flag = false;
		}
		if (!flag)
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
