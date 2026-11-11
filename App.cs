using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DesktopPop;

public sealed class App : Application
{
    [STAThread]
    public static void Main()
    {
        using var mutex = new Mutex(true, "DesktopPop.SingleInstance.v1", out bool created);
        if (!created)
        {
            MessageBox.Show("DesktopPop이 이미 켜져 있어요.\n작업 표시줄 오른쪽 아래(시계 옆) 아이콘을 확인해 주세요.", "DesktopPop");
            return;
        }
        new App().Run();
    }

    public static App Instance => (App)Current;
    internal static int OpenMenus;
    internal static object? DragSource;

    internal AppConfig Config = null!;
    internal ItemStore Store = null!;
    internal EdgePanel LeftPanel = null!, RightPanel = null!;
    internal readonly List<BagWindow> BagWindows = new();

    Tray? tray;
    DispatcherTimer? edgeTimer, fsDebounce;
    readonly List<FileSystemWatcher> watchers = new();
    bool refreshQueued;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        DispatcherUnhandledException += (s, ev) => { Log(ev.Exception); ev.Handled = true; };

        Config = AppConfig.Load();
        Theme.Apply(Config.AccentColor, Config.PanelColor);
        Diag($"시작: 설정 폴더='{AppConfig.Dir}' 가방={Config.Bags.Count}개");

        SessionEnding += (s, ev) =>
        {
            Diag($"  윈도우 종료/로그아웃 감지 ({ev.ReasonSessionEnding})");
            ShuttingDown = true;
            // 컴퓨터를 끄거나 다시 시작할 땐 항상 원래 아이콘을 보이게 돌려 둠
            // → 다음 부팅 때는 평소 바탕화면이 보이고, 앱이 켜지면서 연출과 함께 숨겨짐
            if (Config.HideDesktopIcons) DesktopShell.SetIconsVisible(true);
            Config.Save();
        };

        tray = new Tray(this);

        // 1) 화면 가운데 원형 로딩부터 보여 주고
        StartupOverlay? overlay = null;
        if (Config.ShowStartupAnimation)
        {
            overlay = new StartupOverlay();
            overlay.Show();
            Mark("로딩 화면 표시");
        }
        // 2) 아이콘 읽기는 뒤에서 따로 (그동안 로딩 원이 멈추지 않고 계속 돎)
        var shownAt = bootClock.ElapsedMilliseconds;
        Store = new ItemStore();
        var loader = new Thread(() =>
        {
            try { Store.Refresh(Config); } catch (Exception ex) { Log(ex); }
            Dispatcher.InvokeAsync(() =>
            {
                // 로딩 원이 너무 순식간에 지나가지 않게 최소 0.9초는 보여 줌
                var wait = overlay == null ? 1 : Math.Max(1, 900 - (bootClock.ElapsedMilliseconds - shownAt));
                Anim.After(wait, () => InitAfterSplash(overlay));
            });
        }) { IsBackground = true, Name = "아이콘 읽기" };
        loader.SetApartmentState(ApartmentState.STA); // 윈도우 아이콘 읽기(셸)는 STA 필요
        loader.Start();
    }

    readonly System.Diagnostics.Stopwatch bootClock = System.Diagnostics.Stopwatch.StartNew();
    void Mark(string what) => Diag($"  {bootClock.ElapsedMilliseconds,5}ms {what}");

    void InitAfterSplash(StartupOverlay? overlay)
    {
        Mark("아이콘 읽기 끝");
        if (!Config.LayoutImported)
        {
            try { ImportLayout(); } catch (Exception ex) { Log(ex); }
            Config.LayoutImported = true;
        }
        Store.AssignNew(Config);
        Config.Save();

        LeftPanel = new EdgePanel(Side.Left);
        RightPanel = new EdgePanel(Side.Right);

        void Finish()
        {
            Mark("연출 끝");
            // 원래 아이콘을 숨긴 뒤에 가방을 띄움
            foreach (var b in Config.Bags)
            {
                var w = new BagWindow(b);
                BagWindows.Add(w);
                w.Show();
            }
            if (!Config.FirstRunDone)
            {
                tray?.Balloon("DesktopPop이 켜졌어요!",
                    "화면 왼쪽/오른쪽 끝에 마우스를 대 보세요.\n가방은 시계 옆 아이콘 → '새 가방 만들기'");
                Config.FirstRunDone = true;
                Config.Save();
            }
            edgeTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(30) };
            edgeTimer.Tick += (s, ev) => EdgeTick();
            edgeTimer.Start();
            DesktopBand.Install(); // 빈 바탕화면에서 끌면 선택 네모
            KeyboardHook.Install(); // 선택한 아이콘에 Delete, F2, Enter, Ctrl+A/C/X

            // 가방 그림 보관함을 처음 한 번 미리 채워 둠 (기본 그림 그리기)
            if (!Config.LibrarySeeded)
                Anim.After(3000, () => { try { BagLibrary.EnsureSeeded(Config); } catch (Exception ex) { Log(ex); } });
        }

        if (overlay != null)
        {
            // 3) 빛줄기가 화면을 훑으며 원래 아이콘이 사라지고 → 4) 양쪽 가장자리가 잠깐 열려 "여기로 갔어요"
            overlay.PlayScan(EnsureIconsHidden, () =>
            {
                overlay.Close();
                Finish();
                LeftPanel.Peek();
                RightPanel.Peek();
            });
        }
        else
        {
            EnsureIconsHidden();
            Finish();
        }

        fsDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        fsDebounce.Tick += (s, ev) => { fsDebounce.Stop(); ScheduleRefresh(); };
        foreach (var dir in ItemStore.DesktopDirs)
        {
            try
            {
                var w = new FileSystemWatcher(dir)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Attributes
                };
                FileSystemEventHandler h = (s, ev) => Dispatcher.InvokeAsync(() => { fsDebounce.Stop(); fsDebounce.Start(); });
                w.Created += h;
                w.Deleted += h;
                w.Changed += h;
                w.Renamed += (s, ev) => h(s, ev);
                w.EnableRaisingEvents = true;
                watchers.Add(w);
            }
            catch (Exception ex) { Log(ex); }
        }

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    /// <summary>
    /// 원래 바탕화면 아이콘 숨기기. 부팅 직후엔 바탕화면이 아직 준비 안 됐을 수 있어서
    /// 숨겨질 때까지 1초마다 최대 30번 다시 시도.
    /// </summary>
    void EnsureIconsHidden() => EnsureIconsHidden(0);

    void EnsureIconsHidden(int attempt)
    {
        if (!Config.HideDesktopIcons || ShuttingDown) return;
        try { DesktopShell.SetIconsVisible(false); } catch (Exception ex) { Log(ex); }
        if (DesktopShell.IconsVisible() != false && attempt < 30)
            Anim.After(1000, () => EnsureIconsHidden(attempt + 1));
    }

    void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(ScheduleRefresh);

    internal bool ShuttingDown;
    readonly List<DateTime> bagLosses = new();
    int tickCount;

    /// <summary>가방 창이 예기치 않게 닫혔을 때: 기록하고 다시 띄움.</summary>
    internal void OnBagLost(BagWindow w)
    {
        BagWindows.Remove(w);
        if (ShuttingDown || !Config.Bags.Contains(w.Cfg)) return;
        var now = DateTime.UtcNow;
        bagLosses.RemoveAll(t => (now - t).TotalSeconds > 60);
        bagLosses.Add(now);
        Log($"가방 창이 예기치 않게 닫힘: {w.Cfg.Name} (최근 1분 {bagLosses.Count}회)");
        // 계속 닫히면 바탕화면에 붙이는 방식을 포기하고 그냥 띄움
        bool useOwner = bagLosses.Count <= 3;
        Anim.After(500, () =>
        {
            if (ShuttingDown || !Config.Bags.Contains(w.Cfg) || BagWindows.Any(b => b.Cfg == w.Cfg)) return;
            var nw = new BagWindow(w.Cfg, useOwner);
            BagWindows.Add(nw);
            nw.Show();
        });
    }

    void EdgeTick()
    {
        // 2초마다 가방이 숨겨져 있으면 다시 보이게
        if (++tickCount % 66 == 0)
            foreach (var w in BagWindows.ToList())
                if (!w.IsVisible && !w.IntentionalClose) { try { w.Show(); } catch (Exception ex) { Log(ex); } }

        if (!Native.GetCursorPos(out var p)) return;
        var mon = Native.PrimaryMonitorRect();
        bool lbtn = (Native.GetAsyncKeyState(0x01) & 0x8000) != 0;
        if (DesktopBand.IsActive && !lbtn) DesktopBand.End(); // 안전장치: 버튼을 뗐는데 네모가 남아 있으면 끝냄
        var now = DateTime.UtcNow;
        LeftPanel.Tick(p, mon, lbtn, now);
        RightPanel.Tick(p, mon, lbtn, now);
    }

    /// <summary>선택 네모로 고를 수 있는 곳들.</summary>
    internal IEnumerable<ISelHost> SelHosts()
    {
        if (LeftPanel != null) yield return LeftPanel;
        if (RightPanel != null) yield return RightPanel;
        foreach (var w in BagWindows) yield return w;
    }

    internal static void Track(ContextMenu cm)
    {
        cm.Opened += (s, e) => OpenMenus++;
        cm.Closed += (s, e) => OpenMenus = Math.Max(0, OpenMenus - 1);
    }

    // ---------- 데이터 변경 ----------

    internal void ScheduleRefresh()
    {
        if (refreshQueued) return;
        refreshQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            refreshQueued = false;
            try
            {
                var before = new HashSet<string>(Store.DesktopKeyList, StringComparer.OrdinalIgnoreCase);
                Store.Refresh(Config);
                var applied = ApplyPendingPlacements();
                Store.AssignNew(Config);
                LeftPanel.MarkDirty();
                RightPanel.MarkDirty();
                foreach (var w in BagWindows) w.Rebuild();
                Config.Save();

                // 바탕화면에 새로 들어온 게 있으면 그쪽 가장자리를 열고 보여 줌
                var added = Store.DesktopKeyList.Where(k => !before.Contains(k)).ToList();
                if (added.Count > 0 && before.Count > 0) RevealArrivals(added, applied);
            }
            catch (Exception ex) { Log(ex); }
        }, DispatcherPriority.Background);
    }

    static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    void RemoveEverywhere(string key)
    {
        Config.Left.RemoveAll(k => Eq(k, key));
        Config.Right.RemoveAll(k => Eq(k, key));
        foreach (var b in Config.Bags) b.Items.RemoveAll(k => Eq(k, key));
    }

    internal void MoveToSide(string key, Side side, int? visibleIndex) => MoveManyToSide(new[] { key }, side, visibleIndex);

    /// <summary>여러 개를 한 가장자리의 visibleIndex 자리(없으면 맨 끝)에 순서대로 넣음.</summary>
    internal void MoveManyToSide(IEnumerable<string> keys, Side side, int? visibleIndex)
    {
        var ks = keys.Where(Store.IsOnDesktop).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); // 바탕화면 밖 파일은 가장자리에 못 둠
        if (ks.Count == 0) return;
        var full = Store.VisibleFor(Config, side).Select(i => i.Key).ToList();
        // 놓은 자리 바로 뒤에 있던(끌고 온 것 말고) 아이콘 앞에 끼워 넣음
        string? anchor = visibleIndex is int idx
            ? full.Skip(idx).FirstOrDefault(k => !ks.Contains(k, StringComparer.OrdinalIgnoreCase))
            : null;
        foreach (var k in ks) RemoveEverywhere(k);
        var list = side == Side.Left ? Config.Left : Config.Right;
        int at = anchor != null ? list.FindIndex(k => Eq(k, anchor)) : -1;
        if (at >= 0) list.InsertRange(at, ks); else list.AddRange(ks);
        ScheduleRefresh();
    }

    void PlaceOnSide(string key, Side side, int? visibleIndex)
    {
        var before = Store.VisibleFor(Config, side).Select(i => i.Key).Where(k => !Eq(k, key)).ToList();
        RemoveEverywhere(key);
        var list = side == Side.Left ? Config.Left : Config.Right;
        if (visibleIndex is int idx && idx < before.Count)
        {
            int at = list.FindIndex(k => Eq(k, before[idx]));
            if (at >= 0) list.Insert(at, key); else list.Add(key);
        }
        else list.Add(key);
    }

    // ---------- 바탕화면 파일 다루기 (원래 아이콘이 숨겨져 있어서 대신 해 줌) ----------

    internal static string DesktopDir => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>새로 생길 파일을 어느 가장자리 몇 번째에 둘지(날아가는 연출을 할지) 기억해 뒀다가, 파일이 생기면 그 자리에 넣음.</summary>
    readonly Dictionary<string, (Side side, int? index, bool fly)> pendingPlace = new(StringComparer.OrdinalIgnoreCase);

    /// <returns>이번에 자리를 잡은 파일 → 날아가는 연출 여부</returns>
    Dictionary<string, bool> ApplyPendingPlacements()
    {
        var applied = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in pendingPlace.ToList())
        {
            if (!Store.IsOnDesktop(kv.Key)) continue;
            pendingPlace.Remove(kv.Key);
            PlaceOnSide(kv.Key, kv.Value.side, kv.Value.index);
            applied[kv.Key] = kv.Value.fly;
        }
        return applied;
    }

    /// <summary>
    /// 새로 들어온 아이콘: 그쪽 가장자리를 열고 그 자리를 반짝 강조.
    /// 바탕화면 우클릭/새 폴더처럼 마우스 근처에서 생긴 건 마우스 위치에서 날아가게.
    /// </summary>
    void RevealArrivals(List<string> added, Dictionary<string, bool> applied)
    {
        var mon = Native.PrimaryMonitorRect();
        if (Native.ShouldBlock(mon, true, false)) return; // 게임·영상 전체화면 중엔 방해 안 함
        bool desktopFg = Native.IsDesktopForeground();
        Native.GetCursorPos(out var cp);
        double s = Native.DipScale();
        var from = new Point(cp.X / s, cp.Y / s);
        foreach (var side in new[] { Side.Left, Side.Right })
        {
            var list = side == Side.Left ? Config.Left : Config.Right;
            var mine = added
                .Where(k => list.Any(x => Eq(x, k)))
                .Select(k => (key: k, fly: applied.TryGetValue(k, out var f) ? f : desktopFg))
                .ToList();
            if (mine.Count > 0) (side == Side.Left ? LeftPanel : RightPanel).RevealArrivals(mine, from);
        }
    }

    /// <summary>탐색기에서 끌어온 파일(또는 붙여넣기)을 바탕화면으로 옮기거나 복사.</summary>
    internal void BringFilesToDesktop(string[] files, bool copy, Side side, int? index) =>
        TransferFiles(files, DesktopDir, copy, side, index);

    /// <summary>파일들을 destDir 폴더로 옮기거나 복사 (탐색기와 같은 확인 창 사용).</summary>
    internal void TransferFiles(string[] files, string destDir, bool copy, Side? side = null, int? index = null, bool fly = false)
    {
        destDir = destDir.TrimEnd('\\');
        bool toDesktop = Eq(destDir, DesktopDir.TrimEnd('\\'));
        int n = 0;
        foreach (var src in files)
        {
            try
            {
                var clean = src.TrimEnd('\\');
                if (Eq(Path.GetDirectoryName(clean) ?? "", destDir)) continue; // 이미 거기 있음
                if (Directory.Exists(clean) && (Eq(clean, destDir) || destDir.StartsWith(clean + "\\", StringComparison.OrdinalIgnoreCase)))
                    continue; // 폴더를 자기 안으로는 못 넣음
                var target = Path.Combine(destDir, Path.GetFileName(clean));
                bool isDir = Directory.Exists(clean);
                if (copy)
                {
                    if (isDir) Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(clean, target, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs);
                    else Microsoft.VisualBasic.FileIO.FileSystem.CopyFile(clean, target, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs);
                }
                else
                {
                    if (isDir) Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(clean, target, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs);
                    else Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(clean, target, Microsoft.VisualBasic.FileIO.UIOption.AllDialogs);
                }
                if (!copy) RemoveEverywhere(clean);
                if (toDesktop && side is Side sd) pendingPlace[target] = (sd, index is int i ? i + n : null, fly);
                n++;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log(ex);
                MessageBox.Show($"{(copy ? "복사" : "이동")}하지 못했어요:\n{src}\n\n{ex.Message}", "DesktopPop");
            }
        }
        ScheduleRefresh();
    }

    /// <summary>휴지통으로 보내기 (휴지통 아이콘에 끌어다 놓거나 우클릭 → 삭제).</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    /// <summary>
    /// 휴지통으로 보내기 (permanent = 완전 삭제). 원래 바탕화면처럼 여러 개라도 확인 창은 한 번.
    /// </summary>
    internal void RecycleFiles(string[] paths, bool permanent = false)
    {
        var list = paths.Select(p => p.TrimEnd('\\'))
            .Where(p => !p.StartsWith("::") && (File.Exists(p) || Directory.Exists(p)))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) return;
        var op = new SHFILEOPSTRUCT
        {
            wFunc = 3, // FO_DELETE
            pFrom = string.Join("\0", list) + "\0\0",
            fFlags = (ushort)((permanent ? 0 : 0x40 /*FOF_ALLOWUNDO = 휴지통*/) | 0x4000 /*FOF_WANTNUKEWARNING*/)
        };
        try
        {
            if (SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted)
                foreach (var p in list.Where(p => !File.Exists(p) && !Directory.Exists(p))) RemoveEverywhere(p);
        }
        catch (Exception ex) { MessageBox.Show($"삭제하지 못했어요.\n{ex.Message}", "DesktopPop"); }
        ScheduleRefresh();
    }

    // ---------- 선택된 아이콘 단축키 (KeyboardHook 에서 부름) ----------

    static List<string> SelectedPaths(ISelHost host) => Sel.Ordered(host).Where(k => !k.StartsWith("::")).ToList();

    internal void DeleteSelected(ISelHost host, bool permanent)
    {
        var paths = SelectedPaths(host).ToArray();
        if (paths.Length == 0) return;
        Sel.Clear();
        RecycleFiles(paths, permanent);
    }

    internal void RenameSelected(ISelHost host)
    {
        var keys = Sel.Ordered(host);
        if (keys.Count == 1 && Store.Get(keys[0]) is { IsVirtual: false } item) RenameItem(item);
    }

    internal void OpenSelected(ISelHost host)
    {
        var items = Sel.Ordered(host).Select(Store.Get).Where(i => i != null).ToList();
        Sel.Clear();
        foreach (var it in items) Shell.Launch(it!);
    }

    /// <summary>Ctrl+C / Ctrl+X: 탐색기에서 붙여넣을 수 있게 클립보드에.</summary>
    internal void CopySelected(ISelHost host, bool cut)
    {
        var paths = SelectedPaths(host);
        if (paths.Count == 0) return;
        try
        {
            var files = new System.Collections.Specialized.StringCollection();
            files.AddRange(paths.ToArray());
            var data = new DataObject();
            data.SetFileDropList(files);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
            Clipboard.SetDataObject(data, true);
        }
        catch (Exception ex) { Log(ex); }
    }

    static string UniqueDesktopName(string baseName)
    {
        var desk = DesktopDir;
        if (!Exists(Path.Combine(desk, baseName))) return baseName;
        for (int i = 2; ; i++)
        {
            var n = $"{baseName} ({i})";
            if (!Exists(Path.Combine(desk, n))) return n;
        }
        static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);
    }

    internal void CreateNewFolder(Side side, int? index)
    {
        var name = InputDialog.Ask("새 폴더", "폴더 이름을 입력해 주세요", UniqueDesktopName("새 폴더"));
        if (string.IsNullOrWhiteSpace(name)) return;
        var path = Path.Combine(DesktopDir, name);
        if (File.Exists(path) || Directory.Exists(path))
        {
            MessageBox.Show("바탕화면에 같은 이름이 이미 있어요.", "DesktopPop");
            return;
        }
        try { Directory.CreateDirectory(path); }
        catch (Exception ex)
        {
            MessageBox.Show($"폴더를 만들지 못했어요.\n{ex.Message}", "DesktopPop");
            return;
        }
        pendingPlace[path] = (side, index, true); // 마우스 위치에서 가장자리로 날아가게
        ScheduleRefresh();
    }

    internal static bool ClipboardHasFiles()
    {
        try { return Clipboard.ContainsFileDropList(); } catch { return false; }
    }

    internal void PasteToDesktop(Side side, int? index)
    {
        string[] files;
        bool move = false;
        try
        {
            if (!Clipboard.ContainsFileDropList()) return;
            files = Clipboard.GetFileDropList().Cast<string>().ToArray();
            // 탐색기에서 "잘라내기" 했으면 옮기기, "복사" 했으면 복사
            if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms)
            {
                var b = new byte[4];
                if (ms.Read(b, 0, 4) == 4) move = (BitConverter.ToInt32(b, 0) & 2) != 0;
            }
        }
        catch (Exception ex) { Log(ex); return; }
        BringFilesToDesktop(files, !move, side, index);
        if (move) { try { Clipboard.Clear(); } catch { } }
    }

    internal void OpenDesktopFolder()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{DesktopDir}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log(ex); }
    }

    internal void RenameItem(DesktopItem item)
    {
        if (item.IsVirtual) return;
        var cur = Path.GetFileName(item.Path.TrimEnd('\\'));
        var n = InputDialog.Ask("이름 바꾸기", "새 이름을 입력해 주세요", cur);
        if (string.IsNullOrWhiteSpace(n) || n == cur) return;
        // 바로 가기는 확장자를 빼먹어도 알아서 붙여 줌
        var ext = Path.GetExtension(cur);
        if ((Eq(ext, ".lnk") || Eq(ext, ".url")) && !n.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) n += ext;
        var target = Path.Combine(Path.GetDirectoryName(item.Path.TrimEnd('\\'))!, n);
        try
        {
            if (Directory.Exists(item.Path)) Directory.Move(item.Path, target);
            else File.Move(item.Path, target);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"이름을 바꾸지 못했어요.\n{ex.Message}", "DesktopPop");
            return;
        }
        // 자리는 그대로 유지
        foreach (var list in new[] { Config.Left, Config.Right }.Concat(Config.Bags.Select(b => b.Items)))
        {
            int i = list.FindIndex(k => Eq(k, item.Path));
            if (i >= 0) list[i] = target;
        }
        ScheduleRefresh();
    }

    internal void DeleteItem(DesktopItem item)
    {
        if (item.IsVirtual) return;
        RecycleFiles(new[] { item.Path });
    }

    internal void MoveToBag(string key, BagConfig bag) => MoveManyToBag(new[] { key }, bag);

    internal void MoveManyToBag(IEnumerable<string> keys, BagConfig bag)
    {
        var ks = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var k in ks) RemoveEverywhere(k);
        bag.Items.AddRange(ks);
        ScheduleRefresh();
    }

    /// <summary>
    /// 가방에서 끌어 꺼냄 → 가방 오른쪽에 놓았으면 오른쪽, 왼쪽이면 왼쪽 가장자리 맨 끝에 붙이고,
    /// 그 가장자리를 열어 놓은 자리(from)에서 날아가 앉으며 반짝이게.
    /// </summary>
    internal void TakeOutManyToSide(List<string> keys, BagConfig bag, Side side, Point from)
    {
        var desk = keys.Where(Store.IsOnDesktop).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var k in keys.Where(k => !Store.IsOnDesktop(k))) bag.Items.RemoveAll(x => Eq(x, k)); // 바탕화면 밖 파일은 가방에서만 빠짐
        foreach (var k in desk) RemoveEverywhere(k);
        (side == Side.Left ? Config.Left : Config.Right).AddRange(desk);
        ScheduleRefresh();
        if (desk.Count == 0) return;
        var panel = side == Side.Left ? LeftPanel : RightPanel;
        Dispatcher.InvokeAsync(() => panel.RevealArrivals(desk.Select(k => (k, true)).ToList(), from), DispatcherPriority.ContextIdle);
    }

    internal void TakeOutOfBag(string key, BagConfig bag)
    {
        bag.Items.RemoveAll(k => Eq(k, key));
        ScheduleRefresh(); // 바탕화면 파일이면 자동으로 왼쪽 가장자리에 다시 붙음
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
        // 가방 그림 보관함에서 그림 고르고 이름 정하기
        var pick = BagLibraryWindow.Pick(true, "가방");
        if (pick == null) return;
        var (img, name) = pick.Value;

        var wa = SystemParameters.WorkArea;
        var cfg = new BagConfig
        {
            Name = name,
            ImagePath = img,
            X = wa.Left + wa.Width / 2 + (Config.Bags.Count % 4) * 40,
            Y = wa.Top + wa.Height / 2 + (Config.Bags.Count % 4) * 40
        };
        Config.Bags.Add(cfg);
        foreach (var k in firstKeys ?? Array.Empty<string>())
        {
            RemoveEverywhere(k);
            cfg.Items.Add(k);
        }
        Store.Refresh(Config);
        var w = new BagWindow(cfg);
        BagWindows.Add(w);
        w.Show();
        ScheduleRefresh();
    }

    /// <summary>색상 바꾸기 → 바로 적용하고 왼쪽 가장자리를 잠깐 열어 미리 보여 줌.</summary>
    internal void SetTheme(string? accent, string? panel)
    {
        bool isDefault = string.Equals(accent, Theme.DefaultAccent, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(panel, Theme.DefaultPanel, StringComparison.OrdinalIgnoreCase);
        Config.AccentColor = isDefault ? null : accent;
        Config.PanelColor = isDefault ? null : panel;
        Theme.Apply(Config.AccentColor, Config.PanelColor);
        Config.Save();
        if (LeftPanel == null) return; // 아직 시작 중
        LeftPanel.ApplyTheme();
        RightPanel.ApplyTheme();
        foreach (var w in BagWindows) w.Rebuild();
        LeftPanel.Peek();
    }

    internal void SetHideDesktopIcons(bool hide)
    {
        Config.HideDesktopIcons = hide;
        Config.Save();
        DesktopShell.SetIconsVisible(!hide);
    }

    /// <summary>탐색기에 있는 바탕화면 아이콘 위치를 읽어 화면 왼쪽 절반 → 왼쪽, 오른쪽 절반 → 오른쪽으로 나눔.</summary>
    bool ImportLayout()
    {
        var pos = DesktopShell.ReadIconPositions();
        if (pos == null || pos.Count == 0) return false;
        var mon = Native.PrimaryMonitorRect();
        double mid = (mon.Left + mon.Right) / 2.0;
        var inBags = new HashSet<string>(Config.Bags.SelectMany(b => b.Items), StringComparer.OrdinalIgnoreCase);
        var left = new List<(string k, int x, int y)>();
        var right = new List<(string k, int x, int y)>();
        foreach (var it in Store.DesktopItems)
        {
            if (inBags.Contains(it.Key) || !pos.TryGetValue(it.Name, out var p)) continue;
            (p.X + 40 < mid ? left : right).Add((it.Key, p.X, p.Y));
        }
        if (left.Count + right.Count == 0) return false;
        Config.Left = left.OrderBy(t => t.x).ThenBy(t => t.y).Select(t => t.k).ToList();
        Config.Right = right.OrderByDescending(t => t.x).ThenBy(t => t.y).Select(t => t.k).ToList();
        return true;
    }

    internal void RereadLayout()
    {
        bool ok;
        try { ok = ImportLayout(); } catch (Exception ex) { Log(ex); ok = false; }
        if (!ok)
        {
            MessageBox.Show("바탕화면 배치를 읽지 못했어요.\n'원래 바탕화면 아이콘 숨기기'를 잠깐 끄고 다시 시도해 보세요.", "DesktopPop");
            return;
        }
        ScheduleRefresh();
        MessageBox.Show("원래 바탕화면에서 왼쪽에 있던 건 왼쪽, 오른쪽에 있던 건 오른쪽으로 다시 나눴어요.", "DesktopPop");
    }

    internal void Quit()
    {
        Diag("  종료 메뉴로 끔");
        ShuttingDown = true;
        try { if (Config.HideDesktopIcons) DesktopShell.SetIconsVisible(true); } catch { }
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Diag($"  종료 (코드 {e.ApplicationExitCode}, 종료 메뉴/윈도우 종료={ShuttingDown})");
        ShuttingDown = true;
        DesktopBand.Uninstall();
        KeyboardHook.Uninstall();
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        foreach (var w in watchers) w.Dispose();
        tray?.Dispose();
        Config?.Save();
        base.OnExit(e);
    }

    /// <summary>실행 파일 옆에 시작 기록 남기기 (저장 위치 문제 추적용).</summary>
    internal static void Diag(string msg)
    {
        try { Directory.CreateDirectory(AppConfig.Dir); File.AppendAllText(Path.Combine(AppConfig.Dir, "startup-log.txt"), $"[{DateTime.Now}] {msg}\n"); }
        catch (Exception ex) { Log(ex); }
    }

    internal static void Log(string msg)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.Dir);
            File.AppendAllText(Path.Combine(AppConfig.Dir, "error.log"), $"[{DateTime.Now}] {msg}\n");
        }
        catch { }
    }

    internal static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.Dir);
            File.AppendAllText(Path.Combine(AppConfig.Dir, "error.log"), $"[{DateTime.Now}] {ex}\n\n");
        }
        catch { }
    }
}
