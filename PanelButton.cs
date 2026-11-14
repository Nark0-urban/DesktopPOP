using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace DesktopPop;

internal sealed class PanelButton : Window
{
	private readonly App app;

	private readonly Border surface;

	private readonly TextBlock label;

	private Native.POINT? down;

	private double startLeft;

	private double startTop;

	private bool dragging;

	public PanelButton(App app)
	{
		PanelButton panelButton = this;
		this.app = app;
		Title = "DesktopPop 패널 버튼";
		Width = 112.0;
		Height = 44.0;
		WindowStyle = WindowStyle.None;
		ResizeMode = ResizeMode.NoResize;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		Topmost = true;
		ShowInTaskbar = false;
		ShowActivated = false;
		label = new TextBlock
		{
			Foreground = Brushes.White,
			FontSize = 13.0,
			FontWeight = FontWeights.SemiBold,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		surface = new Border
		{
			CornerRadius = new CornerRadius(22.0),
			BorderThickness = new Thickness(1.0),
			BorderBrush = new SolidColorBrush(Color.FromArgb(110, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
			Margin = new Thickness(2.0),
			Child = label,
			Cursor = Cursors.Hand,
			ToolTip = "클릭: 모든 모니터의 패널과 가방 펼치기 / 접기\n바깥 클릭·다른 프로그램 작업 시 자동으로 접힘\n드래그: 버튼 이동 (작업 표시줄 위에도 배치 가능)\n우클릭: 버튼 메뉴"
		};
		Content = surface;
		SourceInitialized += (object? s, EventArgs e) =>
		{
			nint handle = new WindowInteropHelper(panelButton).Handle;
			Native.MakeToolWindow(handle);
			long num = ((IntPtr)Native.GetWindowLongPtr(handle, -20)).ToInt64();
			Native.SetWindowLongPtr(handle, -20, new IntPtr(num | 0x8000000));
			Native.SetTopmost(handle);
		};
		Rect rect = Native.ToDipRect(Native.Monitors().First((Native.MonitorInfo m) => m.IsPrimary).WorkArea);
		double? panelButtonX = app.Config.PanelButtonX;
		double left;
		if (panelButtonX.HasValue)
		{
			double valueOrDefault = panelButtonX.GetValueOrDefault();
			if (double.IsFinite(valueOrDefault))
			{
				left = valueOrDefault;
				goto IL_01e5;
			}
		}
		left = rect.Right - Width - 24.0;
		goto IL_01e5;
		IL_0231:
		double top;
		Top = top;
		KeepOnScreen();
		UpdateState();
		surface.MouseLeftButtonDown += (object s, MouseButtonEventArgs e) =>
		{
			if (Native.GetCursorPos(out var p))
			{
				panelButton.down = p;
				panelButton.startLeft = panelButton.Left;
				panelButton.startTop = panelButton.Top;
				panelButton.dragging = false;
				panelButton.surface.CaptureMouse();
				e.Handled = true;
			}
		};
		surface.MouseMove += (object s, MouseEventArgs e) =>
		{
			Native.POINT? pOINT = panelButton.down;
			if (pOINT.HasValue)
			{
				Native.POINT valueOrDefault3 = pOINT.GetValueOrDefault();
				if (e.LeftButton == MouseButtonState.Pressed && Native.GetCursorPos(out var p))
				{
					double num = Native.DipScale();
					double num2 = (double)(p.X - valueOrDefault3.X) / num;
					double num3 = (double)(p.Y - valueOrDefault3.Y) / num;
					if (panelButton.dragging || !(Math.Abs(num2) < SystemParameters.MinimumHorizontalDragDistance) || !(Math.Abs(num3) < SystemParameters.MinimumVerticalDragDistance))
					{
						panelButton.dragging = true;
						panelButton.Left = panelButton.startLeft + num2;
						panelButton.Top = panelButton.startTop + num3;
					}
				}
			}
		};
		surface.MouseLeftButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			if (panelButton.down.HasValue)
			{
				panelButton.down = null;
				panelButton.surface.ReleaseMouseCapture();
				if (panelButton.dragging)
				{
					panelButton.KeepOnScreen();
					app.Config.PanelButtonX = panelButton.Left;
					app.Config.PanelButtonY = panelButton.Top;
					app.Config.Save();
				}
				else
				{
					app.ToggleAllPanels();
				}
				e.Handled = true;
			}
		};
		surface.LostMouseCapture += (object s, MouseEventArgs e) =>
		{
			panelButton.down = null;
		};
		surface.MouseRightButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			ContextMenu contextMenu = new ContextMenu
			{
				Items = 
				{
					(object)Tiles.Item(app.AllPanelsOpen ? "패널과 가방 모두 접기" : "패널과 가방 모두 펼치기", app.ToggleAllPanels),
					(object)Tiles.Item("버튼 숨기기 (시계 옆 메뉴에서 다시 표시)", () =>
					{
						app.SetPanelButtonVisible(visible: false);
					})
				}
			};
			App.Track(contextMenu);
			contextMenu.PlacementTarget = panelButton.surface;
			contextMenu.IsOpen = true;
			e.Handled = true;
		};
		return;
		IL_01e5:
		Left = left;
		panelButtonX = app.Config.PanelButtonY;
		if (panelButtonX.HasValue)
		{
			double valueOrDefault2 = panelButtonX.GetValueOrDefault();
			if (double.IsFinite(valueOrDefault2))
			{
				top = valueOrDefault2;
				goto IL_0231;
			}
		}
		top = rect.Bottom - Height - 24.0;
		goto IL_0231;
	}

	public void UpdateState()
	{
		label.Text = (app.AllPanelsOpen ? "▥ 패널 닫기" : "▥ 패널 열기");
		surface.Background = Theme.Brush(app.AllPanelsOpen ? Theme.Accent : Theme.Panel, 245);
		if (IsVisible)
		{
			Native.SetTopmost(new WindowInteropHelper(this).Handle);
		}
	}

	public void KeepOnScreen()
	{
		double num = Native.DipScale();
		Rect rect = Native.ToDipRect(Native.MonitorAt(new Native.POINT
		{
			X = (int)((Left + Width / 2.0) * num),
			Y = (int)((Top + Height / 2.0) * num)
		}).Bounds);
		Left = Math.Clamp(Left, rect.Left, Math.Max(rect.Left, rect.Right - Width));
		Top = Math.Clamp(Top, rect.Top, Math.Max(rect.Top, rect.Bottom - Height));
		EnsureTopmost();
	}

	public void EnsureTopmost()
	{
		if (IsVisible)
		{
			Native.SetTopmost(new WindowInteropHelper(this).Handle);
		}
	}
}
