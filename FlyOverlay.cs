using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace DesktopPop;

/// <summary>새로 생긴 아이콘이 마우스 자리에서 판의 제자리로 날아가는 연출 창.</summary>
internal sealed class FlyOverlay : Window
{
	private static FlyOverlay? inst;

	private readonly Canvas canvas = new Canvas();

	private readonly TranslateTransform viewportOffset = new TranslateTransform();

	private Rect flightArea = Rect.Empty;

	private double desktopTop;

	private int active;

	private bool closed;

	private FlyOverlay()
	{
		WindowStyle = WindowStyle.None;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		ShowInTaskbar = false;
		Topmost = true;
		ShowActivated = false;
		ResizeMode = ResizeMode.NoResize;
		IsHitTestVisible = false;
		canvas.RenderTransform = viewportOffset;
		Content = canvas;
		Closed += (object? s, EventArgs e) =>
		{
			closed = true;
			if (inst == this)
			{
				inst = null;
			}
		};
		SourceInitialized += (object? s, EventArgs e) =>
		{
			nint handle = new WindowInteropHelper(this).Handle;
			Native.MakeToolWindow(handle);
			// 클릭이 통과하고(WS_EX_TRANSPARENT) 활성화되지 않는(WS_EX_NOACTIVATE) 반투명(WS_EX_LAYERED) 창
			long exStyle = ((IntPtr)Native.GetWindowLongPtr(handle, -20)).ToInt64();
			exStyle |= 0x8080020;
			Native.SetWindowLongPtr(handle, -20, new IntPtr(exStyle));
		};
	}

	public static void Fly(DesktopItem item, Point from, Point to, double delayMs, Action onArrive)
	{
		FlyOverlay o = inst ?? (inst = new FlyOverlay());
		Border tile = Tiles.Create(item, backdrop: true);
		tile.IsHitTestVisible = false;
		tile.Effect = new DropShadowEffect
		{
			BlurRadius = 16.0,
			ShadowDepth = 4.0,
			Opacity = 0.5,
			Color = Colors.Black
		};
		if (o.active == 0)
		{
			o.flightArea = Rect.Empty;
			o.desktopTop = Native.ToDipRect(Native.VirtualScreenRect()).Top;
		}
		Point point = OverlayMotion.FlightControl(from, to, o.desktopTop);
		o.Include(OverlayMotion.FlightBounds(from, point, to, tile.Width, tile.Height));
		if (!o.IsVisible)
		{
			o.Show();
			Native.SetTopmost(new WindowInteropHelper(o).Handle);
		}
		o.active++;
		Canvas.SetLeft(tile, (0.0 - tile.Width) / 2.0);
		Canvas.SetTop(tile, (0.0 - tile.Height) / 2.0);
		ScaleTransform scaleTransform = new ScaleTransform(0.2, 0.2);
		TranslateTransform translateTransform = new TranslateTransform(from.X, from.Y);
		TransformGroup transformGroup = new TransformGroup();
		transformGroup.Children.Add(scaleTransform);
		transformGroup.Children.Add(translateTransform);
		tile.RenderTransform = transformGroup;
		tile.RenderTransformOrigin = new Point(0.5, 0.5);
		tile.Opacity = 0.0;
		o.canvas.Children.Add(tile);
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
		{
			BeginTime = TimeSpan.FromMilliseconds(delayMs)
		};
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.2, KeyTime.FromTimeSpan(TimeSpan.Zero)));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280.0)), new BackEase
		{
			Amplitude = 0.6,
			EasingMode = EasingMode.EaseOut
		}));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.15, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(400.0))));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1050.0)), new QuadraticEase
		{
			EasingMode = EasingMode.EaseInOut
		}));
		scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, doubleAnimationUsingKeyFrames);
		scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, doubleAnimationUsingKeyFrames);
		tile.BeginAnimation(UIElement.OpacityProperty, Anim.To(1.0, 120.0, delayMs));
		PathGeometry pathGeometry = new PathGeometry(new PathFigure[1]
		{
			new PathFigure(from, new PathSegment[1]
			{
				new QuadraticBezierSegment(point, to, isStroked: true)
			}, closed: false)
		});
		pathGeometry.Freeze();
		TimeSpan value = TimeSpan.FromMilliseconds(delayMs + 280.0 + 120.0);
		Duration duration = new Duration(TimeSpan.FromMilliseconds(650.0));
		DoubleAnimationUsingPath doubleAnimationUsingPath = new DoubleAnimationUsingPath
		{
			PathGeometry = pathGeometry,
			Source = PathAnimationSource.X,
			Duration = duration,
			BeginTime = value,
			AccelerationRatio = 0.35,
			DecelerationRatio = 0.65
		};
		DoubleAnimationUsingPath animation = new DoubleAnimationUsingPath
		{
			PathGeometry = pathGeometry,
			Source = PathAnimationSource.Y,
			Duration = duration,
			BeginTime = value,
			AccelerationRatio = 0.35,
			DecelerationRatio = 0.65
		};
		doubleAnimationUsingPath.Completed += (object? s, EventArgs e) =>
		{
			o.canvas.Children.Remove(tile);
			try
			{
				onArrive();
			}
			catch (Exception ex)
			{
				App.Log(ex);
			}
			if (--o.active <= 0)
			{
				o.active = 0;
				if (!o.closed)
				{
					o.Hide();
				}
			}
		};
		translateTransform.BeginAnimation(TranslateTransform.XProperty, doubleAnimationUsingPath);
		translateTransform.BeginAnimation(TranslateTransform.YProperty, animation);
	}

	private void Include(Rect next)
	{
		Rect rect = (flightArea.IsEmpty ? next : Rect.Union(flightArea, next));
		if (!(rect == flightArea))
		{
			flightArea = rect;
			Left = rect.Left;
			Top = rect.Top;
			Width = rect.Width;
			Height = rect.Height;
			viewportOffset.X = 0.0 - rect.Left;
			viewportOffset.Y = 0.0 - rect.Top;
		}
	}
}
