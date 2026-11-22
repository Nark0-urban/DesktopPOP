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

/// <summary>
/// 모니터 왼쪽/오른쪽 가장자리에 튀어나오는 아이콘 판. 마우스가 그 가장자리에 닿고 그곳에 실제 바탕화면이 보이면 열리고,
/// 마우스가 멀어지면 닫힘. 펼치기 단축키로 강제로 열 수도 있음(ForceOpen). 아이콘을 끌어 놓거나 판끼리 옮길 수 있음.
/// </summary>
internal sealed class EdgePanel : Window, ISelHost
{
	private const double PadX = 6.0;

	private const double PadY = 8.0;

	private const double Fade = 40.0;

	/// <summary>아이콘 한 칸의 가로·세로 크기 (칸 사이 간격 포함).</summary>
	private const double ColumnWidth = 86.0, RowHeight = 98.0;

	private const string RecycleBinKey = "::{645FF040-5081-101B-9F08-00AA002F954E}";

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
		// 끄는 중: 놓을 자리(폴더·휴지통 아이콘 위인지)를 표시하고 복사/이동 커서를 정함
		DragEventHandler onDragOver = (object s, DragEventArgs e) =>
		{
			string[]? draggedKeys = Tiles.KeysFrom(e.Data); // DesktopPop 안에서 끄는 아이콘들
			string[]? droppedFiles = e.Data.GetData(DataFormats.FileDrop) as string[]; // 탐색기 등에서 끄는 파일들
			(Border tile, DesktopItem item)? target = DropTargetAt(e.GetPosition(canvas), draggedKeys, droppedFiles);
			SetDropHover(target?.tile);
			string[]? realFiles = (draggedKeys != null) ? draggedKeys.Where((string k) => !k.StartsWith("::")).ToArray() : droppedFiles;
			if (target is { } onto && realFiles != null && realFiles.Length > 0)
			{
				// 폴더나 휴지통 아이콘 위에 놓으려는 중
				bool intoRecycleBin = onto.item.Key == RecycleBinKey;
				e.Effects = (!intoRecycleBin && WantsCopy(e, realFiles, onto.item.Path)) ? DragDropEffects.Copy : DragDropEffects.Move;
			}
			else if (draggedKeys != null)
			{
				e.Effects = DragDropEffects.Move; // 판 안에서 자리 옮기기
			}
			else if (droppedFiles != null && droppedFiles.Length > 0)
			{
				e.Effects = WantsCopy(e, droppedFiles, App.DesktopDir) ? DragDropEffects.Copy : DragDropEffects.Move;
			}
			else
			{
				e.Effects = DragDropEffects.None;
			}
			e.Effects &= e.AllowedEffects;
			e.Handled = true;
		};
		root.DragEnter += onDragOver;
		root.DragOver += onDragOver;
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
		// 화면 가장자리 쪽은 진하고 안쪽으로 갈수록 투명해지는 배경
		bool left = Side == Side.Left;
		Color panel = Theme.Panel;
		LinearGradientBrush fade = new LinearGradientBrush
		{
			StartPoint = new Point((!left) ? 1 : 0, 0.5),
			EndPoint = new Point(left ? 1 : 0, 0.5)
		};
		fade.GradientStops.Add(new GradientStop(Theme.WithAlpha(panel, 192), 0.0));
		fade.GradientStops.Add(new GradientStop(Theme.WithAlpha(panel, 136), 0.72));
		fade.GradientStops.Add(new GradientStop(Theme.WithAlpha(panel, 2), 1.0));
		fade.Freeze();
		bg.Background = fade;
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

	/// <summary>아이콘들을 세로로 채운 뒤 다음 줄로 (모니터 높이에 맞춰 칸 수 계산), 판 크기·위치를 정함.</summary>
	private void Layout()
	{
		dirty = false;
		App app = App.Instance;
		Rect workArea = Native.ToDipRect(Monitor.WorkArea);
		(double top, double height) = VerticalRange();
		List<DesktopItem> items = app.Store.VisibleFor(Placement);
		rows = Math.Max(1, (int)((height - 16.0) / RowHeight));
		int columns = Math.Max(1, (items.Count + rows - 1) / rows);
		Width = ((items.Count == 0) ? 188.0 : ((double)columns * ColumnWidth + 12.0 + 40.0));
		Height = height;
		Top = top;
		Left = ((Side == Side.Left) ? workArea.Left : (workArea.Right - Width));
		canvas.Children.Clear();
		tiles.Clear();
		tileByKey.Clear();
		presentations = items.Select(TilePresentation.Capture).ToArray();
		orderedKeys = items.Select((DesktopItem desktopItem) => desktopItem.Key).ToArray();
		if (items.Count == 0)
		{
			TextBlock emptyHint = new TextBlock
			{
				Text = app.PanelLabel(this) + "\n\n아이콘을 여기로\n끌어 놓으세요",
				Width = Width - 12.0 - 12.0,
				FontSize = 12.0,
				Foreground = Brushes.White,
				Opacity = 0.8,
				TextAlignment = TextAlignment.Center,
				TextWrapping = TextWrapping.Wrap,
				IsHitTestVisible = false
			};
			Canvas.SetLeft(emptyHint, 12.0);
			Canvas.SetTop(emptyHint, 28.0);
			canvas.Children.Add(emptyHint);
		}
		for (int i = 0; i < items.Count; i++)
		{
			DesktopItem item = items[i];
			int col = i / rows;
			int row = i % rows;
			// 왼쪽 판은 화면 가장자리부터 오른쪽으로, 오른쪽 판은 가장자리부터 왼쪽으로 줄을 채움
			double x = ((Side == Side.Left) ? (PadX + (double)col * ColumnWidth) : (Width - PadX - (double)(col + 1) * ColumnWidth));
			double y = PadY + (double)row * RowHeight;
			Border tile = Tiles.Create(item, backdrop: false);
			Tiles.Attach(tile, item, this, HidePanel, () => BuildExtras(item));
			Canvas.SetLeft(tile, x + 3.0);
			Canvas.SetTop(tile, y + 3.0);
			// 닫혀 있을 때 아이콘이 숨어 있는 자리 (화면 밖 쪽으로 밀어 둠)
			double hiddenOffset = ((Side == Side.Left) ? (0.0 - (x + ColumnWidth + 10.0)) : (Width - x + 10.0));
			TranslateTransform slide = (TranslateTransform)(tile.RenderTransform = new TranslateTransform(hiddenOffset, 0.0));
			tile.Opacity = 0.0;
			canvas.Children.Add(tile);
			tiles.Add((tile, slide, col, row, hiddenOffset));
			tileByKey.TryAdd(item.Key, tile);
			tile.Measure(new Size(tile.Width, tile.Height));
			tile.Arrange(new Rect(0.0, 0.0, tile.Width, tile.Height));
		}
		ApplySelection();
	}

	/// <summary>판의 위·아래 범위. 작업 표시줄이 자동 숨김이라 작업 영역에 안 잡혀도 그만큼 비켜 줌.</summary>
	private (double top, double height) VerticalRange()
	{
		Rect workArea = Native.ToDipRect(Monitor.WorkArea);
		double top = workArea.Top;
		double bottom = workArea.Bottom;
		if (Native.TryGetTaskbarRect(Monitor, out var taskbar))
		{
			Native.RECT bounds = Monitor.Bounds;
			double scale = Native.DipScale();
			bool horizontal = taskbar.Right - taskbar.Left > taskbar.Bottom - taskbar.Top;
			if (horizontal && scale > 0.0)
			{
				// 자동 숨김 작업 표시줄은 화면 밖으로 거의 다 빠져 있음
				bool autoHidden = Math.Min(taskbar.Bottom, bounds.Bottom) - Math.Max(taskbar.Top, bounds.Top) < 10;
				if (taskbar.Top > (bounds.Top + bounds.Bottom) / 2 || (autoHidden && taskbar.Top >= bounds.Bottom - 10))
				{
					bottom = Math.Min(bottom, autoHidden ? ((double)bounds.Bottom / scale - 52.0) : ((double)taskbar.Top / scale)); // 아래쪽 작업 표시줄
				}
				else
				{
					top = Math.Max(top, autoHidden ? ((double)bounds.Top / scale + 52.0) : ((double)taskbar.Bottom / scale)); // 위쪽 작업 표시줄
				}
			}
		}
		return (top: top, height: Math.Max(114.0, bottom - top));
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
			// 날아오는 중인 아이콘은 도착할 때까지 숨겨 둠
			bool stillFlying = flying.ContainsKey(((DesktopItem)tile.tile.Tag).Key);
			tile.tile.BeginAnimation(UIElement.OpacityProperty, stillFlying ? Anim.To(0.0, 1.0) : Anim.To(1.0, 220.0, delayMs));
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
		int lastRow = 0;
		foreach (var tile in tiles)
		{
			lastRow = Math.Max(lastRow, tile.row);
			double delayMs = tile.row * 6;
			tile.tt.BeginAnimation(TranslateTransform.XProperty, Anim.To(tile.off, 220.0, delayMs, new QuadraticEase
			{
				EasingMode = EasingMode.EaseIn
			}));
			tile.tile.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 220.0, delayMs));
		}
		bg.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 260.0));
		Anim.After(280 + lastRow * 6, () =>
		{
			// 닫는 연출이 끝났고 그 사이 다시 열리지 않았으면 창을 숨기고 아이콘을 숨은 자리로
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

	/// <summary>
	/// 30ms 마다 불림. 닫혀 있으면: 마우스가 이 가장자리에 머물고 그곳에 바탕화면이 보이면 잠깐 뒤 열기.
	/// 열려 있으면: 마우스가 판에서 멀어지거나 프로그램 창이 그 가장자리를 덮으면 닫기.
	/// </summary>
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
		App app = App.Instance;
		Native.RECT bounds = Monitor.Bounds;
		bool atEdge = Native.TryGetEdgeProbe(p, bounds, Side, app.EdgePanels.Select((EdgePanel panel) => panel.Bounds), out var probe);
		if (!shown)
		{
			if (!atEdge || !Native.IsDesktopExposed(probe))
			{
				edgeSince = DateTime.MinValue;
				return;
			}
			// 탐색기에서 파일을 끌고 온 경우 (마우스 버튼을 누른 채 끌기 모양 커서)
			bool draggingFromOutside = (atEdge & lbtn) && App.DragSource == null && Native.LooksLikeDragCursor();
			bool dragging = ((App.DragSource != null) | draggingFromOutside) || DesktopBand.IsActive;
			// 그냥 버튼을 누르고 있을 때(창 크기 조절 등)는 안 열림
			if (!atEdge || !(!lbtn | dragging) || App.OpenMenus != 0)
			{
				edgeSince = DateTime.MinValue;
				return;
			}
			if (edgeSince == DateTime.MinValue)
			{
				edgeSince = now;
			}
			double delayMs = (draggingFromOutside ? Math.Max(250, app.Config.EdgeDelayMs) : app.Config.EdgeDelayMs);
			if ((now - edgeSince).TotalMilliseconds >= delayMs)
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
			if (!atEdge)
			{
				probe = new Native.POINT
				{
					X = ((Side == Side.Left) ? bounds.Left : (bounds.Right - 1)),
					Y = Math.Clamp(p.Y, bounds.Top, bounds.Bottom - 1)
				};
			}
			// 다른 프로그램 창이 이 가장자리를 덮었으면 바로 닫음
			if (!Native.IsDesktopExposed(probe))
			{
				awaySince = DateTime.MinValue;
				HidePanel();
				return;
			}
			Native.GetWindowRect(hwnd, out var panelRect);
			bool nearPanel = Native.Contains(bounds, p) && p.X >= panelRect.Left - 24 && p.X <= panelRect.Right + 24 && p.Y >= panelRect.Top - 24 && p.Y <= panelRect.Bottom + 24;
			if (atEdge || nearPanel || now < peekUntil)
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

	/// <summary>판 안의 위치 → 몇 번째 칸인지 (아이콘을 놓을 자리 계산용).</summary>
	private int IndexAt(Point pos)
	{
		double columnFromEdge = ((Side == Side.Left) ? ((pos.X - PadX) / ColumnWidth) : ((Width - PadX - pos.X) / ColumnWidth));
		int col = Math.Max(0, (int)Math.Floor(columnFromEdge));
		int row = Math.Clamp((int)Math.Floor((pos.Y - PadY) / RowHeight), 0, rows - 1);
		return col * rows + row;
	}

	/// <summary>탐색기처럼: Ctrl = 복사, Shift = 이동, 아무것도 안 누르면 다른 드라이브면 복사·같은 드라이브면 이동.</summary>

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
		string sourceRoot = Path.GetPathRoot(files[0]) ?? "";
		string destRoot = Path.GetPathRoot(destDir) ?? "";
		return !string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// 끌어 온 것을 놓을 수 있는 폴더·휴지통 아이콘이 이 위치에 있으면 그 아이콘. 칸 가장자리는 빼서
	/// "아이콘 사이에 끼워 넣기"와 "폴더 안에 넣기"가 구별되게 함.
	/// </summary>
	private (Border tile, DesktopItem item)? DropTargetAt(Point pos, string[]? draggedKeys, string[]? files)
	{
		int index = IndexAt(pos);
		if (index < 0 || index >= tiles.Count)
		{
			return null;
		}
		Border tile = tiles[index].tile;
		if (tile.Tag is not DesktopItem target)
		{
			return null;
		}
		if (draggedKeys != null && draggedKeys.Any((string k) => string.Equals(k, target.Key, StringComparison.OrdinalIgnoreCase)))
		{
			return null; // 자기 자신 위
		}
		bool isRecycleBin = target.Key == RecycleBinKey;
		bool isFolder = !target.IsVirtual && Directory.Exists(target.Path);
		if (!isRecycleBin && !isFolder)
		{
			return null;
		}
		string[] sources = (draggedKeys ?? files ?? Array.Empty<string>()).Where((string k) => !k.StartsWith("::")).ToArray();
		if (sources.Length == 0)
		{
			return null;
		}
		// 폴더를 자기 자신이나 자기 안쪽 폴더에 넣을 수는 없음
		if (isFolder && sources.Any((string s) => string.Equals(s.TrimEnd('\\'), target.Path, StringComparison.OrdinalIgnoreCase) || target.Path.StartsWith(s.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
		{
			return null;
		}
		double left = Canvas.GetLeft(tile);
		double top = Canvas.GetTop(tile);
		if (pos.X < left + 14.0 || pos.X > left + tile.Width - 14.0 || pos.Y < top + 4.0 || pos.Y > top + tile.Height - 14.0)
		{
			return null;
		}
		return (tile, target);
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
		string[]? draggedKeys = Tiles.KeysFrom(e.Data);
		string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
		(Border tile, DesktopItem item)? target = DropTargetAt(position, draggedKeys, files);
		SetDropHover(null);
		App app = App.Instance;
		PanelConfig destination = Placement;
		if (target is { } onto)
		{
			// 폴더 아이콘 위 = 그 폴더로 옮기기/복사, 휴지통 위 = 삭제
			DesktopItem folder = onto.item;
			string[] srcs = (draggedKeys ?? files)!.Where((string k) => !k.StartsWith("::")).ToArray();
			bool bin = folder.Key == RecycleBinKey;
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
			e.Effects = ((draggedKeys != null) ? DragDropEffects.Move : (DragDropEffects.Copy & e.AllowedEffects));
		}
		else if (draggedKeys != null)
		{
			// 판·가방에서 끌어 온 아이콘: 이 판의 그 자리로 (파일은 그대로)
			app.MoveManyToPanel(draggedKeys, destination, index);
			e.Effects = DragDropEffects.Move;
		}
		else if (files != null && files.Length > 0)
		{
			// 탐색기에서 끌어 온 파일: 바탕화면 폴더로 옮기거나 복사하고 이 자리에 놓음
			bool copyToDesktop = WantsCopy(e, files, App.DesktopDir);
			Dispatcher.InvokeAsync(() =>
			{
				app.BringFilesToDesktop(files, copyToDesktop, destination, index);
			});
			e.Effects = DragDropEffects.Copy & e.AllowedEffects;
		}
		e.Handled = true;
	}

	/// <summary>
	/// 새로 들어온 아이콘을 보여 줌: 판을 잠깐 열고, fly 인 것은 from(마우스 자리)에서 제자리로 날아오게 하고, 반짝 강조.
	/// </summary>
	public void RevealArrivals(List<(string key, bool fly)> items, Point from)
	{
		if (closed)
		{
			return;
		}
		bool wasShown = shown;
		peekUntil = DateTime.UtcNow.AddMilliseconds(3200.0);
		if (!shown)
		{
			ShowPanel();
		}
		if (!shown)
		{
			return;
		}
		int flyCount = 0;
		foreach (var (key, fly) in items)
		{
			Border tile = TileOf(key);
			if (tile == null)
			{
				continue;
			}
			if (fly && flyCount < 12)
			{
				double delayMs = flyCount++ * 90;
				flying[key] = ((!flying.TryGetValue(key, out var inFlight)) ? 1 : (inFlight + 1));
				tile.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 1.0));
				FlyOverlay.Fly(to: new Point(Left + Canvas.GetLeft(tile) + tile.Width / 2.0, Top + Canvas.GetTop(tile) + tile.Height / 2.0), item: (DesktopItem)tile.Tag, from: from, delayMs: delayMs, onArrive: () =>
				{
					if (!closed)
					{
						if (flying.TryGetValue(key, out var stillInFlight) && --stillInFlight > 0)
						{
							flying[key] = stillInFlight;
						}
						else
						{
							flying.Remove(key);
						}
						Border arrivedTile = TileOf(key);
						if (arrivedTile != null)
						{
							if (!flying.ContainsKey(key))
							{
								arrivedTile.BeginAnimation(UIElement.OpacityProperty, Anim.To(1.0, 80.0));
							}
							Highlight(key);
							peekUntil = DateTime.UtcNow.AddMilliseconds(1800.0);
						}
					}
				});
			}
			else
			{
				Anim.After(wasShown ? 60 : 700, () =>
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
		foreach (var (key, until) in highlights.ToList())
		{
			double remainingMs = (until - now).TotalMilliseconds;
			Border tile = TileOf(key);
			if (remainingMs <= 50.0 || tile == null)
			{
				highlights.Remove(key);
				if (tile?.Effect is DropShadowEffect)
				{
					tile.Effect = null;
				}
				continue;
			}
			// 아이콘 둘레에 퍼져 나가는 고리 + 은은한 빛
			Color accent = Theme.Accent;
			ScaleTransform scaleTransform = new ScaleTransform(1.0, 1.0);
			Border ring = new Border
			{
				Width = tile.Width + 10.0,
				Height = tile.Height + 10.0,
				CornerRadius = new CornerRadius(16.0),
				BorderThickness = new Thickness(3.0),
				BorderBrush = new SolidColorBrush(accent),
				IsHitTestVisible = false,
				RenderTransformOrigin = new Point(0.5, 0.5),
				RenderTransform = scaleTransform
			};
			Canvas.SetLeft(ring, Canvas.GetLeft(tile) - 5.0);
			Canvas.SetTop(ring, Canvas.GetTop(tile) - 5.0);
			canvas.Children.Add(ring);
			highlightRings.Add(ring);
			DoubleAnimation animation = new DoubleAnimation(1.0, 1.22, TimeSpan.FromMilliseconds(700L))
			{
				RepeatBehavior = new RepeatBehavior(TimeSpan.FromMilliseconds(remainingMs)),
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
			scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
			ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(700L))
			{
				RepeatBehavior = new RepeatBehavior(TimeSpan.FromMilliseconds(remainingMs))
			});
			tile.Effect = new DropShadowEffect
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
		MenuItem pasteItem = Tiles.Item("붙여넣기", () =>
		{
			app.PasteToDesktop(destination, index);
		});
		pasteItem.IsEnabled = App.ClipboardHasFiles();
		contextMenu.Items.Add(pasteItem);
		contextMenu.Items.Add(new Separator());
		contextMenu.Items.Add(Tiles.Item("바탕화면 폴더 열기", app.OpenDesktopFolder));
		return contextMenu;
	}

	private List<MenuEntry> BuildExtras(DesktopItem item)
	{
		App app = App.Instance;
		List<string> targets = TargetsFor(item);
		string count = ((targets.Count > 1) ? $" ({targets.Count}개)" : "");
		List<MenuEntry> bagItems = app.Config.Bags.Select((BagConfig b) => MenuEntry.Item(b.Name, () =>
		{
			app.MoveManyToBag(targets, b);
		})).ToList();
		if (bagItems.Count > 0)
		{
			bagItems.Add(MenuEntry.Separator);
		}
		bagItems.Add(MenuEntry.Item("새 가방 만들어서 넣기...", () =>
		{
			app.CreateBag(targets);
		}));
		List<MenuEntry> panelItems = (from p in app.EdgePanels.Where((EdgePanel p) => p != this).OrderBy((EdgePanel p) => app.PanelLabel(p), StringComparer.CurrentCulture)
			select MenuEntry.Item(app.PanelLabel(p) + count, () =>
			{
				app.MoveManyToPanel(targets, p.Placement, null);
			})).ToList();
		return new List<MenuEntry>
		{
			MenuEntry.Sub("가방에 넣기" + count, bagItems),
			MenuEntry.Sub("다른 패널로 옮기기" + count, panelItems)
		};
	}
}
