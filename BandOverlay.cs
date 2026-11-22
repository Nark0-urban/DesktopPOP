using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DesktopPop;

/// <summary>바탕화면에서 끌어서 여러 개 고를 때 보이는 선택 네모 창.</summary>
internal sealed class BandOverlay : Window
{
	private readonly Canvas canvas = new Canvas();

	private readonly Rectangle rect;

	private int hideToken;

	public BandOverlay()
	{
		WindowStyle = WindowStyle.None;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		ShowInTaskbar = false;
		Topmost = true;
		ShowActivated = false;
		ResizeMode = ResizeMode.NoResize;
		IsHitTestVisible = false;
		Title = "DesktopPop Band";
		rect = new Rectangle
		{
			StrokeThickness = 1.0,
			RadiusX = 3.0,
			RadiusY = 3.0,
			IsHitTestVisible = false
		};
		canvas.Children.Add(rect);
		Content = canvas;
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

	public void ShowBand()
	{
		hideToken++;
		this.rect.Fill = Theme.Brush(Theme.Accent, 48);
		this.rect.Stroke = Theme.Brush(Theme.Accent, 204);
		Rectangle rectangle = this.rect;
		double width = (this.rect.Height = 0.0);
		rectangle.Width = width;
		this.rect.Visibility = Visibility.Visible;
		Rect rect = Native.ToDipRect(Native.VirtualScreenRect());
		Left = rect.Left;
		Top = rect.Top;
		Width = rect.Width;
		Height = rect.Height;
		if (!IsVisible)
		{
			Show();
		}
		Native.SetTopmost(new WindowInteropHelper(this).Handle);
	}

	public void SetBand(Native.POINT a, Native.POINT b)
	{
		double scale = Native.DipScale();
		Rect rect = new Rect(new Point((double)a.X / scale - Left, (double)a.Y / scale - Top), new Point((double)b.X / scale - Left, (double)b.Y / scale - Top));
		Canvas.SetLeft(this.rect, rect.X);
		Canvas.SetTop(this.rect, rect.Y);
		this.rect.Width = rect.Width;
		this.rect.Height = rect.Height;
	}

	public void HideBand()
	{
		rect.Visibility = Visibility.Hidden;
		Rectangle rectangle = rect;
		double width = (rect.Height = 0.0);
		rectangle.Width = width;
		int token = ++hideToken;
		Dispatcher.InvokeAsync(() =>
		{
			if (token == hideToken)
			{
				Hide();
			}
		}, DispatcherPriority.Background);
	}
}
