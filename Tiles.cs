using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DesktopPop;

/// <summary>아이콘 칸 만들기와 마우스 동작(선택·열기·끌기·우클릭).</summary>
internal static class Tiles
{
	public const string DragFormat = "DesktopPop.ItemKey";

	public const double W = 86.0;

	public const double H = 98.0;

	private static readonly Brush HoverBg = Frozen(new SolidColorBrush(Color.FromArgb(72, byte.MaxValue, byte.MaxValue, byte.MaxValue)));

	private static readonly Brush HoverBorder = Frozen(new SolidColorBrush(Color.FromArgb(112, byte.MaxValue, byte.MaxValue, byte.MaxValue)));

	private static readonly Brush IdleBg = Frozen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

	private static Brush Frozen(Brush b)
	{
		b.Freeze();
		return b;
	}

	public static string[]? KeysFrom(IDataObject data)
	{
		if (!(data.GetData("DesktopPop.ItemKey") is string joined))
		{
			return null;
		}
		return joined.Split('\n', StringSplitOptions.RemoveEmptyEntries);
	}

	public static Border Create(DesktopItem item, bool backdrop)
	{
		Image image = new Image
		{
			Source = item.Icon,
			Width = 48.0,
			Height = 48.0,
			Margin = new Thickness(0.0, 7.0, 0.0, 0.0),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
		TextBlock caption = new TextBlock
		{
			Text = item.DisplayLabel,
			Foreground = Brushes.White,
			FontSize = 12.0,
			TextWrapping = TextWrapping.Wrap,
			TextTrimming = TextTrimming.CharacterEllipsis,
			TextAlignment = TextAlignment.Center,
			MaxHeight = 33.0,
			Margin = new Thickness(4.0, 4.0, 4.0, 0.0),
			Effect = new DropShadowEffect
			{
				ShadowDepth = 1.0,
				BlurRadius = 4.0,
				Opacity = 0.95,
				Color = Colors.Black
			}
		};
		ScaleTransform scale = new ScaleTransform(1.0, 1.0);
		StackPanel content = new StackPanel
		{
			RenderTransformOrigin = new Point(0.5, 0.5),
			RenderTransform = scale
		};
		content.Children.Add(image);
		content.Children.Add(caption);
		// 가방 둘레에 펼친 아이콘은 뒤가 비치지 않게 반투명 바탕, 가장자리 판 아이콘은 거의 투명(그래도 클릭은 되게)
		Brush idleBackground = (backdrop ? Theme.Brush(Theme.Backdrop, 128) : IdleBg);
		Border border = new Border
		{
			Width = 80.0,
			Height = 92.0,
			CornerRadius = new CornerRadius(12.0),
			Background = idleBackground,
			BorderThickness = new Thickness(1.0),
			BorderBrush = Brushes.Transparent,
			Child = content,
			Cursor = Cursors.Hand,
			ToolTip = item.ToolTipText,
			Tag = item,
			CacheMode = new BitmapCache()
		};
		border.Resources["idle"] = idleBackground;
		border.MouseEnter += (object s, MouseEventArgs e) =>
		{
			bool selected = IsSelected(border);
			border.Background = (selected ? Theme.Brush(Theme.Accent, 102) : HoverBg);
			border.BorderBrush = (selected ? Theme.Brush(Theme.Accent) : HoverBorder);
			scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.08, 120.0));
			scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.08, 120.0));
		};
		border.MouseLeave += (object s, MouseEventArgs e) =>
		{
			ApplyRest(border);
			scale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.0, 160.0));
			scale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.0, 160.0));
		};
		return border;
	}

	private static bool IsSelected(Border tile)
	{
		return tile.Resources["sel"] is bool selected && selected;
	}

	public static void SetSelected(Border tile, bool selected)
	{
		if (IsSelected(tile) != selected)
		{
			tile.Resources["sel"] = selected;
			if (!tile.IsMouseOver)
			{
				ApplyRest(tile);
			}
		}
	}

	private static void ApplyRest(Border tile)
	{
		bool selected = IsSelected(tile);
		tile.Background = (selected ? Theme.Brush(Theme.Accent, 69) : (tile.Resources["idle"] as Brush));
		tile.BorderBrush = (selected ? Theme.Brush(Theme.Accent, 204) : Brushes.Transparent);
	}

	/// <summary>
	/// 아이콘 칸에 원래 바탕화면 같은 마우스 동작을 붙임: 클릭 = 선택(Ctrl·Shift 로 여러 개), 두 번 클릭 = 열기,
	/// 끌기 = 옮기기, 우클릭 = 메뉴. isOutside/dropOutside 는 가방에서 원 밖으로 끌어내 꺼낼 때 씀.
	/// </summary>
	public static void Attach(Border tile, DesktopItem item, ISelHost host, Action afterLaunch, Func<List<MenuEntry>> extras, Func<Native.POINT, bool>? isOutside = null, Action<List<string>, Native.POINT>? dropOutside = null)
	{
		Point? down = null;
		ModifierKeys mods = ModifierKeys.None;
		tile.MouseLeftButtonDown += (object s, MouseButtonEventArgs e) =>
		{
			e.Handled = true;
			if (e.ClickCount >= 2 && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
			{
				down = null;
				Sel.Clear();
				Shell.Launch(item);
				afterLaunch();
			}
			else
			{
				down = e.GetPosition(tile);
				mods = Keyboard.Modifiers;
			}
		};
		tile.MouseMove += (object s, MouseEventArgs e) =>
		{
			if (down is Point pressedAt)
			{
				if (e.LeftButton != MouseButtonState.Pressed)
				{
					down = null;
				}
				else
				{
					Point position = e.GetPosition(tile);
					if (!(Math.Abs(position.X - pressedAt.X) + Math.Abs(position.Y - pressedAt.Y) < 8.0)) // 8px 넘게 움직이면 끌기 시작
					{
						down = null;
						StartDrag(tile, item, host, isOutside, dropOutside);
					}
				}
			}
		};
		tile.MouseLeftButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			if (down.HasValue)
			{
				down = null;
				e.Handled = true;
				if ((mods & ModifierKeys.Control) != 0)
				{
					Sel.Toggle(host, item.Key);
				}
				else if ((mods & ModifierKeys.Shift) != 0)
				{
					Sel.Range(host, item.Key);
				}
				else if (App.Instance.Config.SingleClickLaunch)
				{
					Sel.Clear();
					Sel.Anchor = item.Key;
					Shell.Launch(item);
					afterLaunch();
				}
				else
				{
					Sel.Set(host, new string[1] { item.Key });
					Sel.Anchor = item.Key;
				}
			}
		};
		tile.MouseRightButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			e.Handled = true;
			if (!Sel.Contains(host, item.Key))
			{
				Sel.Set(host, new string[1] { item.Key });
				Sel.Anchor = item.Key;
			}
			// 우클릭한 것이 맨 앞, 나머지 선택된 것들이 뒤 (Windows 메뉴는 첫 항목 기준)
			List<string> targets = new List<string> { item.Key };
			targets.AddRange(from k in Sel.Ordered(host)
				where !string.Equals(k, item.Key, StringComparison.OrdinalIgnoreCase)
				select k);
			List<MenuEntry> ownEntries = extras();
			Window window = Window.GetWindow(tile);
			if (window == null || !ShellMenu.Show(window, targets, ownEntries, item.IsVirtual ? null : ((Action)(() =>
			{
				App.Instance.RenameItem(item);
			}))))
			{
				ShowFallbackMenu(tile, item, ownEntries);
			}
		};
	}

	private static void ShowFallbackMenu(Border tile, DesktopItem item, List<MenuEntry> more)
	{
		ContextMenu contextMenu = new ContextMenu();
		contextMenu.Items.Add(Item("열기", () =>
		{
			Shell.Launch(item);
		}));
		if (!item.IsVirtual)
		{
			contextMenu.Items.Add(Item("파일 위치 열기", () =>
			{
				Shell.OpenLocation(item);
			}));
		}
		if (more.Count > 0)
		{
			contextMenu.Items.Add(new Separator());
		}
		foreach (MenuEntry entry in more)
		{
			contextMenu.Items.Add(ToWpf(entry));
		}
		App.Track(contextMenu);
		contextMenu.PlacementTarget = tile;
		contextMenu.IsOpen = true;
	}

	private static object ToWpf(MenuEntry m)
	{
		if (m.IsSeparator)
		{
			return new Separator();
		}
		MenuItem menuItem = new MenuItem
		{
			Header = m.Text
		};
		if (m.Children != null)
		{
			foreach (MenuEntry child in m.Children)
			{
				menuItem.Items.Add(ToWpf(child));
			}
		}
		else if (m.Run != null)
		{
			menuItem.Click += (object s, RoutedEventArgs e) =>
			{
				m.Run();
			};
		}
		return menuItem;
	}

	private static void StartDrag(Border tile, DesktopItem item, ISelHost host, Func<Native.POINT, bool>? isOutside, Action<List<string>, Native.POINT>? dropOutside)
	{
		// 선택된 걸 끌면 선택된 것 전부, 아니면 이것 하나
		List<string> dragged;
		if (Sel.Contains(host, item.Key))
		{
			dragged = Sel.Ordered(host);
		}
		else
		{
			Sel.Clear();
			dragged = new List<string> { item.Key };
		}
		App.DragSource = host;
		DragGhost ghost = new DragGhost(item, dragged.Count);
		ghost.Show();
		ghost.Follow();
		DispatcherTimer followTimer = new DispatcherTimer(DispatcherPriority.Render)
		{
			Interval = TimeSpan.FromMilliseconds(10L)
		};
		followTimer.Tick += (object? s2, EventArgs e2) =>
		{
			ghost.Follow();
		};
		followTimer.Start();
		// 끄는 동안: 놓을 수 있는 곳인지 끌림 그림에 표시. 빈 바탕화면 위는 놓을 수 없음(금지 커서),
		// 가방 원 밖은 "꺼내기"(화살표 커서)
		GiveFeedbackEventHandler onFeedback = (object s2, GiveFeedbackEventArgs e2) =>
		{
			ghost.Follow();
			if (Native.GetCursorPos(out var p2))
			{
				bool overDesktop = Native.IsDesktopAt(p2);
				bool takeOut = isOutside != null && isOutside(p2) && ((e2.Effects == DragDropEffects.None) | overDesktop);
				ghost.SetValid((e2.Effects != DragDropEffects.None && !overDesktop) | takeOut);
				if (takeOut | overDesktop)
				{
					e2.UseDefaultCursors = false;
					Mouse.SetCursor(takeOut ? Cursors.Arrow : Cursors.No);
					e2.Handled = true;
				}
			}
		};
		bool cancelled = false;
		bool droppedOnDesktop = false;
		QueryContinueDragEventHandler onQueryContinue = (object s2, QueryContinueDragEventArgs e2) =>
		{
			Native.POINT p2;
			if (e2.EscapePressed)
			{
				cancelled = true;
			}
			else if ((e2.KeyStates & DragDropKeyStates.LeftMouseButton) == 0 && Native.GetCursorPos(out p2) && Native.IsDesktopAt(p2))
			{
				droppedOnDesktop = true;
				e2.Action = DragAction.Cancel;
				e2.Handled = true;
			}
		};
		tile.GiveFeedback += onFeedback;
		tile.QueryContinueDrag += onQueryContinue;
		foreach (string key in dragged)
		{
			host.TileOf(key)?.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.3, 120.0));
		}
		// DesktopPop 안에서는 아이콘 키로, 탐색기 등 다른 프로그램에는 실제 파일로 끌어 놓을 수 있게
		DataObject dataObject = new DataObject("DesktopPop.ItemKey", string.Join("\n", dragged));
		string[] files = dragged.Where((string k) => !k.StartsWith("::")).ToArray();
		if (files.Length != 0)
		{
			dataObject.SetData(DataFormats.FileDrop, files);
		}
		DragDropEffects allowedEffects = ((files.Length != 0) ? (DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link) : DragDropEffects.Move);
		DragDropEffects result = DragDropEffects.None;
		try
		{
			result = DragDrop.DoDragDrop(tile, dataObject, allowedEffects);
		}
		finally
		{
			followTimer.Stop();
			tile.GiveFeedback -= onFeedback;
			tile.QueryContinueDrag -= onQueryContinue;
			ghost.Close();
			foreach (string key in dragged)
			{
				host.TileOf(key)?.BeginAnimation(UIElement.OpacityProperty, Anim.To(1.0, 150.0));
			}
			App.DragSource = null;
		}
		if ((result != DragDropEffects.None) | droppedOnDesktop)
		{
			Sel.Clear(host);
		}
		// 가방에서 원 밖(아무도 안 받는 곳)에 놓았으면 꺼내기
		if (!cancelled && (droppedOnDesktop || result == DragDropEffects.None) && dropOutside != null && isOutside != null && Native.GetCursorPos(out var p) && isOutside(p))
		{
			dropOutside(dragged, p);
		}
	}

	public static MenuItem Item(string header, Action a)
	{
		MenuItem menuItem = new MenuItem();
		menuItem.Header = header;
		menuItem.Click += (object s, RoutedEventArgs e) =>
		{
			a();
		};
		return menuItem;
	}
}
