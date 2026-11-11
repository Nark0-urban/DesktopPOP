using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DesktopPop;

/// <summary>
/// 앱이 켜질 때 화면 전체에 나오는 연출:
/// 가운데 원형 로딩이 돌다가 → 빛줄기가 화면을 훑고 지나가며 원래 아이콘이 사라짐 → 서서히 사라짐.
/// 클릭은 전부 통과시킴.
/// </summary>
internal sealed class StartupOverlay : Window
{
    readonly Grid root = new();
    readonly Border dim = new();
    readonly Canvas scanLayer = new();
    readonly StackPanel center = new();
    readonly ScaleTransform centerScale = new(0.6, 0.6);
    readonly double w, h;

    public StartupOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Title = "DesktopPop Startup";
        w = SystemParameters.PrimaryScreenWidth;
        h = SystemParameters.PrimaryScreenHeight;
        Left = 0; Top = 0; Width = w; Height = h;

        SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(hwnd);
            const int GWL_EXSTYLE = -20;
            long ex = Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            ex |= 0x20 | 0x80000 | 0x08000000; // 마우스가 뚫고 지나감
            Native.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };

        var accent = Theme.Accent;

        // 화면 전체를 살짝 어둡게 (가운데는 테마색, 바깥은 검게)
        var vignette = new RadialGradientBrush();
        vignette.GradientStops.Add(new GradientStop(Theme.WithAlpha(Theme.Panel, 0x50), 0));
        vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0x99, 0, 0, 0), 1));
        dim.Background = vignette;
        dim.Opacity = 0;
        root.Children.Add(dim);

        // 가운데 원형 로딩
        var spinner = new Grid { Width = 180, Height = 180 };
        spinner.Children.Add(Ring(130, Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF), 4, null));      // 바탕 고리
        var ripple = Ring(120, accent, 2, null);                                               // 퍼지는 물결
        var arc1 = Ring(130, accent, 4, new DoubleCollection { 10, 14 });                       // 도는 고리 1
        var arc2 = Ring(98, Theme.WithAlpha(Theme.Glow, 0xCC), 3, new DoubleCollection { 4, 8 }); // 반대로 도는 고리 2
        var core = new Ellipse
        {
            Width = 16, Height = 16, Fill = Theme.Brush(accent),
            Effect = new DropShadowEffect { Color = accent, ShadowDepth = 0, BlurRadius = 22, Opacity = 1 },
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        spinner.Children.Add(ripple);
        spinner.Children.Add(arc1);
        spinner.Children.Add(arc2);
        spinner.Children.Add(core);

        Spin(arc1, 0, 360, 1100);
        Spin(arc2, 360, 0, 1700);
        var rs = new ScaleTransform(1, 1);
        ripple.RenderTransformOrigin = new Point(0.5, 0.5);
        ripple.RenderTransform = rs;
        var grow = new DoubleAnimation(1, 2.6, TimeSpan.FromMilliseconds(1600)) { RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        rs.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        rs.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        ripple.BeginAnimation(OpacityProperty, new DoubleAnimation(0.9, 0, TimeSpan.FromMilliseconds(1600)) { RepeatBehavior = RepeatBehavior.Forever });
        var cs = new ScaleTransform(1, 1);
        core.RenderTransform = cs;
        var pulse = new DoubleAnimation(0.8, 1.25, TimeSpan.FromMilliseconds(600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() };
        cs.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        cs.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);

        center.HorizontalAlignment = HorizontalAlignment.Center;
        center.VerticalAlignment = VerticalAlignment.Center;
        center.RenderTransformOrigin = new Point(0.5, 0.5);
        center.RenderTransform = centerScale;
        center.Opacity = 0;
        center.Children.Add(spinner);
        center.Children.Add(new TextBlock
        {
            Text = "DesktopPop", Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Opacity = 0.9,
            Effect = new DropShadowEffect { ShadowDepth = 1, BlurRadius = 6, Opacity = 0.9, Color = Colors.Black }
        });
        root.Children.Add(center);
        root.Children.Add(scanLayer);
        Content = root;

        Loaded += (s, e) =>
        {
            dim.BeginAnimation(OpacityProperty, Anim.To(1, 400));
            center.BeginAnimation(OpacityProperty, Anim.To(1, 300));
            var pop = new BackEase { Amplitude = 0.5, EasingMode = EasingMode.EaseOut };
            centerScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1, 450, 0, pop));
            centerScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1, 450, 0, pop));
        };
    }

    static Ellipse Ring(double size, Color color, double thickness, DoubleCollection? dash)
    {
        var e = new Ellipse { Width = size, Height = size, Stroke = Theme.Brush(color, color.A), StrokeThickness = thickness };
        if (dash != null)
        {
            e.StrokeDashArray = dash;
            e.StrokeDashCap = PenLineCap.Round;
        }
        return e;
    }

    static void Spin(UIElement el, double from, double to, double ms)
    {
        var rt = new RotateTransform(from);
        el.RenderTransformOrigin = new Point(0.5, 0.5);
        el.RenderTransform = rt;
        rt.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { RepeatBehavior = RepeatBehavior.Forever });
    }

    /// <summary>빛줄기가 왼쪽→오른쪽으로 화면을 훑음. 지나가는 도중 onSwept(원래 아이콘 숨기기), 끝나면 사라지고 done.</summary>
    public void PlayScan(Action onSwept, Action done)
    {
        const double bandW = 280, ms = 1250, lead = 0.92; // lead = 막대에서 가장 진한 앞쪽 끝 위치
        var accent = Theme.Accent;

        // ── 막대가 지나간 자리에 남는 격자 (막대에서 멀어질수록 흐려짐) ──
        const double cell = 46;
        var lines = new GeometryGroup();
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(cell, 0)));
        lines.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, cell)));
        var tileDrawing = new DrawingGroup();
        tileDrawing.Children.Add(new GeometryDrawing(null, new Pen(Theme.Brush(accent, 0x70), 1), lines));
        tileDrawing.Children.Add(new GeometryDrawing(Theme.Brush(accent, 0xD0), null, new RectangleGeometry(new Rect(-1.5, -1.5, 3, 3)))); // 교차점 점
        var gridBrush = new DrawingBrush(tileDrawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, cell, cell), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(-1.5, -1.5, cell, cell), ViewboxUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
        gridBrush.Freeze();

        // 막대 앞쪽 끝 = 0 기준: 뒤로 trail 만큼 서서히 사라지고, 앞쪽(아직 안 지나간 곳)은 안 보임
        double trail = Math.Max(500, w * 0.45), ahead = 30;
        var mask = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(-trail, 0), EndPoint = new Point(ahead, 0) };
        double at = trail / (trail + ahead);
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 0));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 0, 0, 0), at * 0.6));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0, 0, 0), at));
        mask.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0, 0, 0), 1));
        var maskMove = new TranslateTransform(-bandW + bandW * lead, 0);
        mask.Transform = maskMove;
        var grid = new Rectangle
        {
            Width = w, Height = h, Fill = gridBrush, OpacityMask = mask, IsHitTestVisible = false,
            CacheMode = new BitmapCache() // 격자는 한 번만 그려 두고 가리개만 움직임
        };
        scanLayer.Children.Add(grid);

        // ── 스캔 막대: 테마 색 하나로, 앞쪽 끝이 가장 진하고 뒤로 갈수록 옅어짐 ──
        var g = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        g.GradientStops.Add(new GradientStop(Theme.WithAlpha(accent, 0x00), 0));
        g.GradientStops.Add(new GradientStop(Theme.WithAlpha(accent, 0x30), 0.55));
        g.GradientStops.Add(new GradientStop(Theme.WithAlpha(accent, 0xA0), lead));
        g.GradientStops.Add(new GradientStop(Theme.WithAlpha(accent, 0x00), 1));
        var band = new Rectangle { Width = bandW, Height = h, Fill = g, IsHitTestVisible = false };
        var tt = new TranslateTransform(-bandW, 0);
        band.RenderTransform = tt;
        scanLayer.Children.Add(band);

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
        var sweep = new DoubleAnimation(-bandW, w, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        var maskSweep = new DoubleAnimation(-bandW + bandW * lead, w + bandW * lead, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
        sweep.Completed += (s, e) =>
        {
            // 남은 격자·로딩 원·화면 어둠이 함께 걷힘
            grid.BeginAnimation(OpacityProperty, Anim.To(0, 380));
            centerScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.25, 280));
            centerScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.25, 280));
            center.BeginAnimation(OpacityProperty, Anim.To(0, 280));
            dim.BeginAnimation(OpacityProperty, Anim.To(0, 380));
            Anim.After(400, done);
        };
        tt.BeginAnimation(TranslateTransform.XProperty, sweep);
        maskMove.BeginAnimation(TranslateTransform.XProperty, maskSweep);
        Anim.After(ms * 0.75, onSwept); // 막대가 대부분 지나간 뒤 원래 아이콘을 숨김
    }
}
