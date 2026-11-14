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
		if (!(data.GetData("DesktopPop.ItemKey") is string text))
		{
			return null;
		}
		return text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
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
		TextBlock element = new TextBlock
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
		StackPanel stackPanel = new StackPanel
		{
			RenderTransformOrigin = new Point(0.5, 0.5),
			RenderTransform = scale
		};
		stackPanel.Children.Add(image);
		stackPanel.Children.Add(element);
		Brush brush = (backdrop ? Theme.Brush(Theme.Backdrop, 128) : IdleBg);
		Border border = new Border
		{
			Width = 80.0,
			Height = 92.0,
			CornerRadius = new CornerRadius(12.0),
			Background = brush,
			BorderThickness = new Thickness(1.0),
			BorderBrush = Brushes.Transparent,
			Child = stackPanel,
			Cursor = Cursors.Hand,
			ToolTip = item.ToolTipText,
			Tag = item,
			CacheMode = new BitmapCache()
		};
		border.Resources["idle"] = brush;
		border.MouseEnter += (object s, MouseEventArgs e) =>
		{
			bool flag = IsSelected(border);
			border.Background = (flag ? Theme.Brush(Theme.Accent, 102) : HoverBg);
			border.BorderBrush = (flag ? Theme.Brush(Theme.Accent) : HoverBorder);
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
		object obj = tile.Resources["sel"];
		if (obj is bool)
		{
			return (bool)obj;
		}
		return false;
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
		bool flag = IsSelected(tile);
		tile.Background = (flag ? Theme.Brush(Theme.Accent, 69) : (tile.Resources["idle"] as Brush));
		tile.BorderBrush = (flag ? Theme.Brush(Theme.Accent, 204) : Brushes.Transparent);
	}

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
			if (down.HasValue)
			{
				Point valueOrDefault = down.GetValueOrDefault();
				if (e.LeftButton != MouseButtonState.Pressed)
				{
					down = null;
				}
				else
				{
					Point position = e.GetPosition(tile);
					if (!(Math.Abs(position.X - valueOrDefault.X) + Math.Abs(position.Y - valueOrDefault.Y) < 8.0))
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
			List<string> list = new List<string> { item.Key };
			list.AddRange(from k in Sel.Ordered(host)
				where !string.Equals(k, item.Key, StringComparison.OrdinalIgnoreCase)
				select k);
			List<MenuEntry> list2 = extras();
			Window window = Window.GetWindow(tile);
			if (window == null || !ShellMenu.Show(window, list, list2, item.IsVirtual ? null : ((Action)(() =>
			{
				App.Instance.RenameItem(item);
			}))))
			{
				ShowFallbackMenu(tile, item, list2);
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
		foreach (MenuEntry item2 in more)
		{
			contextMenu.Items.Add(ToWpf(item2));
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
		List<string> list;
		if (Sel.Contains(host, item.Key))
		{
			list = Sel.Ordered(host);
		}
		else
		{
			Sel.Clear();
			list = new List<string> { item.Key };
		}
		App.DragSource = host;
		DragGhost ghost = new DragGhost(item, list.Count);
		ghost.Show();
		ghost.Follow();
		DispatcherTimer dispatcherTimer = new DispatcherTimer(DispatcherPriority.Render)
		{
			Interval = TimeSpan.FromMilliseconds(10L)
		};
		dispatcherTimer.Tick += (object? s2, EventArgs e2) =>
		{
			ghost.Follow();
		};
		dispatcherTimer.Start();
		GiveFeedbackEventHandler value = (object s2, GiveFeedbackEventArgs e2) =>
		{
			ghost.Follow();
			if (Native.GetCursorPos(out var p2))
			{
				bool flag = Native.IsDesktopAt(p2);
				bool flag2 = isOutside != null && isOutside(p2) && ((e2.Effects == DragDropEffects.None) | flag);
				ghost.SetValid((e2.Effects != DragDropEffects.None && !flag) | flag2);
				if (flag2 | flag)
				{
					e2.UseDefaultCursors = false;
					Mouse.SetCursor(flag2 ? Cursors.Arrow : Cursors.No);
					e2.Handled = true;
				}
			}
		};
		bool cancelled = false;
		bool droppedOnDesktop = false;
		QueryContinueDragEventHandler value2 = (object s2, QueryContinueDragEventArgs e2) =>
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
		tile.GiveFeedback += value;
		tile.QueryContinueDrag += value2;
		foreach (string item2 in list)
		{
			host.TileOf(item2)?.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.3, 120.0));
		}
		DataObject dataObject = new DataObject("DesktopPop.ItemKey", string.Join("\n", list));
		string[] array = list.Where((string k) => !k.StartsWith("::")).ToArray();
		if (array.Length != 0)
		{
			dataObject.SetData(DataFormats.FileDrop, array);
		}
		DragDropEffects allowedEffects = ((array.Length != 0) ? (DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link) : DragDropEffects.Move);
		DragDropEffects dragDropEffects = DragDropEffects.None;
		try
		{
			dragDropEffects = DragDrop.DoDragDrop(tile, dataObject, allowedEffects);
		}
		finally
		{
			dispatcherTimer.Stop();
			tile.GiveFeedback -= value;
			tile.QueryContinueDrag -= value2;
			ghost.Close();
			foreach (string item3 in list)
			{
				host.TileOf(item3)?.BeginAnimation(UIElement.OpacityProperty, Anim.To(1.0, 150.0));
			}
			App.DragSource = null;
		}
		if ((dragDropEffects != DragDropEffects.None) | droppedOnDesktop)
		{
			Sel.Clear(host);
		}
		if (!cancelled && (droppedOnDesktop || dragDropEffects == DragDropEffects.None) && dropOutside != null && isOutside != null && Native.GetCursorPos(out var p) && isOutside(p))
		{
			dropOutside(list, p);
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
