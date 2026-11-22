using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopPop;

/// <summary>아이콘을 끄는 동안 마우스를 따라다니는 반투명 그림.</summary>
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
			// 클릭이 통과하고(WS_EX_TRANSPARENT) 활성화되지 않는(WS_EX_NOACTIVATE) 반투명(WS_EX_LAYERED) 창
			long exStyle = ((IntPtr)Native.GetWindowLongPtr(hwnd, -20)).ToInt64();
			exStyle |= 0x8080020;
			Native.SetWindowLongPtr(hwnd, -20, new IntPtr(exStyle));
		};
	}

	public void Follow()
	{
		if (hwnd == IntPtr.Zero || !Native.GetCursorPos(out var p))
		{
			return;
		}
		double widthDip = ((ActualWidth > 0.0) ? ActualWidth : 118.0);
		(int, int) target = OverlayMotion.GhostPosition(p.X, p.Y, widthDip, scale);
		if (lastPosition.HasValue)
		{
			(int, int) value = lastPosition.Value;
			if (value.Item1 == target.Item1 && value.Item2 == target.Item2) // 그대로면 창을 옮기지 않음
			{
				return;
			}
		}
		if (Native.SetWindowPos(hwnd, IntPtr.Zero, target.Item1, target.Item2, 0, 0, 21u))
		{
			lastPosition = target;
		}
	}

	public void SetValid(bool valid)
	{
		tile.Opacity = (valid ? 1.0 : 0.6);
	}
}
