using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace DesktopPop;

/// <summary>켤 때 연출: 가운데 로딩 원 → 빛줄기가 훑으며 원래 아이콘이 사라짐.</summary>
internal sealed class StartupOverlay : Window
{
	private readonly Grid root = new Grid();

	private readonly Border dim = new Border();

	private readonly Canvas scanLayer = new Canvas();

	private readonly StackPanel center = new StackPanel();

	private readonly ScaleTransform centerScale = new ScaleTransform(0.6, 0.6);

	private readonly double w;

	private readonly double h;

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
		Left = 0.0;
		Top = 0.0;
		Width = w;
		Height = h;
		SourceInitialized += (object? s, EventArgs e) =>
		{
			nint handle = new WindowInteropHelper(this).Handle;
			Native.MakeToolWindow(handle);
			// 클릭이 통과하고(WS_EX_TRANSPARENT) 활성화되지 않는(WS_EX_NOACTIVATE) 반투명(WS_EX_LAYERED) 창
			long exStyle = ((IntPtr)Native.GetWindowLongPtr(handle, -20)).ToInt64();
			exStyle |= 0x8080020;
			Native.SetWindowLongPtr(handle, -20, new IntPtr(exStyle));
		};
		Color accent = Theme.Accent;
		RadialGradientBrush background = new RadialGradientBrush
		{
			GradientStops = 
			{
				new GradientStop(Theme.WithAlpha(Theme.Panel, 80), 0.0),
				new GradientStop(Color.FromArgb(153, 0, 0, 0), 1.0)
			}
		};
		dim.Background = background;
		dim.Opacity = 0.0;
		root.Children.Add(dim);
		Grid grid = new Grid
		{
			Width = 180.0,
			Height = 180.0
		};
		grid.Children.Add(Ring(130.0, Color.FromArgb(38, byte.MaxValue, byte.MaxValue, byte.MaxValue), 4.0, null));
		Ellipse ellipse = Ring(120.0, accent, 2.0, null);
		Ellipse ellipse2 = Ring(130.0, accent, 4.0, new DoubleCollection { 10.0, 14.0 });
		Ellipse ellipse3 = Ring(98.0, Theme.WithAlpha(Theme.Glow, 204), 3.0, new DoubleCollection { 4.0, 8.0 });
		Ellipse ellipse4 = new Ellipse
		{
			Width = 16.0,
			Height = 16.0,
			Fill = Theme.Brush(accent),
			Effect = new DropShadowEffect
			{
				Color = accent,
				ShadowDepth = 0.0,
				BlurRadius = 22.0,
				Opacity = 1.0
			},
			RenderTransformOrigin = new Point(0.5, 0.5)
		};
		grid.Children.Add(ellipse);
		grid.Children.Add(ellipse2);
		grid.Children.Add(ellipse3);
		grid.Children.Add(ellipse4);
		Spin(ellipse2, 0.0, 360.0, 1100.0);
		Spin(ellipse3, 360.0, 0.0, 1700.0);
		ScaleTransform scaleTransform = new ScaleTransform(1.0, 1.0);
		ellipse.RenderTransformOrigin = new Point(0.5, 0.5);
		ellipse.RenderTransform = scaleTransform;
		DoubleAnimation animation = new DoubleAnimation(1.0, 2.6, TimeSpan.FromMilliseconds(1600L))
		{
			RepeatBehavior = RepeatBehavior.Forever,
			EasingFunction = new QuadraticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
		scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
		ellipse.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.9, 0.0, TimeSpan.FromMilliseconds(1600L))
		{
			RepeatBehavior = RepeatBehavior.Forever
		});
		ScaleTransform scaleTransform2 = (ScaleTransform)(ellipse4.RenderTransform = new ScaleTransform(1.0, 1.0));
		DoubleAnimation animation2 = new DoubleAnimation(0.8, 1.25, TimeSpan.FromMilliseconds(600L))
		{
			AutoReverse = true,
			RepeatBehavior = RepeatBehavior.Forever,
			EasingFunction = new SineEase()
		};
		scaleTransform2.BeginAnimation(ScaleTransform.ScaleXProperty, animation2);
		scaleTransform2.BeginAnimation(ScaleTransform.ScaleYProperty, animation2);
		center.HorizontalAlignment = HorizontalAlignment.Center;
		center.VerticalAlignment = VerticalAlignment.Center;
		center.RenderTransformOrigin = new Point(0.5, 0.5);
		center.RenderTransform = centerScale;
		center.Opacity = 0.0;
		center.Children.Add(grid);
		center.Children.Add(new TextBlock
		{
			Text = "DesktopPop",
			Foreground = Brushes.White,
			FontSize = 15.0,
			FontWeight = FontWeights.SemiBold,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
			Opacity = 0.9,
			Effect = new DropShadowEffect
			{
				ShadowDepth = 1.0,
				BlurRadius = 6.0,
				Opacity = 0.9,
				Color = Colors.Black
			}
		});
		root.Children.Add(center);
		root.Children.Add(scanLayer);
		Content = root;
		Loaded += (object s, RoutedEventArgs e) =>
		{
			dim.BeginAnimation(UIElement.OpacityProperty, Anim.To(1.0, 400.0));
			center.BeginAnimation(UIElement.OpacityProperty, Anim.To(1.0, 300.0));
			BackEase ease = new BackEase
			{
				Amplitude = 0.5,
				EasingMode = EasingMode.EaseOut
			};
			centerScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.0, 450.0, 0.0, ease));
			centerScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.0, 450.0, 0.0, ease));
		};
	}

	private static Ellipse Ring(double size, Color color, double thickness, DoubleCollection? dash)
	{
		Ellipse ellipse = new Ellipse
		{
			Width = size,
			Height = size,
			Stroke = Theme.Brush(color, color.A),
			StrokeThickness = thickness
		};
		if (dash != null)
		{
			ellipse.StrokeDashArray = dash;
			ellipse.StrokeDashCap = PenLineCap.Round;
		}
		return ellipse;
	}

	private static void Spin(UIElement el, double from, double to, double ms)
	{
		RotateTransform rotateTransform = new RotateTransform(from);
		el.RenderTransformOrigin = new Point(0.5, 0.5);
		el.RenderTransform = rotateTransform;
		rotateTransform.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
		{
			RepeatBehavior = RepeatBehavior.Forever
		});
	}

	public void PlayScan(Action onSwept, Action done)
	{
		Color accent = Theme.Accent;
		GeometryGroup geometryGroup = new GeometryGroup();
		geometryGroup.Children.Add(new LineGeometry(new Point(0.0, 0.0), new Point(46.0, 0.0)));
		geometryGroup.Children.Add(new LineGeometry(new Point(0.0, 0.0), new Point(0.0, 46.0)));
		DrawingBrush drawingBrush = new DrawingBrush(new DrawingGroup
		{
			Children = 
			{
				(Drawing)new GeometryDrawing(null, new Pen(Theme.Brush(accent, 112), 1.0), geometryGroup),
				(Drawing)new GeometryDrawing(Theme.Brush(accent, 208), null, new RectangleGeometry(new Rect(-1.5, -1.5, 3.0, 3.0)))
			}
		})
		{
			TileMode = TileMode.Tile,
			Viewport = new Rect(0.0, 0.0, 46.0, 46.0),
			ViewportUnits = BrushMappingMode.Absolute,
			Viewbox = new Rect(-1.5, -1.5, 46.0, 46.0),
			ViewboxUnits = BrushMappingMode.Absolute,
			Stretch = Stretch.None
		};
		drawingBrush.Freeze();
		double trail = Math.Max(500.0, w * 0.45); // 빛줄기 뒤로 어두워지는 꼬리 길이
		double head = 30.0;
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush
		{
			MappingMode = BrushMappingMode.Absolute,
			StartPoint = new Point(0.0 - trail, 0.0),
			EndPoint = new Point(head, 0.0)
		};
		double edge = trail / (trail + head);
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(85, 0, 0, 0), edge * 0.6));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(byte.MaxValue, 0, 0, 0), edge));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 1.0));
		TranslateTransform translateTransform = (TranslateTransform)(linearGradientBrush.Transform = new TranslateTransform(-22.399999999999977, 0.0));
		Rectangle grid = new Rectangle
		{
			Width = w,
			Height = h,
			Fill = drawingBrush,
			OpacityMask = linearGradientBrush,
			IsHitTestVisible = false,
			CacheMode = new BitmapCache()
		};
		scanLayer.Children.Add(grid);
		LinearGradientBrush linearGradientBrush2 = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.5),
			EndPoint = new Point(1.0, 0.5)
		};
		linearGradientBrush2.GradientStops.Add(new GradientStop(Theme.WithAlpha(accent, 0), 0.0));
		linearGradientBrush2.GradientStops.Add(new GradientStop(Theme.WithAlpha(accent, 48), 0.55));
		linearGradientBrush2.GradientStops.Add(new GradientStop(Theme.WithAlpha(accent, 160), 0.92));
		linearGradientBrush2.GradientStops.Add(new GradientStop(Theme.WithAlpha(accent, 0), 1.0));
		Rectangle rectangle = new Rectangle
		{
			Width = 280.0,
			Height = h,
			Fill = linearGradientBrush2,
			IsHitTestVisible = false
		};
		TranslateTransform translateTransform2 = (TranslateTransform)(rectangle.RenderTransform = new TranslateTransform(-280.0, 0.0));
		scanLayer.Children.Add(rectangle);
		QuadraticEase easingFunction = new QuadraticEase
		{
			EasingMode = EasingMode.EaseInOut
		};
		DoubleAnimation doubleAnimation = new DoubleAnimation(-280.0, w, TimeSpan.FromMilliseconds(1250.0))
		{
			EasingFunction = easingFunction
		};
		DoubleAnimation animation = new DoubleAnimation(-22.399999999999977, w + 257.6, TimeSpan.FromMilliseconds(1250.0))
		{
			EasingFunction = easingFunction
		};
		doubleAnimation.Completed += (object? s, EventArgs e) =>
		{
			grid.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 380.0));
			centerScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.25, 280.0));
			centerScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.25, 280.0));
			center.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 280.0));
			dim.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 380.0));
			Anim.After(400.0, done);
		};
		translateTransform2.BeginAnimation(TranslateTransform.XProperty, doubleAnimation);
		translateTransform.BeginAnimation(TranslateTransform.XProperty, animation);
		Anim.After(937.5, onSwept);
	}
}
