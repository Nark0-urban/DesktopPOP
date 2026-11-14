using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DesktopPop;

internal sealed class EdgePanel : Window, ISelHost
{
	private const double PadX = 6.0;

	private const double PadY = 8.0;

	private const double Fade = 40.0;

	private readonly RubberBand band;

	public readonly Side Side;

	private readonly Canvas canvas = new Canvas();

	private readonly Border bg = new Border();

	private readonly List<(Border tile, TranslateTransform tt, int col, int row, double off)> tiles = new List<(Border, TranslateTransform, int, int, double)>();

	private readonly Dictionary<string, Border> tileByKey = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);

	private TilePresentation[] presentations = Array.Empty<TilePresentation>();

	private string[] orderedKeys = Array.Empty<string>();

	private int rows = 1;

	private bool shown;

	private bool dirty = true;

	private DateTime edgeSince = DateTime.MinValue;

	private DateTime awaySince = DateTime.MinValue;

	private nint hwnd;

	private int hideToken;

	private bool closed;

	private DateTime peekUntil = DateTime.MinValue;

	private Border? hoverTile;

	private Brush? hoverOldBg;

	private Brush? hoverOldBorder;

	private readonly Dictionary<string, int> flying = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, DateTime> highlights = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

	private readonly List<UIElement> highlightRings = new List<UIElement>();

	private bool highlightRedrawQueued;

	private bool redrawScheduled;

	public Native.MonitorInfo Monitor { get; private set; }

	public Native.RECT Bounds => Monitor.Bounds;

	public PanelConfig Placement => App.Instance.Config.GetPanel(Monitor.DeviceName, Side);

	public bool IsForcedOpen { get; private set; }

	public bool IsShown => shown;

	public IReadOnlyList<string> OrderedKeys => orderedKeys;

	public bool BandSelectable
	{
		get
		{
			if (shown)
			{
				return IsVisible;
			}
			return false;
		}
	}

	public EdgePanel(Side side)
		: this(side, Native.Monitors()[0])
	{
	}

	public EdgePanel(Side side, Native.MonitorInfo monitor)
	{
		Side = side;
		Monitor = monitor;
		Title = $"DesktopPop Edge {monitor.DeviceName} {side}";
		WindowStyle = WindowStyle.None;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		ShowInTaskbar = false;
		Topmost = true;
		ShowActivated = false;
		ResizeMode = ResizeMode.NoResize;
		ApplyThemeBackground();
		bg.Opacity = 0.0;
		Grid root = new Grid
		{
			AllowDrop = true
		};
		root.Children.Add(bg);
		root.Children.Add(canvas);
		DragEventHandler value = (object s, DragEventArgs e) =>
		{
			string[] array = Tiles.KeysFrom(e.Data);
			string[] array2 = e.Data.GetData(DataFormats.FileDrop) as string[];
			(Border, DesktopItem)? tuple = DropTargetAt(e.GetPosition(canvas), array, array2);
			SetDropHover(tuple?.Item1);
			string[] array3 = ((array != null) ? array.Where((string k) => !k.StartsWith("::")).ToArray() : array2);
			if (tuple.HasValue)
			{
				(Border, DesktopItem) valueOrDefault = tuple.GetValueOrDefault();
				if (array3 != null && array3.Length > 0)
				{
					e.Effects = ((!(valueOrDefault.Item2.Key == "::{645FF040-5081-101B-9F08-00AA002F954E}") && WantsCopy(e, array3, valueOrDefault.Item2.Path)) ? DragDropEffects.Copy : DragDropEffects.Move);
					goto IL_0110;
				}
			}
			if (array != null)
			{
				e.Effects = DragDropEffects.Move;
			}
			else if (array2 != null && array2.Length > 0)
			{
				e.Effects = (WantsCopy(e, array2, App.DesktopDir) ? DragDropEffects.Copy : DragDropEffects.Move);
			}
			else
			{
				e.Effects = DragDropEffects.None;
			}
			goto IL_0110;
			IL_0110:
			e.Effects &= e.AllowedEffects;
			e.Handled = true;
		};
		root.DragEnter += value;
		root.DragOver += value;
		root.DragLeave += (object s, DragEventArgs e) =>
		{
			SetDropHover(null);
		};
		root.Drop += OnDrop;
		root.MouseRightButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			ContextMenu contextMenu = BackgroundMenu(IndexAt(e.GetPosition(canvas)));
			App.Track(contextMenu);
			contextMenu.PlacementTarget = root;
			contextMenu.IsOpen = true;
			e.Handled = true;
		};
		Content = root;
		band = new RubberBand(root, canvas, this)
		{
			OnClick = () =>
			{
				Sel.Clear(this);
			}
		};
		SourceInitialized += (object? s, EventArgs e) =>
		{
			hwnd = new WindowInteropHelper(this).Handle;
			Native.MakeToolWindow(hwnd);
		};
		Closed += (object? s, EventArgs e) =>
		{
			closed = true;
			shown = false;
			hideToken++;
			Sel.Clear(this);
		};
	}

	public void UpdateMonitor(Native.MonitorInfo monitor)
	{
		Monitor = monitor;
		edgeSince = (awaySince = DateTime.MinValue);
		MarkDirty();
	}

	public void ForceOpen(bool value)
	{
		if (closed)
		{
			return;
		}
		IsForcedOpen = value;
		edgeSince = (awaySince = (peekUntil = DateTime.MinValue));
		if (value)
		{
			if (!shown)
			{
				ShowPanel();
			}
		}
		else
		{
			HidePanel();
		}
	}

	public void DisposePanel()
	{
		if (!closed)
		{
			IsForcedOpen = false;
			closed = true;
			shown = false;
			hideToken++;
			Sel.Clear(this);
			Close();
		}
	}

	public Border? TileOf(string key)
	{
		if (!tileByKey.TryGetValue(key, out Border value))
		{
			return null;
		}
		return value;
	}

	internal void RefreshItems()
	{
		if (!closed && (dirty || !TilePresentation.Matches(presentations, App.Instance.Store.VisibleFor(Placement))))
		{
			MarkDirty();
		}
	}

	public void ApplySelection()
	{
		foreach (var tile in tiles)
		{
			Tiles.SetSelected(tile.tile, Sel.Contains(this, ((DesktopItem)tile.tile.Tag).Key));
		}
	}

	private void ApplyThemeBackground()
	{
		bool flag = Side == Side.Left;
		Color panel = Theme.Panel;
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush
		{
			StartPoint = new Point((!flag) ? 1 : 0, 0.5),
			EndPoint = new Point(flag ? 1 : 0, 0.5)
		};
		linearGradientBrush.GradientStops.Add(new GradientStop(Theme.WithAlpha(panel, 192), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Theme.WithAlpha(panel, 136), 0.72));
		linearGradientBrush.GradientStops.Add(new GradientStop(Theme.WithAlpha(panel, 2), 1.0));
		linearGradientBrush.Freeze();
		bg.Background = linearGradientBrush;
	}

	public void ApplyTheme()
	{
		ApplyThemeBackground();
		MarkDirty();
	}

	public void MarkDirty()
	{
		if (closed)
		{
			return;
		}
		dirty = true;
		PrepareOpen();
		if (!shown)
		{
			return;
		}
		foreach (var tile in tiles)
		{
			tile.tt.X = 0.0;
			tile.tile.Opacity = 1.0;
			if (flying.ContainsKey(((DesktopItem)tile.tile.Tag).Key))
			{
				tile.tile.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 1.0));
			}
		}
		QueueHighlightRedraw();
	}

	internal void PrepareOpen()
	{
		if (!closed && dirty)
		{
			Layout();
		}
	}

	private void Layout()
	{
		dirty = false;
		App instance = App.Instance;
		Rect rect = Native.ToDipRect(Monitor.WorkArea);
		(double top, double height) tuple = VerticalRange();
		double item = tuple.top;
		double item2 = tuple.height;
		List<DesktopItem> list = instance.Store.VisibleFor(Placement);
		rows = Math.Max(1, (int)((item2 - 16.0) / 98.0));
		int num = Math.Max(1, (list.Count + rows - 1) / rows);
		Width = ((list.Count == 0) ? 188.0 : ((double)num * 86.0 + 12.0 + 40.0));
		Height = item2;
		Top = item;
		Left = ((Side == Side.Left) ? rect.Left : (rect.Right - Width));
		canvas.Children.Clear();
		tiles.Clear();
		tileByKey.Clear();
		presentations = list.Select(TilePresentation.Capture).ToArray();
		orderedKeys = list.Select((DesktopItem desktopItem) => desktopItem.Key).ToArray();
		if (list.Count == 0)
		{
			TextBlock element = new TextBlock
			{
				Text = instance.PanelLabel(this) + "\n\n아이콘을 여기로\n끌어 놓으세요",
				Width = Width - 12.0 - 12.0,
				FontSize = 12.0,
				Foreground = Brushes.White,
				Opacity = 0.8,
				TextAlignment = TextAlignment.Center,
				TextWrapping = TextWrapping.Wrap,
				IsHitTestVisible = false
			};
			Canvas.SetLeft(element, 12.0);
			Canvas.SetTop(element, 28.0);
			canvas.Children.Add(element);
		}
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			DesktopItem item3 = list[num2];
			int num3 = num2 / rows;
			int num4 = num2 % rows;
			double num5 = ((Side == Side.Left) ? (6.0 + (double)num3 * 86.0) : (Width - 6.0 - (double)(num3 + 1) * 86.0));
			double num6 = 8.0 + (double)num4 * 98.0;
			Border border = Tiles.Create(item3, backdrop: false);
			Tiles.Attach(border, item3, this, HidePanel, () => BuildExtras(item3));
			Canvas.SetLeft(border, num5 + 3.0);
			Canvas.SetTop(border, num6 + 3.0);
			double num7 = ((Side == Side.Left) ? (0.0 - (num5 + 86.0 + 10.0)) : (Width - num5 + 10.0));
			TranslateTransform item4 = (TranslateTransform)(border.RenderTransform = new TranslateTransform(num7, 0.0));
			border.Opacity = 0.0;
			canvas.Children.Add(border);
			tiles.Add((border, item4, num3, num4, num7));
			tileByKey.TryAdd(item3.Key, border);
			border.Measure(new Size(border.Width, border.Height));
			border.Arrange(new Rect(0.0, 0.0, border.Width, border.Height));
		}
		ApplySelection();
	}

	private (double top, double height) VerticalRange()
	{
		Rect rect = Native.ToDipRect(Monitor.WorkArea);
		double num = rect.Top;
		double num2 = rect.Bottom;
		if (Native.TryGetTaskbarRect(Monitor, out var r))
		{
			Native.RECT bounds = Monitor.Bounds;
			double num3 = Native.DipScale();
			if (r.Right - r.Left > r.Bottom - r.Top && num3 > 0.0)
			{
				bool flag = Math.Min(r.Bottom, bounds.Bottom) - Math.Max(r.Top, bounds.Top) < 10;
				if (r.Top > (bounds.Top + bounds.Bottom) / 2 || (flag && r.Top >= bounds.Bottom - 10))
				{
					num2 = Math.Min(num2, flag ? ((double)bounds.Bottom / num3 - 52.0) : ((double)r.Top / num3));
				}
				else
				{
					num = Math.Max(num, flag ? ((double)bounds.Top / num3 + 52.0) : ((double)r.Bottom / num3));
				}
			}
		}
		return (top: num, height: Math.Max(114.0, num2 - num));
	}

	private void ShowPanel()
	{
		if (closed)
		{
			return;
		}
		if (!IsForcedOpen)
		{
			if (!Native.GetCursorPos(out var p))
			{
				return;
			}
			Native.RECT bounds = Monitor.Bounds;
			if (!Native.IsDesktopExposed(new Native.POINT
			{
				X = ((Side == Side.Left) ? bounds.Left : (bounds.Right - 1)),
				Y = Math.Clamp(p.Y, bounds.Top, bounds.Bottom - 1)
			}))
			{
				return;
			}
		}
		if (dirty)
		{
			Layout();
		}
		shown = true;
		hideToken++;
		if (!IsVisible)
		{
			Show();
		}
		Native.SetTopmost(hwnd);
		bg.BeginAnimation(UIElement.OpacityProperty, Anim.To(1.0, 200.0));
		foreach (var tile in tiles)
		{
			double delayMs = tile.row * 14 + tile.col * 55;
			tile.tt.BeginAnimation(TranslateTransform.XProperty, Anim.To(0.0, 430.0, delayMs, new BackEase
			{
				Amplitude = 0.55,
				EasingMode = EasingMode.EaseOut
			}));
			bool flag = flying.ContainsKey(((DesktopItem)tile.tile.Tag).Key);
			tile.tile.BeginAnimation(UIElement.OpacityProperty, flag ? Anim.To(0.0, 1.0) : Anim.To(1.0, 220.0, delayMs));
		}
	}

	public void Peek()
	{
		if (!closed)
		{
			peekUntil = DateTime.UtcNow.AddMilliseconds(1600.0);
			if (!shown)
			{
				ShowPanel();
			}
		}
	}

	public void HidePanel()
	{
		if (closed || IsForcedOpen || !shown)
		{
			return;
		}
		shown = false;
		Sel.Clear(this);
		int token = ++hideToken;
		int num = 0;
		foreach (var tile in tiles)
		{
			num = Math.Max(num, tile.row);
			double delayMs = tile.row * 6;
			tile.tt.BeginAnimation(TranslateTransform.XProperty, Anim.To(tile.off, 220.0, delayMs, new QuadraticEase
			{
				EasingMode = EasingMode.EaseIn
			}));
			tile.tile.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 220.0, delayMs));
		}
		bg.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 260.0));
		Anim.After(280 + num * 6, () =>
		{
			if (!closed && token == hideToken && !shown)
			{
				Hide();
				foreach (var tile2 in tiles)
				{
					tile2.tt.BeginAnimation(TranslateTransform.XProperty, null);
					tile2.tile.BeginAnimation(UIElement.OpacityProperty, null);
					tile2.tt.X = tile2.off;
					tile2.tile.Opacity = 0.0;
				}
				bg.BeginAnimation(UIElement.OpacityProperty, null);
				bg.Opacity = 0.0;
			}
		});
	}

	public void Tick(Native.POINT p, Native.RECT mon, bool lbtn, DateTime now)
	{
		Tick(p, lbtn, now);
	}

	public void Tick(Native.POINT p, bool lbtn, DateTime now)
	{
		if (closed)
		{
			return;
		}
		if (IsForcedOpen)
		{
			if (!shown)
			{
				ShowPanel();
			}
			return;
		}
		App instance = App.Instance;
		Native.RECT bounds = Monitor.Bounds;
		bool flag = Native.TryGetEdgeProbe(p, bounds, Side, instance.EdgePanels.Select((EdgePanel panel) => panel.Bounds), out var probe);
		if (!shown)
		{
			if (!flag || !Native.IsDesktopExposed(probe))
			{
				edgeSince = DateTime.MinValue;
				return;
			}
			bool flag2 = (flag & lbtn) && App.DragSource == null && Native.LooksLikeDragCursor();
			bool flag3 = ((App.DragSource != null) | flag2) || DesktopBand.IsActive;
			if (!flag || !(!lbtn | flag3) || App.OpenMenus != 0)
			{
				edgeSince = DateTime.MinValue;
				return;
			}
			if (edgeSince == DateTime.MinValue)
			{
				edgeSince = now;
			}
			double num = (flag2 ? Math.Max(250, instance.Config.EdgeDelayMs) : instance.Config.EdgeDelayMs);
			if ((now - edgeSince).TotalMilliseconds >= num)
			{
				edgeSince = DateTime.MinValue;
				awaySince = DateTime.MinValue;
				ShowPanel();
			}
		}
		else
		{
			if (hwnd == IntPtr.Zero)
			{
				return;
			}
			if (App.OpenMenus > 0 || App.DragSource != null || band.IsActive || (lbtn && Native.LooksLikeDragCursor()) || KeyboardHook.IsSuspended)
			{
				awaySince = DateTime.MinValue;
				return;
			}
			if (!flag)
			{
				probe = new Native.POINT
				{
					X = ((Side == Side.Left) ? bounds.Left : (bounds.Right - 1)),
					Y = Math.Clamp(p.Y, bounds.Top, bounds.Bottom - 1)
				};
			}
			if (!Native.IsDesktopExposed(probe))
			{
				awaySince = DateTime.MinValue;
				HidePanel();
				return;
			}
			Native.GetWindowRect(hwnd, out var r);
			if (flag || (Native.Contains(bounds, p) && p.X >= r.Left - 24 && p.X <= r.Right + 24 && p.Y >= r.Top - 24 && p.Y <= r.Bottom + 24) || now < peekUntil)
			{
				awaySince = DateTime.MinValue;
				return;
			}
			if (awaySince == DateTime.MinValue)
			{
				awaySince = now;
			}
			if ((now - awaySince).TotalMilliseconds >= 350.0)
			{
				awaySince = DateTime.MinValue;
				HidePanel();
			}
		}
	}

	private int IndexAt(Point pos)
	{
		double d = ((Side == Side.Left) ? ((pos.X - 6.0) / 86.0) : ((Width - 6.0 - pos.X) / 86.0));
		int num = Math.Max(0, (int)Math.Floor(d));
		int num2 = Math.Clamp((int)Math.Floor((pos.Y - 8.0) / 98.0), 0, rows - 1);
		return num * rows + num2;
	}

	private static bool WantsCopy(DragEventArgs e, string[] files, string destDir)
	{
		if ((e.KeyStates & DragDropKeyStates.ControlKey) != 0)
		{
			return true;
		}
		if ((e.KeyStates & DragDropKeyStates.ShiftKey) != 0)
		{
			return false;
		}
		string? a = Path.GetPathRoot(files[0]) ?? "";
		string b = Path.GetPathRoot(destDir) ?? "";
		return !string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	}

	private (Border tile, DesktopItem item)? DropTargetAt(Point pos, string[]? draggedKeys, string[]? files)
	{
		int num = IndexAt(pos);
		if (num < 0 || num >= tiles.Count)
		{
			return null;
		}
		Border item = tiles[num].tile;
		object tag = item.Tag;
		DesktopItem item2 = tag as DesktopItem;
		if (item2 == null)
		{
			return null;
		}
		if (draggedKeys != null && draggedKeys.Any((string k) => string.Equals(k, item2.Key, StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}
		bool flag = item2.Key == "::{645FF040-5081-101B-9F08-00AA002F954E}";
		bool flag2 = !item2.IsVirtual && Directory.Exists(item2.Path);
		if (!flag && !flag2)
		{
			return null;
		}
		string[] array = (draggedKeys ?? files ?? Array.Empty<string>()).Where((string k) => !k.StartsWith("::")).ToArray();
		if (array.Length == 0)
		{
			return null;
		}
		if (flag2 && array.Any((string s) => string.Equals(s.TrimEnd('\\'), item2.Path, StringComparison.OrdinalIgnoreCase) || item2.Path.StartsWith(s.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}
		double left = Canvas.GetLeft(item);
		double top = Canvas.GetTop(item);
		if (pos.X < left + 14.0 || pos.X > left + item.Width - 14.0 || pos.Y < top + 4.0 || pos.Y > top + item.Height - 14.0)
		{
			return null;
		}
		return (item, item2);
	}

	private void SetDropHover(Border? tile)
	{
		if (tile != hoverTile)
		{
			if (hoverTile != null)
			{
				hoverTile.Background = hoverOldBg;
				hoverTile.BorderBrush = hoverOldBorder;
			}
			hoverTile = tile;
			if (tile != null)
			{
				hoverOldBg = tile.Background;
				hoverOldBorder = tile.BorderBrush;
				tile.Background = Theme.Brush(Theme.Accent, 85);
				tile.BorderBrush = Theme.Brush(Theme.Accent);
			}
		}
	}

	private void OnDrop(object sender, DragEventArgs e)
	{
		Point position = e.GetPosition(canvas);
		int index = IndexAt(position);
		string[] array = Tiles.KeysFrom(e.Data);
		string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
		(Border, DesktopItem)? tuple = DropTargetAt(position, array, files);
		SetDropHover(null);
		App app = App.Instance;
		PanelConfig destination = Placement;
		if (tuple.HasValue)
		{
			(Border, DesktopItem) valueOrDefault = tuple.GetValueOrDefault();
			DesktopItem folder = valueOrDefault.Item2;
			string[] srcs = (array ?? files).Where((string k) => !k.StartsWith("::")).ToArray();
			bool bin = folder.Key == "::{645FF040-5081-101B-9F08-00AA002F954E}";
			bool copy = !bin && WantsCopy(e, srcs, folder.Path);
			Dispatcher.InvokeAsync(() =>
			{
				if (bin)
				{
					app.RecycleFiles(srcs);
				}
				else
				{
					app.TransferFiles(srcs, folder.Path, copy);
				}
			});
			e.Effects = ((array != null) ? DragDropEffects.Move : (DragDropEffects.Copy & e.AllowedEffects));
		}
		else if (array != null)
		{
			app.MoveManyToPanel(array, destination, index);
			e.Effects = DragDropEffects.Move;
		}
		else if (files != null && files.Length > 0)
		{
			bool copy2 = WantsCopy(e, files, App.DesktopDir);
			Dispatcher.InvokeAsync(() =>
			{
				app.BringFilesToDesktop(files, copy2, destination, index);
			});
			e.Effects = DragDropEffects.Copy & e.AllowedEffects;
		}
		e.Handled = true;
	}

	public void RevealArrivals(List<(string key, bool fly)> items, Point from)
	{
		if (closed)
		{
			return;
		}
		bool flag = shown;
		peekUntil = DateTime.UtcNow.AddMilliseconds(3200.0);
		if (!shown)
		{
			ShowPanel();
		}
		if (!shown)
		{
			return;
		}
		int num = 0;
		foreach (var item2 in items)
		{
			string key = item2.key;
			bool item = item2.fly;
			Border border = TileOf(key);
			if (border == null)
			{
				continue;
			}
			if (item && num < 12)
			{
				double delayMs = num++ * 90;
				flying[key] = ((!flying.TryGetValue(key, out var value)) ? 1 : (value + 1));
				border.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 1.0));
				FlyOverlay.Fly(to: new Point(Left + Canvas.GetLeft(border) + border.Width / 2.0, Top + Canvas.GetTop(border) + border.Height / 2.0), item: (DesktopItem)border.Tag, from: from, delayMs: delayMs, onArrive: () =>
				{
					if (!closed)
					{
						if (flying.TryGetValue(key, out var value2) && --value2 > 0)
						{
							flying[key] = value2;
						}
						else
						{
							flying.Remove(key);
						}
						Border border2 = TileOf(key);
						if (border2 != null)
						{
							if (!flying.ContainsKey(key))
							{
								border2.BeginAnimation(UIElement.OpacityProperty, Anim.To(1.0, 80.0));
							}
							Highlight(key);
							peekUntil = DateTime.UtcNow.AddMilliseconds(1800.0);
						}
					}
				});
			}
			else
			{
				Anim.After(flag ? 60 : 700, () =>
				{
					Highlight(key);
				});
			}
		}
	}

	private void Highlight(string key)
	{
		if (!closed)
		{
			highlights[key] = DateTime.UtcNow.AddMilliseconds(2200.0);
			QueueHighlightRedraw();
		}
	}

	private void QueueHighlightRedraw()
	{
		if (!closed && !highlightRedrawQueued)
		{
			highlightRedrawQueued = true;
			Dispatcher.InvokeAsync(() =>
			{
				highlightRedrawQueued = false;
				RedrawHighlights();
			}, DispatcherPriority.Render);
		}
	}

	private void RedrawHighlights()
	{
		if (closed)
		{
			return;
		}
		foreach (UIElement highlightRing in highlightRings)
		{
			canvas.Children.Remove(highlightRing);
		}
		highlightRings.Clear();
		DateTime now = DateTime.UtcNow;
		foreach (var (key, dateTime2) in highlights.ToList())
		{
			double totalMilliseconds = (dateTime2 - now).TotalMilliseconds;
			Border border = TileOf(key);
			if (totalMilliseconds <= 50.0 || border == null)
			{
				highlights.Remove(key);
				if (border?.Effect is DropShadowEffect)
				{
					border.Effect = null;
				}
				continue;
			}
			Color accent = Theme.Accent;
			ScaleTransform scaleTransform = new ScaleTransform(1.0, 1.0);
			Border border2 = new Border
			{
				Width = border.Width + 10.0,
				Height = border.Height + 10.0,
				CornerRadius = new CornerRadius(16.0),
				BorderThickness = new Thickness(3.0),
				BorderBrush = new SolidColorBrush(accent),
				IsHitTestVisible = false,
				RenderTransformOrigin = new Point(0.5, 0.5),
				RenderTransform = scaleTransform
			};
			Canvas.SetLeft(border2, Canvas.GetLeft(border) - 5.0);
			Canvas.SetTop(border2, Canvas.GetTop(border) - 5.0);
			canvas.Children.Add(border2);
			highlightRings.Add(border2);
			DoubleAnimation animation = new DoubleAnimation(1.0, 1.22, TimeSpan.FromMilliseconds(700L))
			{
				RepeatBehavior = new RepeatBehavior(TimeSpan.FromMilliseconds(totalMilliseconds)),
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
			scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
			border2.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(700L))
			{
				RepeatBehavior = new RepeatBehavior(TimeSpan.FromMilliseconds(totalMilliseconds))
			});
			border.Effect = new DropShadowEffect
			{
				Color = accent,
				ShadowDepth = 0.0,
				BlurRadius = 24.0,
				Opacity = 1.0
			};
		}
		if (highlights.Count > 0 && !redrawScheduled)
		{
			redrawScheduled = true;
			Anim.After(highlights.Values.Min((DateTime u) => (u - now).TotalMilliseconds) + 30.0, () =>
			{
				redrawScheduled = false;
				QueueHighlightRedraw();
			});
		}
	}

	private List<string> TargetsFor(DesktopItem item)
	{
		if (!Sel.Contains(this, item.Key) || Sel.Keys.Count <= 1)
		{
			return new List<string> { item.Key };
		}
		return Sel.Ordered(this);
	}

	private ContextMenu BackgroundMenu(int index)
	{
		App app = App.Instance;
		PanelConfig destination = Placement;
		ContextMenu contextMenu = new ContextMenu
		{
			Items = { (object)Tiles.Item("새 폴더", () =>
			{
				app.CreateNewFolder(destination, index);
			}) }
		};
		MenuItem menuItem = Tiles.Item("붙여넣기", () =>
		{
			app.PasteToDesktop(destination, index);
		});
		menuItem.IsEnabled = App.ClipboardHasFiles();
		contextMenu.Items.Add(menuItem);
		contextMenu.Items.Add(new Separator());
		contextMenu.Items.Add(Tiles.Item("바탕화면 폴더 열기", app.OpenDesktopFolder));
		return contextMenu;
	}

	private List<MenuEntry> BuildExtras(DesktopItem item)
	{
		App app = App.Instance;
		List<string> targets = TargetsFor(item);
		string count = ((targets.Count > 1) ? $" ({targets.Count}개)" : "");
		List<MenuEntry> list = app.Config.Bags.Select((BagConfig b) => MenuEntry.Item(b.Name, () =>
		{
			app.MoveManyToBag(targets, b);
		})).ToList();
		if (list.Count > 0)
		{
			list.Add(MenuEntry.Separator);
		}
		list.Add(MenuEntry.Item("새 가방 만들어서 넣기...", () =>
		{
			app.CreateBag(targets);
		}));
		List<MenuEntry> children = (from p in app.EdgePanels.Where((EdgePanel p) => p != this).OrderBy((EdgePanel p) => app.PanelLabel(p), StringComparer.CurrentCulture)
			select MenuEntry.Item(app.PanelLabel(p) + count, () =>
			{
				app.MoveManyToPanel(targets, p.Placement, null);
			})).ToList();
		return new List<MenuEntry>
		{
			MenuEntry.Sub("가방에 넣기" + count, list),
			MenuEntry.Sub("다른 패널로 옮기기" + count, children)
		};
	}
}
