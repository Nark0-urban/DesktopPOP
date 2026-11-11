using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DesktopPop;

internal static class Anim
{
    public static DoubleAnimation To(double to, double ms, double delayMs = 0, IEasingFunction? ease = null) =>
        new(to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delayMs), EasingFunction = ease };

    public static void After(double ms, Action a)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (s, e) => { t.Stop(); a(); };
        t.Start();
    }
}

/// <summary>아이콘들을 담고 있는 곳(가장자리 판, 펼친 가방). 여러 개 선택을 지원.</summary>
internal interface ISelHost
{
    IReadOnlyList<string> OrderedKeys { get; }
    Border? TileOf(string key);
    void ApplySelection();
    /// <summary>지금 화면에 보여서 선택 네모로 고를 수 있는지 (판이 열려 있음 / 가방이 펼쳐져 있음).</summary>
    bool BandSelectable { get; }
}

/// <summary>지금 선택된 아이콘들 (한 번에 한 곳에서만 선택).</summary>
internal static class Sel
{
    static readonly List<string> keys = new();
    public static ISelHost? Host { get; private set; }
    public static string? Anchor;
    public static IReadOnlyList<string> Keys => keys;

    static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    public static bool Contains(ISelHost h, string key) => Host == h && keys.Any(k => Eq(k, key));

    static void Change(ISelHost? host, IEnumerable<string> ks)
    {
        var old = Host;
        Host = host;
        keys.Clear();
        keys.AddRange(ks.Distinct(StringComparer.OrdinalIgnoreCase));
        if (keys.Count == 0) Host = null;
        if (old != null && old != Host) old.ApplySelection();
        Host?.ApplySelection();
    }

    public static void Clear(ISelHost? onlyIf = null)
    {
        if (onlyIf != null && Host != onlyIf) return;
        if (Host == null && keys.Count == 0) return;
        Change(null, Array.Empty<string>());
    }

    public static void Set(ISelHost h, IEnumerable<string> ks) => Change(h, ks);

    /// <summary>Ctrl+클릭: 하나 넣거나 빼기.</summary>
    public static void Toggle(ISelHost h, string key)
    {
        var cur = Host == h ? keys.ToList() : new List<string>();
        if (cur.RemoveAll(k => Eq(k, key)) == 0) cur.Add(key);
        Anchor = key;
        Change(h, cur);
    }

    /// <summary>Shift+클릭: 기준 아이콘부터 여기까지 전부.</summary>
    public static void Range(ISelHost h, string key)
    {
        var order = h.OrderedKeys.ToList();
        int b = order.FindIndex(k => Eq(k, key));
        int a = Anchor != null ? order.FindIndex(k => Eq(k, Anchor)) : -1;
        if (a < 0) { a = b; Anchor = key; }
        if (b < 0) return;
        Change(h, order.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1));
    }

    /// <summary>선택된 것들을 보이는 순서대로.</summary>
    public static List<string> Ordered(ISelHost h) => h.OrderedKeys.Where(k => Contains(h, k)).ToList();
}

/// <summary>빈 곳을 끌어서 네모로 여러 개 선택. Ctrl/Shift를 누르고 끌면 기존 선택에 더함.</summary>
internal sealed class RubberBand
{
    public Action? OnClick;            // 끌지 않고 그냥 클릭했을 때
    public bool IsActive => DesktopBand.IsActive;

    /// <summary>surface 의 빈 곳을 누르면 화면 전체 선택 네모를 시작 (그리기·선택은 DesktopBand 가 함).</summary>
    public RubberBand(UIElement surface, Canvas canvas, ISelHost host)
    {
        surface.MouseLeftButtonDown += (s, e) =>
        {
            if (e.Handled) return;
            if (!Native.GetCursorPos(out var p)) return;
            bool add = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
            DesktopBand.Begin(p, add, OnClick);
            e.Handled = true;
        };
    }
}

// <summary>아이콘 한 칸(그림 + 이름) 만들기와 클릭/드래그/우클릭 처리.</summary>
internal static class Tiles
{
    public const string DragFormat = "DesktopPop.ItemKey";
    public const double W = 86, H = 98;

    static readonly Brush HoverBg = Frozen(new SolidColorBrush(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF)));
    static readonly Brush HoverBorder = Frozen(new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF)));
    static readonly Brush IdleBg = Frozen(new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)));

    static Brush Frozen(Brush b) { b.Freeze(); return b; }

    /// <summary>끌기 데이터에서 아이콘 목록 꺼내기 (여러 개면 줄바꿈으로 이어져 있음).</summary>
    public static string[]? KeysFrom(IDataObject data) =>
        data.GetData(DragFormat) is string s ? s.Split('\n', StringSplitOptions.RemoveEmptyEntries) : null;

    public static Border Create(DesktopItem item, bool backdrop)
    {
        var img = new Image
        {
            Source = item.Icon, Width = 48, Height = 48,
            Margin = new Thickness(0, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        var txt = new TextBlock
        {
            Text = item.Name, Foreground = Brushes.White, FontSize = 12,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center, MaxHeight = 33, Margin = new Thickness(4, 4, 4, 0),
            Effect = new DropShadowEffect { ShadowDepth = 1, BlurRadius = 4, Opacity = 0.95, Color = Colors.Black }
        };

        var scale = new ScaleTransform(1, 1);
        var sp = new StackPanel { RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = scale };
        sp.Children.Add(img);
        sp.Children.Add(txt);

        var idle = backdrop ? Theme.Brush(Theme.Backdrop, 0x80) : IdleBg;
        var border = new Border
        {
            Width = W - 6, Height = H - 6, CornerRadius = new CornerRadius(12),
            Background = idle, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
            Child = sp, Cursor = Cursors.Hand, ToolTip = item.Name, Tag = item
        };
        border.Resources["idle"] = idle;
        border.MouseEnter += (s, e) =>
        {
            bool sel = IsSelected(border);
            border.Background = sel ? Theme.Brush(Theme.Accent, 0x66) : HoverBg;
            border.BorderBrush = sel ? Theme.Brush(Theme.Accent) : HoverBorder;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.08, 120));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.08, 120));
        };
        border.MouseLeave += (s, e) =>
        {
            ApplyRest(border);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1, 160));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1, 160));
        };
        return border;
    }

    static bool IsSelected(Border tile) => tile.Resources["sel"] is true;

    /// <summary>선택된 아이콘은 테마 색으로 칠해 둠.</summary>
    public static void SetSelected(Border tile, bool selected)
    {
        if (IsSelected(tile) == selected) return;
        tile.Resources["sel"] = selected;
        if (!tile.IsMouseOver) ApplyRest(tile);
    }

    static void ApplyRest(Border tile)
    {
        bool sel = IsSelected(tile);
        tile.Background = sel ? Theme.Brush(Theme.Accent, 0x45) : tile.Resources["idle"] as Brush;
        tile.BorderBrush = sel ? Theme.Brush(Theme.Accent, 0xCC) : Brushes.Transparent;
    }

    /// <summary>
    /// 클릭 = 선택, 두 번 클릭 = 실행, Ctrl+클릭 = 선택 넣기/빼기, Shift+클릭 = 범위 선택,
    /// 끌기 = 옮기기 (선택된 걸 잡으면 선택된 것 전부), 우클릭 = 메뉴.
    /// isOutside/dropOutside: 빈 바탕화면 쪽에 놓았을 때 할 일 (가방에서 꺼내기용).
    /// </summary>
    public static void Attach(Border tile, DesktopItem item, ISelHost host, Action afterLaunch, Func<List<MenuEntry>> extras,
        Func<Native.POINT, bool>? isOutside = null, Action<List<string>, Native.POINT>? dropOutside = null)
    {
        Point? down = null;
        var mods = ModifierKeys.None;
        tile.MouseLeftButtonDown += (s, e) =>
        {
            e.Handled = true;
            // 두 번 클릭 = 실행
            if (e.ClickCount >= 2 && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
            {
                down = null;
                Sel.Clear();
                Shell.Launch(item);
                afterLaunch();
                return;
            }
            down = e.GetPosition(tile);
            mods = Keyboard.Modifiers;
        };
        tile.MouseMove += (s, e) =>
        {
            if (down is not Point d) return;
            if (e.LeftButton != MouseButtonState.Pressed) { down = null; return; }
            var p = e.GetPosition(tile);
            if (Math.Abs(p.X - d.X) + Math.Abs(p.Y - d.Y) < 8) return;
            down = null;
            StartDrag(tile, item, host, isOutside, dropOutside);
        };
        tile.MouseLeftButtonUp += (s, e) =>
        {
            if (down == null) return;
            down = null;
            e.Handled = true;
            if ((mods & ModifierKeys.Control) != 0) { Sel.Toggle(host, item.Key); return; }
            if ((mods & ModifierKeys.Shift) != 0) { Sel.Range(host, item.Key); return; }
            if (App.Instance.Config.SingleClickLaunch)
            {
                Sel.Clear();
                Sel.Anchor = item.Key;
                Shell.Launch(item);
                afterLaunch();
                return;
            }
            // 한 번 클릭 = 이것만 선택 (Shift+클릭 범위의 시작점)
            Sel.Set(host, new[] { item.Key });
            Sel.Anchor = item.Key;
        };
        tile.MouseRightButtonUp += (s, e) =>
        {
            e.Handled = true;
            // 원래 바탕화면처럼: 선택 안 된 걸 우클릭하면 그것만 선택
            if (!Sel.Contains(host, item.Key)) { Sel.Set(host, new[] { item.Key }); Sel.Anchor = item.Key; }
            var keys = new List<string> { item.Key };
            keys.AddRange(Sel.Ordered(host).Where(k => !string.Equals(k, item.Key, StringComparison.OrdinalIgnoreCase)));
            var more = extras();
            var owner = Window.GetWindow(tile);
            // Windows 원래 메뉴 (안 되면 예전 메뉴로)
            if (owner == null || !ShellMenu.Show(owner, keys, more, item.IsVirtual ? null : () => App.Instance.RenameItem(item)))
                ShowFallbackMenu(tile, item, more);
        };
    }

    static void ShowFallbackMenu(Border tile, DesktopItem item, List<MenuEntry> more)
    {
        var cm = new ContextMenu();
        cm.Items.Add(Item("열기", () => Shell.Launch(item)));
        if (!item.IsVirtual) cm.Items.Add(Item("파일 위치 열기", () => Shell.OpenLocation(item)));
        if (more.Count > 0) cm.Items.Add(new Separator());
        foreach (var m in more) cm.Items.Add(ToWpf(m));
        App.Track(cm);
        cm.PlacementTarget = tile;
        cm.IsOpen = true;
    }

    static object ToWpf(MenuEntry m)
    {
        if (m.IsSeparator) return new Separator();
        var mi = new MenuItem { Header = m.Text };
        if (m.Children != null) foreach (var c in m.Children) mi.Items.Add(ToWpf(c));
        else if (m.Run != null) mi.Click += (s, e) => m.Run();
        return mi;
    }

    static void StartDrag(Border tile, DesktopItem item, ISelHost host,
        Func<Native.POINT, bool>? isOutside, Action<List<string>, Native.POINT>? dropOutside)
    {
        // 선택된 아이콘을 잡았으면 선택된 것 전부, 아니면 이것 하나
        List<string> keys;
        if (Sel.Contains(host, item.Key)) keys = Sel.Ordered(host);
        else { Sel.Clear(); keys = new List<string> { item.Key }; }

        App.DragSource = host;
        var ghost = new DragGhost(item, keys.Count);
        ghost.Show();
        ghost.Follow();
        var follow = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(10) };
        follow.Tick += (s2, e2) => ghost.Follow();
        follow.Start();

        GiveFeedbackEventHandler feedback = (s2, e2) =>
        {
            ghost.Follow();
            if (!Native.GetCursorPos(out var cp)) return;
            bool overDesktop = Native.IsDesktopAt(cp);
            bool takeOut = isOutside != null && isOutside(cp) && (e2.Effects == DragDropEffects.None || overDesktop);
            ghost.SetValid((e2.Effects != DragDropEffects.None && !overDesktop) || takeOut);
            if (takeOut || overDesktop)
            {
                // 빈 바탕화면 위: 꺼내기면 보통 화살표, 아니면 "여기엔 못 놓아요"
                e2.UseDefaultCursors = false;
                Mouse.SetCursor(takeOut ? Cursors.Arrow : Cursors.No);
                e2.Handled = true;
            }
        };
        bool cancelled = false, droppedOnDesktop = false;
        QueryContinueDragEventHandler query = (s2, e2) =>
        {
            if (e2.EscapePressed) { cancelled = true; return; }
            bool released = (e2.KeyStates & DragDropKeyStates.LeftMouseButton) == 0;
            // 숨겨진 원래 바탕화면에 떨어뜨리면 탐색기가 "원본과 대상 파일이 같습니다" 오류를 냄 → 우리가 가로챔
            if (released && Native.GetCursorPos(out var qp) && Native.IsDesktopAt(qp))
            {
                droppedOnDesktop = true;
                e2.Action = DragAction.Cancel;
                e2.Handled = true;
            }
        };
        tile.GiveFeedback += feedback;
        tile.QueryContinueDrag += query;
        foreach (var k in keys) host.TileOf(k)?.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.3, 120));

        // 진짜 파일 정보도 같이 실어서 탐색기 창·다른 폴더·다른 프로그램에도 원래 바탕화면처럼 넣을 수 있게
        var data = new DataObject(DragFormat, string.Join("\n", keys));
        var paths = keys.Where(k => !k.StartsWith("::")).ToArray();
        if (paths.Length > 0) data.SetData(DataFormats.FileDrop, paths);
        var allowed = paths.Length > 0 ? DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link : DragDropEffects.Move;
        var result = DragDropEffects.None;
        try { result = DragDrop.DoDragDrop(tile, data, allowed); }
        finally
        {
            follow.Stop();
            tile.GiveFeedback -= feedback;
            tile.QueryContinueDrag -= query;
            ghost.Close();
            foreach (var k in keys) host.TileOf(k)?.BeginAnimation(UIElement.OpacityProperty, Anim.To(1, 150));
            App.DragSource = null;
        }
        if (result != DragDropEffects.None || droppedOnDesktop) Sel.Clear(host);
        if (!cancelled && (droppedOnDesktop || result == DragDropEffects.None) && dropOutside != null && isOutside != null
            && Native.GetCursorPos(out var dp) && isOutside(dp))
            dropOutside(keys, dp);
    }

    public static MenuItem Item(string header, Action a)
    {
        var m = new MenuItem { Header = header };
        m.Click += (s, e) => a();
        return m;
    }
}

/// <summary>화면 전체를 덮는 투명 창. 새로 생긴 아이콘이 마우스 위치에서 가장자리 자리로 날아가는 연출용.</summary>
internal sealed class FlyOverlay : Window
{
    static FlyOverlay? inst;
    readonly Canvas canvas = new();
    int active;

    FlyOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Content = canvas;
        SourceInitialized += (s, e) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(hwnd);
            const int GWL_EXSTYLE = -20;
            long ex = Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            ex |= 0x20 | 0x80000 | 0x08000000; // 마우스가 뚫고 지나감
            Native.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };
    }

    /// <summary>from(마우스 위치)에서 뿅 나타났다가 포물선을 그리며 to(가장자리 자리)로 날아감. 좌표는 DIP.</summary>
    public static void Fly(DesktopItem item, Point from, Point to, double delayMs, Action onArrive)
    {
        var o = inst ??= new FlyOverlay();
        o.Left = 0; o.Top = 0;
        o.Width = SystemParameters.PrimaryScreenWidth;
        o.Height = SystemParameters.PrimaryScreenHeight;
        if (!o.IsVisible) o.Show();
        Native.SetTopmost(new System.Windows.Interop.WindowInteropHelper(o).Handle);
        o.active++;

        var tile = Tiles.Create(item, true);
        tile.IsHitTestVisible = false;
        tile.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Opacity = 0.5, Color = Colors.Black };
        Canvas.SetLeft(tile, -tile.Width / 2);
        Canvas.SetTop(tile, -tile.Height / 2);
        var st = new ScaleTransform(0.2, 0.2);
        var tt = new TranslateTransform(from.X, from.Y);
        var tg = new TransformGroup();
        tg.Children.Add(st);
        tg.Children.Add(tt);
        tile.RenderTransform = tg;
        tile.RenderTransformOrigin = new Point(0.5, 0.5);
        tile.Opacity = 0;
        o.canvas.Children.Add(tile);

        const double popMs = 280, holdMs = 120, flyMs = 650;
        // 1) 마우스 위치에서 뿅 → 2) 잠깐 멈춤 → 3) 날아가면서 원래 크기로
        var scale = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(delayMs) };
        scale.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(popMs)),
            new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut }));
        scale.KeyFrames.Add(new LinearDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(popMs + holdMs))));
        scale.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(popMs + holdMs + flyMs)),
            new QuadraticEase { EasingMode = EasingMode.EaseInOut }));
        st.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, scale);
        tile.BeginAnimation(UIElement.OpacityProperty, Anim.To(1, 120, delayMs));

        double lift = Math.Max(60, Math.Abs(to.X - from.X) * 0.25);
        var mid = new Point((from.X + to.X) / 2, Math.Max(20, Math.Min(from.Y, to.Y) - lift));
        var geo = new PathGeometry(new[] { new PathFigure(from, new PathSegment[] { new QuadraticBezierSegment(mid, to, true) }, false) });
        geo.Freeze();
        var begin = TimeSpan.FromMilliseconds(delayMs + popMs + holdMs);
        var dur = new Duration(TimeSpan.FromMilliseconds(flyMs));
        var ax = new DoubleAnimationUsingPath { PathGeometry = geo, Source = PathAnimationSource.X, Duration = dur, BeginTime = begin, AccelerationRatio = 0.35, DecelerationRatio = 0.65 };
        var ay = new DoubleAnimationUsingPath { PathGeometry = geo, Source = PathAnimationSource.Y, Duration = dur, BeginTime = begin, AccelerationRatio = 0.35, DecelerationRatio = 0.65 };
        ax.Completed += (s, e) =>
        {
            o.canvas.Children.Remove(tile);
            try { onArrive(); } catch (Exception ex) { App.Log(ex); }
            if (--o.active <= 0) { o.active = 0; o.Hide(); }
        };
        tt.BeginAnimation(TranslateTransform.XProperty, ax);
        tt.BeginAnimation(TranslateTransform.YProperty, ay);
    }
}

/// <summary>끌고 있는 동안 마우스를 따라다니는 아이콘. 클릭·드롭은 통과시킴.</summary>
internal sealed class DragGhost : Window
{
    readonly double scale;
    readonly Border tile;

    public DragGhost(DesktopItem item, int count = 1)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        tile = Tiles.Create(item, true);
        tile.Margin = new Thickness(16);
        tile.Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 5, Opacity = 0.55, Color = Colors.Black };
        tile.RenderTransformOrigin = new Point(0.5, 0.5);
        var tg = new TransformGroup();
        tg.Children.Add(new ScaleTransform(1.08, 1.08));
        tg.Children.Add(new RotateTransform(-6)); // 살짝 기울여서 "집어 든" 느낌
        tile.RenderTransform = tg;

        if (count <= 1) Content = tile;
        else
        {
            // 여러 개면 뒤에 카드가 겹쳐 보이고 오른쪽 위에 개수 표시
            var back = new Border
            {
                Width = tile.Width, Height = tile.Height, Margin = new Thickness(16), CornerRadius = new CornerRadius(12),
                Background = Theme.Brush(Theme.Backdrop, 0xB0), BorderBrush = Theme.Brush(Theme.Accent, 0x88), BorderThickness = new Thickness(1),
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(5)
            };
            var badge = new Border
            {
                MinWidth = 24, Height = 24, CornerRadius = new CornerRadius(12), Padding = new Thickness(6, 0, 6, 0),
                Background = Theme.Brush(Theme.Badge), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                Child = new TextBlock { Text = count.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            var stack = new Grid();
            stack.Children.Add(back);
            stack.Children.Add(tile);
            stack.Children.Add(badge);
            Content = stack;
        }

        scale = Native.DipScale();

        SourceInitialized += (s, e) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(hwnd);
            const int GWL_EXSTYLE = -20;
            long ex = Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            ex |= 0x20 | 0x80000 | 0x08000000; // TRANSPARENT | LAYERED | NOACTIVATE → 마우스가 뚫고 지나감
            Native.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };
    }

    public void Follow()
    {
        if (!Native.GetCursorPos(out var p)) return;
        double w = ActualWidth > 0 ? ActualWidth : Tiles.W + 32;
        Left = p.X / scale - w / 2;
        Top = p.Y / scale - 34; // 커서가 아이콘 그림 위쯤에 오도록
    }

    /// <summary>놓을 수 있는 곳 위면 또렷하게, 아니면 반투명.</summary>
    public void SetValid(bool valid) => tile.Opacity = valid ? 1.0 : 0.6;}
