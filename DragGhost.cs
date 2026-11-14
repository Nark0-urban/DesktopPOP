using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopPop;

internal sealed class DragGhost : Window
{
	private readonly double scale;

	private readonly Border tile;

	private nint hwnd;

	private (int x, int y)? lastPosition;

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
		tile = Tiles.Create(item, backdrop: true);
		tile.Margin = new Thickness(16.0);
		tile.Effect = new DropShadowEffect
		{
			BlurRadius = 18.0,
			ShadowDepth = 5.0,
			Opacity = 0.55,
			Color = Colors.Black
		};
		tile.RenderTransformOrigin = new Point(0.5, 0.5);
		TransformGroup renderTransform = new TransformGroup
		{
			Children = 
			{
				(Transform)new ScaleTransform(1.08, 1.08),
				(Transform)new RotateTransform(-6.0)
			}
		};
		tile.RenderTransform = renderTransform;
		if (count <= 1)
		{
			Content = tile;
		}
		else
		{
			Border element = new Border
			{
				Width = tile.Width,
				Height = tile.Height,
				Margin = new Thickness(16.0),
				CornerRadius = new CornerRadius(12.0),
				Background = Theme.Brush(Theme.Backdrop, 176),
				BorderBrush = Theme.Brush(Theme.Accent, 136),
				BorderThickness = new Thickness(1.0),
				RenderTransformOrigin = new Point(0.5, 0.5),
				RenderTransform = new RotateTransform(5.0)
			};
			Border element2 = new Border
			{
				MinWidth = 24.0,
				Height = 24.0,
				CornerRadius = new CornerRadius(12.0),
				Padding = new Thickness(6.0, 0.0, 6.0, 0.0),
				Background = Theme.Brush(Theme.Badge),
				HorizontalAlignment = HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Top,
				Margin = new Thickness(0.0, 6.0, 6.0, 0.0),
				Child = new TextBlock
				{
					Text = count.ToString(),
					Foreground = Brushes.White,
					FontWeight = FontWeights.Bold,
					FontSize = 12.0,
					HorizontalAlignment = HorizontalAlignment.Center,
					VerticalAlignment = VerticalAlignment.Center
				}
			};
			Content = new Grid
			{
				Children = 
				{
					(UIElement)element,
					(UIElement)tile,
					(UIElement)element2
				}
			};
		}
		scale = Native.DipScale();
		SourceInitialized += (object? s, EventArgs e) =>
		{
			hwnd = new WindowInteropHelper(this).Handle;
			Native.MakeToolWindow(hwnd);
			long num = ((IntPtr)Native.GetWindowLongPtr(hwnd, -20)).ToInt64();
			num |= 0x8080020;
			Native.SetWindowLongPtr(hwnd, -20, new IntPtr(num));
		};
	}

	public void Follow()
	{
		if (hwnd == IntPtr.Zero || !Native.GetCursorPos(out var p))
		{
			return;
		}
		double widthDip = ((ActualWidth > 0.0) ? ActualWidth : 118.0);
		(int, int) tuple = OverlayMotion.GhostPosition(p.X, p.Y, widthDip, scale);
		if (lastPosition.HasValue)
		{
			(int, int) value = lastPosition.Value;
			(int, int) tuple2 = tuple;
			if (value.Item1 == tuple2.Item1 && value.Item2 == tuple2.Item2)
			{
				return;
			}
		}
		if (Native.SetWindowPos(hwnd, IntPtr.Zero, tuple.Item1, tuple.Item2, 0, 0, 21u))
		{
			lastPosition = tuple;
		}
	}

	public void SetValid(bool valid)
	{
		tile.Opacity = (valid ? 1.0 : 0.6);
	}
}
