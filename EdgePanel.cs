using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace DesktopPop;

/// <summary>화면 왼쪽/오른쪽 끝에 마우스를 대면 튀어나오는 아이콘 판.</summary>
internal sealed class EdgePanel : Window, ISelHost
{
    const double PadX = 6, PadY = 8, Fade = 40;
    readonly RubberBand band;

    public readonly Side Side;
    readonly Canvas canvas = new();
    readonly Border bg = new();
    readonly List<(Border tile, TranslateTransform tt, int col, int row, double off)> tiles = new();
    int rows = 1;
    bool shown;
    bool dirty = true;
    DateTime edgeSince = DateTime.MinValue, awaySince = DateTime.MinValue;
    IntPtr hwnd;
    int hideToken;

    public EdgePanel(Side side)
    {
        Side = side;
        Title = "DesktopPop Edge";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;

        ApplyThemeBackground();
        bg.Opacity = 0;

        var root = new Grid { AllowDrop = true };
        root.Children.Add(bg);
        root.Children.Add(canvas);
        DragEventHandler over = (s, e) =>
        {
            var keys = Tiles.KeysFrom(e.Data);
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            var target = DropTargetAt(e.GetPosition(canvas), keys, files);
            SetDropHover(target?.tile);
            var srcs = keys != null ? keys.Where(k => !k.StartsWith("::")).ToArray() : files;
            if (target is { } tg && srcs is { Length: > 0 })
                e.Effects = tg.item.Key == ItemStore.RecycleBinKey || !WantsCopy(e, srcs, tg.item.Path) ? DragDropEffects.Move : DragDropEffects.Copy;
            else if (keys != null) e.Effects = DragDropEffects.Move;          // 자리 바꾸기
            else if (files is { Length: > 0 })
                e.Effects = WantsCopy(e, files, App.DesktopDir) ? DragDropEffects.Copy : DragDropEffects.Move;
            else e.Effects = DragDropEffects.None;
            e.Effects &= e.AllowedEffects;
            e.Handled = true;
        };
        root.DragEnter += over;
        root.DragOver += over;
        root.DragLeave += (s, e) => SetDropHover(null);
        root.Drop += OnDrop;
        root.MouseRightButtonUp += (s, e) =>
        {
            // 빈 곳 우클릭 = 새 폴더 / 붙여넣기 / 바탕화면 폴더 열기
            var cm = BackgroundMenu(IndexAt(e.GetPosition(canvas)));
            App.Track(cm);
            cm.PlacementTarget = root;
            cm.IsOpen = true;
            e.Handled = true;
        };
        Content = root;
        // 빈 곳을 끌면 네모로 여러 개 선택, 그냥 클릭하면 선택 풀기
        band = new RubberBand(root, canvas, this) { OnClick = () => Sel.Clear(this) };

        SourceInitialized += (s, e) =>
        {
            hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(hwnd);
        };
    }

    // ---------- 여러 개 선택 ----------

    public IReadOnlyList<string> OrderedKeys => tiles.Select(t => ((DesktopItem)t.tile.Tag).Key).ToList();
    public bool BandSelectable => shown && IsVisible;

    public Border? TileOf(string key)
    {
        foreach (var t in tiles)
            if (t.tile.Tag is DesktopItem it && string.Equals(it.Key, key, StringComparison.OrdinalIgnoreCase)) return t.tile;
        return null;
    }

    public void ApplySelection()
    {
        foreach (var t in tiles) Tiles.SetSelected(t.tile, Sel.Contains(this, ((DesktopItem)t.tile.Tag).Key));
    }

    /// <summary>판 배경을 지금 테마 색으로 (가장자리 쪽이 진하고 안쪽으로 갈수록 투명).</summary>
    void ApplyThemeBackground()
    {
        bool left = Side == Side.Left;
        var c = Theme.Panel;
        var g = new LinearGradientBrush { StartPoint = new Point(left ? 0 : 1, 0.5), EndPoint = new Point(left ? 1 : 0, 0.5) };
        g.GradientStops.Add(new GradientStop(Theme.WithAlpha(c, 0xC0), 0));
        g.GradientStops.Add(new GradientStop(Theme.WithAlpha(c, 0x88), 0.72));
        g.GradientStops.Add(new GradientStop(Theme.WithAlpha(c, 0x02), 1));
        g.Freeze();
        bg.Background = g;
    }

    public void ApplyTheme()
    {
        ApplyThemeBackground();
        MarkDirty();
    }

    public void MarkDirty()
    {
        dirty = true;
        if (shown)
        {
            Layout();
            foreach (var t in tiles)
            {
                t.tt.X = 0;
                t.tile.Opacity = 1;
                // 아직 날아오는 중인 아이콘은 빈자리로 둠
                if (flying.ContainsKey(((DesktopItem)t.tile.Tag).Key)) t.tile.BeginAnimation(OpacityProperty, Anim.To(0, 1));
            }
            RedrawHighlights();
        }
    }

    void Layout()
    {
        dirty = false;
        var app = App.Instance;
        var wa = SystemParameters.WorkArea;
        var (top, height) = VerticalRange();
        var list = app.Store.VisibleFor(app.Config, Side);
        rows = Math.Max(1, (int)((height - PadY * 2) / Tiles.H));
        int cols = Math.Max(1, (list.Count + rows - 1) / rows);
        Width = cols * Tiles.W + PadX * 2 + Fade;
        Height = height;
        Top = top;
        Left = Side == Side.Left ? wa.Left : wa.Right - Width;

        canvas.Children.Clear();
        tiles.Clear();
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            int col = i / rows, row = i % rows;
            double x = Side == Side.Left ? PadX + col * Tiles.W : Width - PadX - (col + 1) * Tiles.W;
            double y = PadY + row * Tiles.H;
            var tile = Tiles.Create(item, false);
            Tiles.Attach(tile, item, this, HidePanel, () => BuildExtras(item));
            Canvas.SetLeft(tile, x + 3);
            Canvas.SetTop(tile, y + 3);
            double off = Side == Side.Left ? -(x + Tiles.W + 10) : (Width - x + 10);
            var tt = new TranslateTransform(off, 0);
            tile.RenderTransform = tt;
            tile.Opacity = 0;
            canvas.Children.Add(tile);
            tiles.Add((tile, tt, col, row, off));
        }
        ApplySelection();
    }

    /// <summary>작업 표시줄과 절대 겹치지 않는 세로 범위 (DIP).</summary>
    static (double top, double height) VerticalRange()
    {
        var wa = SystemParameters.WorkArea;
        double top = wa.Top, bottom = wa.Bottom;
        if (Native.TryGetTaskbarRect(out var tb))
        {
            var mon = Native.PrimaryMonitorRect();
            double scale = (mon.Right - mon.Left) / SystemParameters.PrimaryScreenWidth;
            bool horizontal = (tb.Right - tb.Left) > (tb.Bottom - tb.Top);
            if (horizontal && scale > 0)
            {
                // 자동 숨김 작업 표시줄이면 올라왔을 때 높이(약 48)만큼 비워 둠
                bool autoHidden = (Math.Min(tb.Bottom, mon.Bottom) - Math.Max(tb.Top, mon.Top)) < 10;
                if (tb.Top > (mon.Top + mon.Bottom) / 2 || autoHidden && tb.Top >= mon.Bottom - 10)
                    bottom = Math.Min(bottom, autoHidden ? mon.Bottom / scale - 52 : tb.Top / scale);
                else
                    top = Math.Max(top, autoHidden ? mon.Top / scale + 52 : tb.Bottom / scale);
            }
        }
        return (top, Math.Max(Tiles.H + PadY * 2, bottom - top));
    }

    void ShowPanel()
    {
        if (dirty) Layout();
        if (tiles.Count == 0) return;
        shown = true;
        hideToken++;
        if (!IsVisible) Show();
        Native.SetTopmost(hwnd);

        bg.BeginAnimation(OpacityProperty, Anim.To(1, 200));
        foreach (var t in tiles)
        {
            double delay = t.row * 14 + t.col * 55;
            t.tt.BeginAnimation(TranslateTransform.XProperty,
                Anim.To(0, 430, delay, new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut }));
            bool isFlying = flying.ContainsKey(((DesktopItem)t.tile.Tag).Key);
            t.tile.BeginAnimation(OpacityProperty, isFlying ? Anim.To(0, 1) : Anim.To(1, 220, delay));
        }
    }

    DateTime peekUntil = DateTime.MinValue;

    /// <summary>잠깐 튀어나와서 "여기 들어왔어요" 보여 주고 다시 들어감.</summary>
    public void Peek()
    {
        peekUntil = DateTime.UtcNow.AddMilliseconds(1600);
        if (!shown) ShowPanel();
    }

    public void HidePanel()
    {
        if (!shown) return;
        shown = false;
        Sel.Clear(this);
        int token = ++hideToken;
        int maxRow = 0;
        foreach (var t in tiles)
        {
            maxRow = Math.Max(maxRow, t.row);
            double delay = t.row * 6;
            t.tt.BeginAnimation(TranslateTransform.XProperty,
                Anim.To(t.off, 220, delay, new QuadraticEase { EasingMode = EasingMode.EaseIn }));
            t.tile.BeginAnimation(OpacityProperty, Anim.To(0, 220, delay));
        }
        bg.BeginAnimation(OpacityProperty, Anim.To(0, 260));
        Anim.After(280 + maxRow * 6, () =>
        {
            if (token == hideToken && !shown) Hide();
        });
    }

    /// <summary>30ms마다 호출. 커서 위치(물리 픽셀) 기준으로 열고 닫음.</summary>
    public void Tick(Native.POINT p, Native.RECT mon, bool lbtn, DateTime now)
    {
        var app = App.Instance;
        bool inTaskbar = Native.InTaskbar(p);
        if (!shown)
        {
            bool atEdge = (Side == Side.Left ? p.X <= mon.Left + 1 : p.X >= mon.Right - 2)
                          && p.Y >= mon.Top && p.Y < mon.Bottom
                          && !inTaskbar; // 시작 버튼·바탕화면 보기 버튼 쪽은 무시
            // 탐색기에서 파일을 끌고 오는 중이면(드래그 모양 커서) 버튼을 누른 상태여도 튀어나옴
            bool externalDrag = atEdge && lbtn && App.DragSource == null && Native.LooksLikeDragCursor();
            // 선택 네모를 끌다가 화면 끝에 닿아도 열림
            bool dragging = App.DragSource != null || externalDrag || DesktopBand.IsActive;
            bool allowed = atEdge
                           && (!lbtn || dragging)
                           && App.OpenMenus == 0
                           // 끌고 오는 중엔 최대화된 탐색기 창 위에서도 나오게
                           && !Native.ShouldBlock(mon, app.Config.DisableInFullscreen, app.Config.BlockWhenMaximized && !dragging);
            if (!allowed) { edgeSince = DateTime.MinValue; return; }
            if (edgeSince == DateTime.MinValue) edgeSince = now;
            double delay = externalDrag ? Math.Max(250, app.Config.EdgeDelayMs) : app.Config.EdgeDelayMs;
            if ((now - edgeSince).TotalMilliseconds >= delay)
            {
                edgeSince = DateTime.MinValue;
                awaySince = DateTime.MinValue;
                ShowPanel();
            }
        }
        else
        {
            if (hwnd == IntPtr.Zero) return;
            // 작업 표시줄로 내려가면 바로 비켜 줌 (시작 메뉴 등을 가리지 않게)
            if (inTaskbar && App.OpenMenus == 0 && App.DragSource == null) { awaySince = DateTime.MinValue; HidePanel(); return; }
            Native.GetWindowRect(hwnd, out var r);
            const int margin = 24;
            bool inside = p.X >= r.Left - margin && p.X <= r.Right + margin && p.Y >= r.Top - margin && p.Y <= r.Bottom + margin;
            if (inside || App.OpenMenus > 0 || App.DragSource == this || band.IsActive || Sel.Host == this || now < peekUntil) { awaySince = DateTime.MinValue; return; }
            if (awaySince == DateTime.MinValue) awaySince = now;
            if ((now - awaySince).TotalMilliseconds >= 350) { awaySince = DateTime.MinValue; HidePanel(); }
        }
    }

    int IndexAt(Point pos)
    {
        double cx = Side == Side.Left ? (pos.X - PadX) / Tiles.W : (Width - PadX - pos.X) / Tiles.W;
        int col = Math.Max(0, (int)Math.Floor(cx));
        int row = Math.Clamp((int)Math.Floor((pos.Y - PadY) / Tiles.H), 0, rows - 1);
        return col * rows + row;
    }

    /// <summary>탐색기와 같은 규칙: 같은 드라이브면 옮기기, 다른 드라이브면 복사. Ctrl=복사, Shift=옮기기.</summary>
    static bool WantsCopy(DragEventArgs e, string[] files, string destDir)
    {
        if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0) return true;
        if ((e.KeyStates & DragDropKeyStates.ShiftKey) != 0) return false;
        var srcRoot = System.IO.Path.GetPathRoot(files[0]) ?? "";
        var dstRoot = System.IO.Path.GetPathRoot(destDir) ?? "";
        return !string.Equals(srcRoot, dstRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 끌고 있는 위치가 폴더나 휴지통 아이콘 "한가운데"면 그 아이콘을 돌려줌 (= 그 안에 넣기).
    /// 아이콘 가장자리 쪽이면 null (= 그 자리로 순서 바꾸기).
    /// </summary>
    (Border tile, DesktopItem item)? DropTargetAt(Point pos, string[]? draggedKeys, string[]? files)
    {
        int idx = IndexAt(pos);
        if (idx < 0 || idx >= tiles.Count) return null;
        var tile = tiles[idx].tile;
        if (tile.Tag is not DesktopItem item) return null;
        if (draggedKeys != null && draggedKeys.Any(k => string.Equals(k, item.Key, StringComparison.OrdinalIgnoreCase))) return null;
        bool isBin = item.Key == ItemStore.RecycleBinKey;
        bool isDir = !item.IsVirtual && System.IO.Directory.Exists(item.Path);
        if (!isBin && !isDir) return null;
        var srcs = (draggedKeys ?? files ?? Array.Empty<string>()).Where(k => !k.StartsWith("::")).ToArray();
        if (srcs.Length == 0) return null;
        if (isDir && srcs.Any(s =>
            string.Equals(s.TrimEnd('\\'), item.Path, StringComparison.OrdinalIgnoreCase) ||
            item.Path.StartsWith(s.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))) return null;

        double x = Canvas.GetLeft(tile), y = Canvas.GetTop(tile);
        const double inset = 14;
        if (pos.X < x + inset || pos.X > x + tile.Width - inset || pos.Y < y + 4 || pos.Y > y + tile.Height - inset) return null;
        return (tile, item);
    }

    Border? hoverTile;
    Brush? hoverOldBg, hoverOldBorder;

    /// <summary>"여기 넣을 수 있어요" — 폴더/휴지통 아이콘을 테마 색으로.</summary>
    void SetDropHover(Border? tile)
    {
        if (tile == hoverTile) return;
        if (hoverTile != null) { hoverTile.Background = hoverOldBg; hoverTile.BorderBrush = hoverOldBorder; }
        hoverTile = tile;
        if (tile != null)
        {
            hoverOldBg = tile.Background;
            hoverOldBorder = tile.BorderBrush;
            tile.Background = Theme.Brush(Theme.Accent, 0x55);
            tile.BorderBrush = Theme.Brush(Theme.Accent);
        }
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        var pos = e.GetPosition(canvas);
        int index = IndexAt(pos);
        var keys = Tiles.KeysFrom(e.Data);
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        var target = DropTargetAt(pos, keys, files);
        SetDropHover(null);
        var app = App.Instance;

        if (target is { } tg)
        {
            var folder = tg.item;
            // 폴더/휴지통 안으로 넣기 (실제 작업은 끌기가 끝난 뒤에)
            var srcs = (keys ?? files!).Where(k => !k.StartsWith("::")).ToArray();
            bool bin = folder.Key == ItemStore.RecycleBinKey;
            bool copy = !bin && WantsCopy(e, srcs, folder.Path);
            Dispatcher.InvokeAsync(() =>
            {
                if (bin) app.RecycleFiles(srcs);
                else app.TransferFiles(srcs, folder.Path, copy);
            });
            e.Effects = keys != null ? DragDropEffects.Move : DragDropEffects.Copy & e.AllowedEffects;
        }
        else if (keys != null)
        {
            app.MoveManyToSide(keys, Side, index);
            e.Effects = DragDropEffects.Move;
        }
        else if (files is { Length: > 0 })
        {
            bool copy = WantsCopy(e, files, App.DesktopDir);
            Dispatcher.InvokeAsync(() => app.BringFilesToDesktop(files, copy, Side, index));
            // 탐색기한테는 "복사"라고 알려야 원본을 지우지 않음 (옮기기는 우리가 직접 함)
            e.Effects = DragDropEffects.Copy & e.AllowedEffects;
        }
        e.Handled = true;
    }

    // ---------- 새로 들어온 아이콘 보여 주기 ----------

    /// <summary>날아오는 중인 아이콘 (판이 다시 그려져도 빈자리로 유지하려고).</summary>
    readonly Dictionary<string, int> flying = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>반짝이는 중인 아이콘과 끝나는 시각 (판이 다시 그려져도 이어서 반짝이게).</summary>
    readonly Dictionary<string, DateTime> highlights = new(StringComparer.OrdinalIgnoreCase);
    readonly List<UIElement> highlightRings = new();

    /// <summary>가장자리를 열고, 새 아이콘 자리를 반짝 강조. fly면 from(마우스/놓은 위치)에서 날아와서 자리에 앉음.</summary>
    public void RevealArrivals(List<(string key, bool fly)> items, Point from)
    {
        bool wasShown = shown;
        peekUntil = DateTime.UtcNow.AddMilliseconds(3200);
        if (!shown) ShowPanel();
        if (!shown) return;
        int n = 0;
        foreach (var (key, fly) in items)
        {
            var tile = TileOf(key);
            if (tile == null) continue;

            if (fly && n < 12)
            {
                double delay = n++ * 90;
                flying[key] = flying.TryGetValue(key, out var c) ? c + 1 : 1;
                tile.BeginAnimation(OpacityProperty, Anim.To(0, 1)); // 날아와서 앉을 때까지 빈자리
                var to = new Point(Left + Canvas.GetLeft(tile) + tile.Width / 2, Top + Canvas.GetTop(tile) + tile.Height / 2);
                FlyOverlay.Fly((DesktopItem)tile.Tag, from, to, delay, () =>
                {
                    if (flying.TryGetValue(key, out var left) && --left > 0) flying[key] = left;
                    else flying.Remove(key);
                    var cur = TileOf(key); // 그새 판이 다시 그려졌을 수 있어서 지금 칸을 다시 찾음
                    if (cur == null) return;
                    if (!flying.ContainsKey(key)) cur.BeginAnimation(OpacityProperty, Anim.To(1, 80));
                    Highlight(key);
                    peekUntil = DateTime.UtcNow.AddMilliseconds(1800);
                });
            }
            else
            {
                Anim.After(wasShown ? 60 : 700, () => Highlight(key)); // 판이 다 튀어나온 뒤에
            }
        }
    }

    void Highlight(string key)
    {
        highlights[key] = DateTime.UtcNow.AddMilliseconds(2200);
        RedrawHighlights();
    }

    /// <summary>반짝이는 중인 아이콘마다 고리 + 빛. 판이 다시 그려진 뒤에도 남은 시간만큼 이어서.</summary>
    void RedrawHighlights()
    {
        foreach (var r in highlightRings) canvas.Children.Remove(r);
        highlightRings.Clear();
        var now = DateTime.UtcNow;
        foreach (var (key, until) in highlights.ToList())
        {
            var remaining = (until - now).TotalMilliseconds;
            var tile = TileOf(key);
            if (remaining <= 50 || tile == null)
            {
                highlights.Remove(key);
                if (tile?.Effect is DropShadowEffect) tile.Effect = null;
                continue;
            }
            var accent = Theme.Accent;
            var st = new ScaleTransform(1, 1);
            var ring = new Border
            {
                Width = tile.Width + 10, Height = tile.Height + 10,
                CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(3),
                BorderBrush = new SolidColorBrush(accent), IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = st
            };
            Canvas.SetLeft(ring, Canvas.GetLeft(tile) - 5);
            Canvas.SetTop(ring, Canvas.GetTop(tile) - 5);
            canvas.Children.Add(ring);
            highlightRings.Add(ring);
            var grow = new DoubleAnimation(1, 1.22, TimeSpan.FromMilliseconds(700))
            {
                RepeatBehavior = new RepeatBehavior(TimeSpan.FromMilliseconds(remaining)),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            st.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            st.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(700)) { RepeatBehavior = new RepeatBehavior(TimeSpan.FromMilliseconds(remaining)) });
            tile.Effect = new DropShadowEffect { Color = accent, ShadowDepth = 0, BlurRadius = 24, Opacity = 1 };
        }
        if (highlights.Count > 0 && !redrawScheduled)
        {
            redrawScheduled = true;
            Anim.After(highlights.Values.Min(u => (u - now).TotalMilliseconds) + 30, () => { redrawScheduled = false; RedrawHighlights(); });
        }
    }

    bool redrawScheduled;

    // ---------- 메뉴 ----------

    /// <summary>우클릭한 아이콘이 여러 개 선택 중 하나면 선택된 것 전부, 아니면 그것 하나.</summary>
    List<string> TargetsFor(DesktopItem item) =>
        Sel.Contains(this, item.Key) && Sel.Keys.Count > 1 ? Sel.Ordered(this) : new List<string> { item.Key };

    ContextMenu BackgroundMenu(int index)
    {
        var app = App.Instance;
        var cm = new ContextMenu();
        cm.Items.Add(Tiles.Item("새 폴더", () => app.CreateNewFolder(Side, index)));
        var paste = Tiles.Item("붙여넣기", () => app.PasteToDesktop(Side, index));
        paste.IsEnabled = App.ClipboardHasFiles();
        cm.Items.Add(paste);
        cm.Items.Add(new Separator());
        cm.Items.Add(Tiles.Item("바탕화면 폴더 열기", app.OpenDesktopFolder));
        return cm;
    }

    /// <summary>Windows 원래 메뉴 맨 위에 붙는 DesktopPop 전용 항목.</summary>
    List<MenuEntry> BuildExtras(DesktopItem item)
    {
        var app = App.Instance;
        var targets = TargetsFor(item);
        string count = targets.Count > 1 ? $" ({targets.Count}개)" : "";

        var bags = app.Config.Bags.Select(b => MenuEntry.Item(b.Name, () => app.MoveManyToBag(targets, b))).ToList();
        if (bags.Count > 0) bags.Add(MenuEntry.Separator);
        bags.Add(MenuEntry.Item("새 가방 만들어서 넣기...", () => app.CreateBag(targets)));

        var other = Side == Side.Left ? Side.Right : Side.Left;
        return new List<MenuEntry>
        {
            MenuEntry.Sub("가방에 넣기" + count, bags),
            MenuEntry.Item((Side == Side.Left ? "오른쪽 가장자리로 보내기" : "왼쪽 가장자리로 보내기") + count,
                () => app.MoveManyToSide(targets, other, null)),
        };
    }
}
