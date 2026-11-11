using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DesktopPop;

/// <summary>내가 고른 그림으로 만든 "가방". 누르면 보관한 아이콘이 주변으로 촤르륵 펼쳐짐.</summary>
internal sealed class BagWindow : Window, ISelHost
{
    readonly RubberBand haloBand;

    // ---------- 여러 개 선택 (펼친 아이콘들) ----------

    public IReadOnlyList<string> OrderedKeys => ringTiles.Select(t => ((DesktopItem)t.Tag).Key).ToList();
    public bool BandSelectable => open;

    public Border? TileOf(string key) =>
        ringTiles.FirstOrDefault(t => t.Tag is DesktopItem it && string.Equals(it.Key, key, StringComparison.OrdinalIgnoreCase));

    public void ApplySelection()
    {
        foreach (var t in ringTiles) Tiles.SetSelected(t, Sel.Contains(this, ((DesktopItem)t.Tag).Key));
    }

    List<string> TargetsFor(DesktopItem item) =>
        Sel.Contains(this, item.Key) && Sel.Keys.Count > 1 ? Sel.Ordered(this) : new List<string> { item.Key };

    Point CenterDip => new(Cfg.X, Cfg.Y);

    public readonly BagConfig Cfg;
    readonly Canvas canvas = new();
    readonly Grid bagVisual = new();
    readonly Border hit = new();
    readonly Image bagImg = new();
    readonly TextBlock label = new();
    readonly Border badge = new();
    readonly TextBlock badgeText = new();
    readonly ScaleTransform bagScale = new(1, 1);
    readonly Ellipse halo = new();
    readonly Border hintBox = new();
    List<Border> ringTiles = new();
    bool open;
    double center;
    Point? downPt;

    static Effect Glow => new DropShadowEffect { Color = Theme.Glow, ShadowDepth = 0, BlurRadius = 24, Opacity = 0.95 };

    public bool IntentionalClose;

    public BagWindow(BagConfig cfg, bool useDesktopOwner = true)
    {
        Cfg = cfg;
        Title = "DesktopPop Bag";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = false;
        Content = canvas;

        // 바탕화면에 붙어 있게(Win+D 눌러도 남도록) 바탕화면 창을 주인으로.
        if (useDesktopOwner)
        {
            var progman = DesktopShell.FindProgman();
            if (progman != IntPtr.Zero) new WindowInteropHelper(this).Owner = progman;
        }
        SourceInitialized += (s, e) => Native.MakeToolWindow(new WindowInteropHelper(this).Handle);
        // 일부러 지운 게 아닌데 창이 닫히면(바탕화면이 다시 그려질 때 등) 곧바로 되살림
        Closed += (s, e) => { if (!IntentionalClose) App.Instance.OnBagLost(this); };
        Deactivated += (s, e) => { if (open && App.OpenMenus == 0 && App.DragSource == null) CloseRing(); };
        PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) CloseRing(); };

        // 펼쳐질 때 뒤에 깔리는 은은한 원
        halo.Opacity = 0;
        halo.IsHitTestVisible = false;
        // 펼친 원의 빈 곳: 끌면 네모로 여러 개 선택, 그냥 클릭하면 접기
        haloBand = new RubberBand(halo, canvas, this) { OnClick = () => CloseRing() };
        Panel.SetZIndex(halo, 0);
        canvas.Children.Add(halo);

        // 가방 본체
        hit.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        hit.CornerRadius = new CornerRadius(22);
        bagImg.Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(bagImg, BitmapScalingMode.HighQuality);
        label.Foreground = Brushes.White;
        label.FontSize = 12.5;
        label.FontWeight = FontWeights.SemiBold;
        label.TextAlignment = TextAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.Effect = new DropShadowEffect { ShadowDepth = 1, BlurRadius = 4, Opacity = 0.95, Color = Colors.Black };
        badgeText.Foreground = Brushes.White;
        badgeText.FontSize = 11;
        badgeText.FontWeight = FontWeights.Bold;
        badgeText.HorizontalAlignment = HorizontalAlignment.Center;
        badgeText.VerticalAlignment = VerticalAlignment.Center;
        badge.CornerRadius = new CornerRadius(10);
        badge.MinWidth = 20;
        badge.Height = 20;
        badge.Padding = new Thickness(5, 0, 5, 0);
        badge.Child = badgeText;
        badge.IsHitTestVisible = false;

        bagVisual.Children.Add(hit);
        bagVisual.Children.Add(bagImg);
        bagVisual.Children.Add(label);
        bagVisual.Children.Add(badge);
        bagVisual.RenderTransform = bagScale;
        bagVisual.Cursor = Cursors.Hand;
        bagVisual.AllowDrop = true;
        Panel.SetZIndex(bagVisual, 5);
        canvas.Children.Add(bagVisual);

        // 빈 가방 안내 말풍선
        hintBox.CornerRadius = new CornerRadius(10);
        hintBox.Padding = new Thickness(12, 8, 12, 8);
        hintBox.Width = 230;
        hintBox.Opacity = 0;
        hintBox.IsHitTestVisible = false;
        hintBox.Child = new TextBlock
        {
            Text = "가방이 비어 있어요!\n가장자리 아이콘을 여기로 끌어다 놓아 보세요.",
            Foreground = Brushes.White, FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center
        };
        Panel.SetZIndex(hintBox, 10);
        canvas.Children.Add(hintBox);

        HookEvents();
        Rebuild();
    }

    void HookEvents()
    {
        bagVisual.MouseEnter += (s, e) =>
        {
            bagImg.Effect = Glow;
            bagScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.07, 130));
            bagScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.07, 130));
        };
        bagVisual.MouseLeave += (s, e) =>
        {
            bagImg.Effect = null;
            bagScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1, 160));
            bagScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1, 160));
        };

        // 클릭 = 열기/닫기, 끌기 = 가방 위치 옮기기
        bagVisual.MouseLeftButtonDown += (s, e) =>
        {
            downPt = e.GetPosition(this);
            bagVisual.CaptureMouse();
            e.Handled = true;
        };
        bagVisual.MouseMove += (s, e) =>
        {
            if (downPt is not Point d || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - d.X) + Math.Abs(p.Y - d.Y) < 6) return;
            downPt = null;
            bagVisual.ReleaseMouseCapture();
            CloseRing(false);
            try { DragMove(); } catch (InvalidOperationException) { }
            Cfg.X = Left + Width / 2;
            Cfg.Y = Top + Height / 2;
            Rebuild();
            App.Instance.Config.Save();
        };
        bagVisual.MouseLeftButtonUp += (s, e) =>
        {
            if (downPt == null) return;
            downPt = null;
            bagVisual.ReleaseMouseCapture();
            if (open) CloseRing(); else OpenRing();
            e.Handled = true;
        };
        bagVisual.MouseRightButtonUp += (s, e) =>
        {
            var cm = BagMenu();
            App.Track(cm);
            cm.PlacementTarget = bagVisual;
            cm.IsOpen = true;
            e.Handled = true;
        };

        // 아이콘 끌어다 넣기
        DragEventHandler over = (s, e) =>
        {
            if (e.Data.GetDataPresent(Tiles.DragFormat)) e.Effects = DragDropEffects.Move;
            else if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effects = DragDropEffects.Link;
            else e.Effects = DragDropEffects.None;
            if (e.Effects != DragDropEffects.None) bagImg.Effect = Glow;
            e.Handled = true;
        };
        bagVisual.DragEnter += (s, e) =>
        {
            over(s, e);
            if (e.Effects == DragDropEffects.None) return;
            // "여기 놓으면 돼요" — 가방이 입을 벌리듯 커짐
            bagScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.2, 160, 0, new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }));
            bagScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.2, 160, 0, new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut }));
        };
        bagVisual.DragOver += over;
        bagVisual.DragLeave += (s, e) =>
        {
            bagImg.Effect = null;
            bagScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1, 160));
            bagScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1, 160));
        };
        bagVisual.Drop += (s, e) =>
        {
            bagImg.Effect = null;
            var app = App.Instance;
            if (Tiles.KeysFrom(e.Data) is { } keys)
                app.MoveManyToBag(keys, Cfg);
            else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                app.MoveManyToBag(files, Cfg);
            Bounce();
            e.Handled = true;
        };
    }

    List<DesktopItem> Items() =>
        Cfg.Items.Select(k => App.Instance.Store.Get(k)).Where(i => i != null).Select(i => i!).ToList();

    public void Rebuild()
    {
        bool wasOpen = open;
        if (open) CloseRing(false);
        RebuildCore();
        // 하나 꺼내거나 넣은 뒤에도 가방은 펼친 채로 유지
        if (wasOpen && Items().Count > 0) OpenRing();
    }

    void RebuildCore()
    {
        // 테마 색
        var hc = Theme.Halo;
        var hb = new RadialGradientBrush();
        hb.GradientStops.Add(new GradientStop(Theme.WithAlpha(hc, 0x9A), 0));
        hb.GradientStops.Add(new GradientStop(Theme.WithAlpha(hc, 0x70), 0.75));
        hb.GradientStops.Add(new GradientStop(Theme.WithAlpha(hc, 0x00), 1));
        hb.Freeze();
        halo.Fill = hb;
        badge.Background = Theme.Brush(Theme.Badge);
        hintBox.Background = Theme.Brush(Theme.Hint, 0xE6);


        double S = Cfg.Size, hitS = S + 24, visW = Math.Max(hitS, 120);
        bagVisual.Width = visW;
        bagVisual.Height = hitS + 24;
        bagVisual.RenderTransformOrigin = new Point(0.5, (hitS / 2) / (hitS + 24));
        hit.Width = hitS; hit.Height = hitS;
        hit.HorizontalAlignment = HorizontalAlignment.Center;
        hit.VerticalAlignment = VerticalAlignment.Top;
        bagImg.Width = S; bagImg.Height = S;
        bagImg.HorizontalAlignment = HorizontalAlignment.Center;
        bagImg.VerticalAlignment = VerticalAlignment.Top;
        bagImg.Margin = new Thickness(0, 12, 0, 0);
        bagImg.Source = LoadImage(Cfg.ImagePath);
        label.Text = Cfg.Name;
        label.VerticalAlignment = VerticalAlignment.Bottom;
        badge.HorizontalAlignment = HorizontalAlignment.Right;
        badge.VerticalAlignment = VerticalAlignment.Top;
        badge.Margin = new Thickness(0, 2, (visW - hitS) / 2 + 2, 0);

        var items = Items();
        badgeText.Text = items.Count.ToString();
        badge.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var pts = ComputeRing(items.Count);
        double rMax = pts.Count == 0 ? 0 : pts.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
        double size = Math.Max(S + 260, 2 * (rMax + Tiles.W));
        center = size / 2;
        Width = size; Height = size;
        Left = Cfg.X - center;
        Top = Cfg.Y - center;

        Canvas.SetLeft(bagVisual, center - visW / 2);
        Canvas.SetTop(bagVisual, center - hitS / 2);
        double hr = rMax + Tiles.W * 0.85;
        halo.Width = halo.Height = 2 * hr;
        Canvas.SetLeft(halo, center - hr);
        Canvas.SetTop(halo, center - hr);
        Canvas.SetLeft(hintBox, center - hintBox.Width / 2);
        Canvas.SetTop(hintBox, center - hitS / 2 - 72);
    }

    // ---------- 펼치기 / 접기 ----------

    List<Point> ComputeRing(int n)
    {
        if (n == 0) return new List<Point>();
        var full = Rings(n, 2 * Math.PI, -Math.PI / 2);
        double rMax = full.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y)) + Tiles.W * 0.6;
        var wa = SystemParameters.WorkArea;
        int near = 0;
        if (Cfg.X - rMax < wa.Left || Cfg.X + rMax > wa.Right) near++;
        if (Cfg.Y - rMax < wa.Top || Cfg.Y + rMax > wa.Bottom) near++;
        if (near == 0) return full;
        // 화면 가장자리 근처면 화면 안쪽으로 부채꼴로 펼침
        double dir = Math.Atan2(wa.Top + wa.Height / 2 - Cfg.Y, wa.Left + wa.Width / 2 - Cfg.X);
        return Rings(n, near >= 2 ? Math.PI * 0.55 : Math.PI, dir);
    }

    List<Point> Rings(int n, double span, double dir)
    {
        var pts = new List<Point>();
        bool full = span >= 2 * Math.PI - 1e-6;
        double r0 = Cfg.Size / 2 + 12 + Tiles.W * 0.62;
        int placed = 0, ring = 0;
        while (placed < n)
        {
            double r = r0 + ring * Tiles.H * 0.98;
            int cap = Math.Max(1, (int)Math.Floor(r * span / (Tiles.W * 1.04)));
            int k = Math.Min(cap, n - placed);
            for (int i = 0; i < k; i++)
            {
                double a = full
                    ? dir + 2 * Math.PI * i / k + (ring % 2 == 1 ? Math.PI / k : 0)
                    : dir - span / 2 + span * (i + 0.5) / k;
                pts.Add(new Point(r * Math.Cos(a), r * Math.Sin(a)));
            }
            placed += k;
            ring++;
        }
        return pts;
    }

    void Bounce()
    {
        var ease = new ElasticEase { Oscillations = 2, Springiness = 6, EasingMode = EasingMode.EaseOut };
        var a = new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(600)) { EasingFunction = ease };
        bagScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        bagScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    void OpenRing()
    {
        var items = Items();
        if (items.Count == 0) { ShowHint(); return; }
        if (open) return;
        open = true;
        Activate();
        Bounce();

        var pts = ComputeRing(items.Count);
        ringRadius = pts.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
        halo.IsHitTestVisible = true;
        halo.BeginAnimation(OpacityProperty, Anim.To(1, 260));

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var tile = Tiles.Create(item, true);
            var st = new ScaleTransform(0.2, 0.2);
            var tt = new TranslateTransform();
            var tg = new TransformGroup();
            tg.Children.Add(st);
            tg.Children.Add(tt);
            tile.RenderTransform = tg;
            tile.RenderTransformOrigin = new Point(0.5, 0.5);
            tile.Opacity = 0;
            Canvas.SetLeft(tile, center - tile.Width / 2);
            Canvas.SetTop(tile, center - tile.Height / 2);
            Panel.SetZIndex(tile, 2);
            Tiles.Attach(tile, item, this, () => CloseRing(), () => ItemExtras(item), IsOutsideRing, (keys, p) =>
            {
                // 가방 밖 빈 곳에 놓으면 꺼냄 → 가방 오른쪽에 놓았으면 오른쪽, 왼쪽이면 왼쪽 가장자리로
                double s = Native.DipScale();
                var drop = new Point(p.X / s, p.Y / s);
                var side = drop.X >= Cfg.X ? Side.Right : Side.Left;
                App.Instance.TakeOutManyToSide(keys, Cfg, side, drop);
            });
            canvas.Children.Add(tile);
            ringTiles.Add(tile);

            double delay = i * 22;
            var ease = new BackEase { Amplitude = 0.6, EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.XProperty, Anim.To(pts[i].X, 470, delay, ease));
            tt.BeginAnimation(TranslateTransform.YProperty, Anim.To(pts[i].Y, 470, delay, ease));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1, 470, delay, ease));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1, 470, delay, ease));
            tile.BeginAnimation(OpacityProperty, Anim.To(1, 180, delay));
        }
    }

    double ringRadius;

    /// <summary>커서(물리 픽셀)가 펼쳐진 원 바깥에 있는지.</summary>
    bool IsOutsideRing(Native.POINT p)
    {
        double s = Native.DipScale();
        double dx = p.X / s - Cfg.X, dy = p.Y / s - Cfg.Y;
        return Math.Sqrt(dx * dx + dy * dy) > ringRadius + Tiles.W * 0.6;
    }

    public void CloseRing(bool animate = true)
    {
        if (!open) return;
        open = false;
        Sel.Clear(this);
        var old = ringTiles;
        ringTiles = new List<Border>();
        halo.IsHitTestVisible = false;

        if (!animate)
        {
            foreach (var t in old) canvas.Children.Remove(t);
            halo.BeginAnimation(OpacityProperty, null);
            halo.Opacity = 0;
            return;
        }

        halo.BeginAnimation(OpacityProperty, Anim.To(0, 220));
        for (int i = 0; i < old.Count; i++)
        {
            var t = old[i];
            t.IsHitTestVisible = false;
            var tg = (TransformGroup)t.RenderTransform;
            var st = (ScaleTransform)tg.Children[0];
            var tt = (TranslateTransform)tg.Children[1];
            double delay = (old.Count - 1 - i) * 10;
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
            tt.BeginAnimation(TranslateTransform.XProperty, Anim.To(0, 230, delay, ease));
            tt.BeginAnimation(TranslateTransform.YProperty, Anim.To(0, 230, delay, ease));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(0.2, 230, delay, ease));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(0.2, 230, delay, ease));
            t.BeginAnimation(OpacityProperty, Anim.To(0, 230, delay));
        }
        Anim.After(280 + old.Count * 10, () => { foreach (var t in old) canvas.Children.Remove(t); });
    }

    void ShowHint()
    {
        Bounce();
        var a = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(2600) };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2200))));
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2600))));
        hintBox.BeginAnimation(OpacityProperty, a);
    }

    // ---------- 메뉴 ----------

    /// <summary>Windows 원래 메뉴 맨 위에 붙는 DesktopPop 전용 항목.</summary>
    List<MenuEntry> ItemExtras(DesktopItem item)
    {
        var app = App.Instance;
        var targets = TargetsFor(item);
        string count = targets.Count > 1 ? $" ({targets.Count}개)" : "";
        // 꺼내면 가방에서 왼쪽 가장자리로 날아가 앉음
        var list = new List<MenuEntry> { MenuEntry.Item("가방에서 꺼내기" + count, () => app.TakeOutManyToSide(targets, Cfg, Side.Left, CenterDip)) };
        var others = app.Config.Bags.Where(b => b != Cfg).ToList();
        if (others.Count > 0)
            list.Add(MenuEntry.Sub("다른 가방으로 옮기기" + count,
                others.Select(b => MenuEntry.Item(b.Name, () => app.MoveManyToBag(targets, b))).ToList()));
        return list;
    }

    ContextMenu BagMenu()
    {
        var app = App.Instance;
        var cm = new ContextMenu();
        cm.Items.Add(Tiles.Item("이름 바꾸기...", () =>
        {
            var n = InputDialog.Ask("가방 이름", "새 이름을 입력해 주세요", Cfg.Name);
            if (string.IsNullOrWhiteSpace(n)) return;
            Cfg.Name = n;
            Rebuild();
            app.Config.Save();
        }));
        cm.Items.Add(Tiles.Item("그림 바꾸기...", () =>
        {
            var pick = BagLibraryWindow.Pick(false, Cfg.Name);
            if (pick == null) return;
            Cfg.ImagePath = pick.Value.image;
            Rebuild();
            app.Config.Save();
        }));
        var size = new MenuItem { Header = "크기" };
        foreach (var (name, px) in new[] { ("작게", 64.0), ("보통", 88.0), ("크게", 120.0), ("아주 크게", 160.0) })
        {
            var m = Tiles.Item(name, () => { Cfg.Size = px; Rebuild(); app.Config.Save(); });
            m.IsChecked = Math.Abs(Cfg.Size - px) < 0.5;
            size.Items.Add(m);
        }
        cm.Items.Add(size);
        cm.Items.Add(new Separator());
        cm.Items.Add(Tiles.Item("가방 지우기", () =>
        {
            var r = MessageBox.Show($"'{Cfg.Name}' 가방을 지울까요?\n안에 있던 아이콘은 왼쪽 가장자리로 돌아가요. (파일은 지워지지 않아요)",
                "DesktopPop", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes) app.DeleteBag(this);
        }));
        return cm;
    }

    // ---------- 그림 ----------

    static ImageSource LoadImage(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bi.DecodePixelWidth = 320;
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }
        catch (Exception ex) { App.Log(ex); }
        return DefaultImage();
    }

    static ImageSource DefaultImage()
    {
        var body = new LinearGradientBrush(Color.FromRgb(0xEC, 0x48, 0x99), Color.FromRgb(0x7C, 0x3A, 0xED), 45);
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(body, null, new RectangleGeometry(new Rect(8, 22, 84, 70), 16, 16)));
        g.Children.Add(new GeometryDrawing(null, new Pen(Brushes.White, 6), new RectangleGeometry(new Rect(32, 6, 36, 26), 10, 10)));
        g.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(0x55, 255, 255, 255)), null, new RectangleGeometry(new Rect(8, 44, 84, 10))));
        var img = new DrawingImage(g);
        img.Freeze();
        return img;
    }
}
