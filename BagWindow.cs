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
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DesktopPop;

/// <summary>
/// 바탕화면 위에 떠 있는 "가방" 하나. 아이콘을 끌어 넣어 보관하고(파일은 실제로 옮기지 않음), 클릭하면 둘레로 펼쳐짐.
/// 끌면 위치 이동, 우클릭하면 이름·그림·크기·지우기 메뉴.
/// </summary>
internal sealed class BagWindow : Window, ISelHost
{
	private readonly RubberBand haloBand;

	public readonly BagConfig Cfg;

	private readonly Canvas canvas = new Canvas();

	private readonly Grid bagVisual = new Grid();

	private readonly Border hit = new Border();

	private readonly Image bagImg = new Image();

	private readonly TextBlock label = new TextBlock();

	private readonly Border badge = new Border();

	private readonly TextBlock badgeText = new TextBlock();

	private readonly ScaleTransform bagScale = new ScaleTransform(1.0, 1.0);

	private readonly Ellipse halo = new Ellipse();

	private readonly Border hintBox = new Border();

	private readonly List<Border> ringTiles = new List<Border>();

	private readonly Dictionary<string, Border> tileByKey = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);

	private string[] ringKeys = Array.Empty<string>();

	private TilePresentation[] presentations = Array.Empty<TilePresentation>();

	private List<Point> ringPoints = new List<Point>();

	private Rect layoutWorkArea;

	private int ringTransition;

	private bool open;

	private double center;

	private Point? downPt;

	public bool IntentionalClose;

	private double ringRadius;

	private string? loadedImagePath;

	private DateTime loadedImageStamp;

	private long loadedImageLength;

	private ImageSource? loadedImage;

	private static readonly ImageSource fallbackImage = DefaultImage();

	public IReadOnlyList<string> OrderedKeys
	{
		get
		{
			if (!open)
			{
				return Array.Empty<string>();
			}
			return ringKeys;
		}
	}

	public bool BandSelectable => open;

	private Point CenterDip => new Point(Cfg.X, Cfg.Y);

	public bool IsForcedOpen { get; private set; }

	private static Effect Glow => new DropShadowEffect
	{
		Color = Theme.Glow,
		ShadowDepth = 0.0,
		BlurRadius = 24.0,
		Opacity = 0.95
	};

	public Border? TileOf(string key)
	{
		if (!open || !tileByKey.TryGetValue(key, out Border value))
		{
			return null;
		}
		return value;
	}

	public void ApplySelection()
	{
		foreach (Border ringTile in ringTiles)
		{
			Tiles.SetSelected(ringTile, Sel.Contains(this, ((DesktopItem)ringTile.Tag).Key));
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

	public BagWindow(BagConfig cfg, bool useDesktopOwner = true)
	{
		Cfg = cfg;
		Title = "DesktopPop Bag";
		WindowStyle = WindowStyle.None;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		ShowInTaskbar = false;
		ShowActivated = false;
		ResizeMode = ResizeMode.NoResize;
		Topmost = false;
		Content = canvas;
		if (useDesktopOwner)
		{
			// 바탕화면 창을 주인으로 삼아 "바탕화면 보기"에도 함께 남아 있게
			nint progman = DesktopShell.FindProgman();
			if (progman != IntPtr.Zero)
			{
				new WindowInteropHelper(this).Owner = progman;
			}
		}
		SourceInitialized += (object? s, EventArgs e) =>
		{
			Native.MakeToolWindow(new WindowInteropHelper(this).Handle);
		};
		Closed += (object? s, EventArgs e) =>
		{
			if (!IntentionalClose)
			{
				App.Instance.OnBagLost(this);
			}
		};
		Deactivated += (object? s, EventArgs e) =>
		{
			if (!IsForcedOpen && open && App.OpenMenus == 0 && App.DragSource == null)
			{
				CloseRing();
			}
		};
		PreviewKeyDown += (object s, KeyEventArgs e) =>
		{
			if (e.Key == Key.Escape)
			{
				CloseRing();
			}
		};
		halo.Opacity = 0.0;
		halo.IsHitTestVisible = false;
		haloBand = new RubberBand(halo, canvas, this)
		{
			OnClick = () =>
			{
				if (IsForcedOpen)
				{
					App.Instance.SetAllPanelsOpen(open: false);
				}
				else
				{
					CloseRing();
				}
			}
		};
		Panel.SetZIndex(halo, 0);
		canvas.Children.Add(halo);
		hit.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
		hit.CornerRadius = new CornerRadius(22.0);
		bagImg.Stretch = Stretch.Uniform;
		RenderOptions.SetBitmapScalingMode(bagImg, BitmapScalingMode.HighQuality);
		label.Foreground = Brushes.White;
		label.FontSize = 12.5;
		label.FontWeight = FontWeights.SemiBold;
		if (UiFont.BagNameFamily is FontFamily bagNameFont)
		{
			label.FontFamily = bagNameFont; // data\fonts 에 넣은 폰트 (가방 이름에만)
		}
		label.TextAlignment = TextAlignment.Center;
		label.TextTrimming = TextTrimming.CharacterEllipsis;
		label.Effect = new DropShadowEffect
		{
			ShadowDepth = 1.0,
			BlurRadius = 4.0,
			Opacity = 0.95,
			Color = Colors.Black
		};
		badgeText.Foreground = Brushes.White;
		badgeText.FontSize = 11.0;
		badgeText.FontWeight = FontWeights.Bold;
		badgeText.HorizontalAlignment = HorizontalAlignment.Center;
		badgeText.VerticalAlignment = VerticalAlignment.Center;
		badge.CornerRadius = new CornerRadius(10.0);
		badge.MinWidth = 20.0;
		badge.Height = 20.0;
		badge.Padding = new Thickness(5.0, 0.0, 5.0, 0.0);
		badge.Child = badgeText;
		badge.IsHitTestVisible = false;
		bagVisual.Children.Add(hit);
		bagVisual.Children.Add(bagImg);
		bagVisual.Children.Add(label);
		bagVisual.Children.Add(badge);
		bagVisual.RenderTransform = bagScale;
		bagVisual.CacheMode = new BitmapCache();
		bagVisual.Cursor = Cursors.Hand;
		bagVisual.AllowDrop = true;
		Panel.SetZIndex(bagVisual, 5);
		canvas.Children.Add(bagVisual);
		hintBox.CornerRadius = new CornerRadius(10.0);
		hintBox.Padding = new Thickness(12.0, 8.0, 12.0, 8.0);
		hintBox.Width = 230.0;
		hintBox.Opacity = 0.0;
		hintBox.IsHitTestVisible = false;
		hintBox.Child = new TextBlock
		{
			Text = "가방이 비어 있어요!\n가장자리 아이콘을 여기로 끌어다 놓아 보세요.",
			Foreground = Brushes.White,
			FontSize = 12.0,
			TextWrapping = TextWrapping.Wrap,
			TextAlignment = TextAlignment.Center
		};
		Panel.SetZIndex(hintBox, 10);
		canvas.Children.Add(hintBox);
		HookEvents();
		Rebuild();
	}

	private void HookEvents()
	{
		bagVisual.MouseEnter += (object s, MouseEventArgs e) =>
		{
			bagImg.Effect = Glow;
			bagScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.07, 130.0));
			bagScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.07, 130.0));
		};
		bagVisual.MouseLeave += (object s, MouseEventArgs e) =>
		{
			bagImg.Effect = null;
			bagScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.0, 160.0));
			bagScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.0, 160.0));
		};
		bagVisual.MouseLeftButtonDown += (object s, MouseButtonEventArgs e) =>
		{
			downPt = e.GetPosition(this);
			bagVisual.CaptureMouse();
			e.Handled = true;
		};
		bagVisual.MouseMove += (object s, MouseEventArgs e) =>
		{
			// 누른 채 6px 넘게 움직이면 클릭이 아니라 가방 옮기기
			if (downPt is Point down)
			{
				if (e.LeftButton == MouseButtonState.Pressed)
				{
					Point position = e.GetPosition(this);
					if (!(Math.Abs(position.X - down.X) + Math.Abs(position.Y - down.Y) < 6.0))
					{
						downPt = null;
						bagVisual.ReleaseMouseCapture();
						CloseRingCore(animate: false);
						try
						{
							DragMove();
						}
						catch (InvalidOperationException)
						{
						}
						Cfg.X = Left + Width / 2.0;
						Cfg.Y = Top + Height / 2.0;
						Rebuild();
						App.Instance.Config.Save();
					}
				}
			}
		};
		bagVisual.MouseLeftButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			if (downPt.HasValue)
			{
				downPt = null;
				bagVisual.ReleaseMouseCapture();
				if (open)
				{
					CloseRing();
				}
				else
				{
					OpenRing();
				}
				e.Handled = true;
			}
		};
		bagVisual.MouseRightButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			ContextMenu contextMenu = BagMenu();
			App.Track(contextMenu);
			contextMenu.PlacementTarget = bagVisual;
			contextMenu.IsOpen = true;
			e.Handled = true;
		};
		DragEventHandler over = (object s, DragEventArgs e) =>
		{
			if (e.Data.GetDataPresent("DesktopPop.ItemKey"))
			{
				e.Effects = DragDropEffects.Move;
			}
			else if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				e.Effects = DragDropEffects.Link;
			}
			else
			{
				e.Effects = DragDropEffects.None;
			}
			if (e.Effects != DragDropEffects.None)
			{
				bagImg.Effect = Glow;
			}
			e.Handled = true;
		};
		bagVisual.DragEnter += (object s, DragEventArgs e) =>
		{
			over(s, e);
			if (e.Effects != DragDropEffects.None)
			{
				bagScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.2, 160.0, 0.0, new BackEase
				{
					Amplitude = 0.5,
					EasingMode = EasingMode.EaseOut
				}));
				bagScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.2, 160.0, 0.0, new BackEase
				{
					Amplitude = 0.5,
					EasingMode = EasingMode.EaseOut
				}));
			}
		};
		bagVisual.DragOver += over;
		bagVisual.DragLeave += (object s, DragEventArgs e) =>
		{
			bagImg.Effect = null;
			bagScale.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.0, 160.0));
			bagScale.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.0, 160.0));
		};
		bagVisual.Drop += (object s, DragEventArgs e) =>
		{
			bagImg.Effect = null;
			App app = App.Instance;
			string[]? draggedKeys = Tiles.KeysFrom(e.Data);
			if (draggedKeys != null)
			{
				app.MoveManyToBag(draggedKeys, Cfg);
			}
			else if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
			{
				app.MoveManyToBag(files, Cfg); // 탐색기에서 끌어 온 파일도 가리키기만 함 (파일은 그대로)
			}
			Bounce();
			e.Handled = true;
		};
	}

	private List<DesktopItem> Items()
	{
		return (from k in Cfg.Items
			select App.Instance.Store.Get(k) into i
			where i != null
			select (i)).ToList();
	}

	internal void RefreshItems()
	{
		if (!TilePresentation.Matches(presentations, Items()) || WorkAreaAtCenter() != layoutWorkArea)
		{
			Rebuild();
		}
	}

	public void Rebuild()
	{
		bool wasOpen = open;
		if (open)
		{
			CloseRingCore(animate: false);
		}
		ringTransition++;
		foreach (Border ringTile in ringTiles)
		{
			canvas.Children.Remove(ringTile);
		}
		ringTiles.Clear();
		tileByKey.Clear();
		RebuildCore();
		if ((wasOpen || IsForcedOpen) && ringTiles.Count > 0)
		{
			OpenRing(!IsForcedOpen);
		}
	}

	public void ForceOpen(bool value)
	{
		IsForcedOpen = value;
		if (value)
		{
			if (!IsVisible)
			{
				Show();
			}
			Topmost = true;
			if (ringTiles.Count > 0)
			{
				OpenRing(activate: false);
			}
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				Native.SetTopmost(handle);
			}
		}
		else
		{
			CloseRingCore(animate: true);
			Topmost = false;
		}
	}

	private void RebuildCore()
	{
		// 이름 글자 크기에 맞춰 이름 칸의 폭·높이를 정함 (기본 12.5 → 폭 120, 높이 24)
		double nameSize = Math.Clamp(Cfg.NameFontSize, 8.0, 48.0);
		labelHeight = Math.Max(24.0, Math.Ceiling(nameSize * 1.6));
		labelWidth = Math.Max(120.0, Math.Ceiling(nameSize * 9.6));
		label.FontSize = nameSize;
		// 화면 연결이 바뀌어 가방이 밖으로 나갔으면 화면 안으로
		Point clamped = BagLayout.ClampCenter(CenterDip, Cfg.Size, WorkAreaAtCenter(), labelWidth, labelHeight);
		Cfg.X = clamped.X;
		Cfg.Y = clamped.Y;
		layoutWorkArea = WorkAreaAtCenter();
		Color c = Theme.Halo;
		RadialGradientBrush haloFill = new RadialGradientBrush();
		haloFill.GradientStops.Add(new GradientStop(Theme.WithAlpha(c, 154), 0.0));
		haloFill.GradientStops.Add(new GradientStop(Theme.WithAlpha(c, 112), 0.75));
		haloFill.GradientStops.Add(new GradientStop(Theme.WithAlpha(c, 0), 1.0));
		haloFill.Freeze();
		halo.Fill = haloFill;
		badge.Background = Theme.Brush(Theme.Badge);
		hintBox.Background = Theme.Brush(Theme.Hint, 230);
		double size = Cfg.Size;
		double box = size + 24.0; // 가방 그림 + 둘레 여유
		double visualWidth = Math.Max(box, labelWidth); // 이름 글자 폭만큼은 넓게
		bagVisual.Width = visualWidth;
		bagVisual.Height = box + labelHeight;
		bagVisual.RenderTransformOrigin = new Point(0.5, box / 2.0 / (box + labelHeight)); // 그림 가운데를 기준으로 커지고 작아짐
		hit.Width = box;
		hit.Height = box;
		hit.HorizontalAlignment = HorizontalAlignment.Center;
		hit.VerticalAlignment = VerticalAlignment.Top;
		bagImg.Width = size;
		bagImg.Height = size;
		bagImg.HorizontalAlignment = HorizontalAlignment.Center;
		bagImg.VerticalAlignment = VerticalAlignment.Top;
		bagImg.Margin = new Thickness(0.0, 12.0, 0.0, 0.0);
		bagImg.Source = LoadImage(Cfg.ImagePath);
		label.Text = Cfg.Name;
		label.VerticalAlignment = VerticalAlignment.Bottom;
		badge.HorizontalAlignment = HorizontalAlignment.Right;
		badge.VerticalAlignment = VerticalAlignment.Top;
		// 개수 배지도 가방 크기에 맞춰 (기본 크기 88 일 때 지름 20, 글자 11)
		double badgeScale = Math.Max(0.75, size / 88.0);
		badge.Height = 20.0 * badgeScale;
		badge.MinWidth = 20.0 * badgeScale;
		badge.CornerRadius = new CornerRadius(10.0 * badgeScale);
		badge.Padding = new Thickness(5.0 * badgeScale, 0.0, 5.0 * badgeScale, 0.0);
		badgeText.FontSize = 11.0 * badgeScale;
		badge.Margin = new Thickness(0.0, 2.0 * badgeScale, (visualWidth - box) / 2.0 + 2.0 * badgeScale, 0.0);
		List<DesktopItem> items = Items();
		badgeText.Text = items.Count.ToString();
		badge.Visibility = ((items.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		List<Point> points = (ringPoints = ComputeRing(items.Count));
		double radius = (ringRadius = ((points.Count == 0) ? 0.0 : points.Max((Point p) => Math.Sqrt(p.X * p.X + p.Y * p.Y))));
		// 창은 펼친 아이콘까지 다 들어가는 정사각형, 가방이 가운데
		double windowSize = Math.Max(Math.Max(size + 260.0, 2.0 * (radius + 86.0)), Math.Max(visualWidth + 40.0, box + 2.0 * labelHeight + 40.0));
		center = windowSize / 2.0;
		Width = windowSize;
		Height = windowSize;
		Left = Cfg.X - center;
		Top = Cfg.Y - center;
		Canvas.SetLeft(bagVisual, center - visualWidth / 2.0);
		Canvas.SetTop(bagVisual, center - box / 2.0);
		double haloRadius = radius + 73.1; // 펼친 아이콘 뒤 둥근 배경
		halo.Width = (halo.Height = 2.0 * haloRadius);
		Canvas.SetLeft(halo, center - haloRadius);
		Canvas.SetTop(halo, center - haloRadius);
		Canvas.SetLeft(hintBox, center - hintBox.Width / 2.0);
		Canvas.SetTop(hintBox, center - box / 2.0 - 72.0);
		PrepareRingTiles(items);
	}

	private List<Point> ComputeRing(int n)
	{
		if (n == 0)
		{
			return new List<Point>();
		}
		return BagLayout.ComputeRing(n, Cfg.Size, CenterDip, layoutWorkArea, 86.0, 98.0, labelWidth, labelHeight);
	}

	/// <summary>가방 이름 칸의 폭·높이 (이름 글자 크기에 따라 RebuildCore 에서 정함).</summary>
	private double labelWidth = 120.0, labelHeight = 24.0;

	private Rect WorkAreaAtCenter()
	{
		double scale = Native.DipScale();
		return Native.ToDipRect(Native.MonitorAt(new Native.POINT
		{
			X = (int)Math.Round(Cfg.X * scale),
			Y = (int)Math.Round(Cfg.Y * scale)
		}).WorkArea);
	}

	private void Bounce()
	{
		ElasticEase easingFunction = new ElasticEase
		{
			Oscillations = 2,
			Springiness = 6.0,
			EasingMode = EasingMode.EaseOut
		};
		DoubleAnimation animation = new DoubleAnimation(0.82, 1.0, TimeSpan.FromMilliseconds(600L))
		{
			EasingFunction = easingFunction
		};
		bagScale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
		bagScale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
	}

	private void PrepareRingTiles(List<DesktopItem> items)
	{
		presentations = items.Select(TilePresentation.Capture).ToArray();
		ringKeys = items.Select((DesktopItem desktopItem) => desktopItem.Key).ToArray();
		for (int i = 0; i < items.Count; i++)
		{
			DesktopItem item = items[i];
			Border border = Tiles.Create(item, backdrop: true);
			// 접혀 있을 땐 가방 가운데에 작게 숨어 있다가 펼칠 때 제자리로 날아감
			ScaleTransform shrink = new ScaleTransform(0.2, 0.2);
			TranslateTransform move = new TranslateTransform();
			TransformGroup transformGroup = new TransformGroup();
			transformGroup.Children.Add(shrink);
			transformGroup.Children.Add(move);
			border.RenderTransform = transformGroup;
			border.RenderTransformOrigin = new Point(0.5, 0.5);
			border.Opacity = 0.0;
			border.Visibility = Visibility.Hidden;
			border.IsHitTestVisible = false;
			Canvas.SetLeft(border, center - border.Width / 2.0);
			Canvas.SetTop(border, center - border.Height / 2.0);
			Panel.SetZIndex(border, 2);
			Tiles.Attach(border, item, this, () =>
			{
				CloseRing();
			}, () => ItemExtras(item), IsOutsideRing, (List<string> keys, Native.POINT p) =>
			{
				// 원 밖으로 끌어 놓으면 꺼냄: 가방 오른쪽에 놓았으면 오른쪽 판, 왼쪽이면 왼쪽 판으로
				double scale = Native.DipScale();
				Point dropAt = new Point((double)p.X / scale, (double)p.Y / scale);
				Side side = ((dropAt.X >= Cfg.X) ? Side.Right : Side.Left);
				App.Instance.TakeOutManyToSide(keys, Cfg, side, dropAt);
			});
			canvas.Children.Add(border);
			ringTiles.Add(border);
			tileByKey.TryAdd(item.Key, border);
			border.Measure(new Size(border.Width, border.Height));
			border.Arrange(new Rect(0.0, 0.0, border.Width, border.Height));
		}
	}

	private void OpenRing(bool activate = true)
	{
		if (ringTiles.Count == 0)
		{
			ShowHint();
		}
		else if (!open)
		{
			ringTransition++;
			open = true;
			if (activate)
			{
				Activate();
			}
			Bounce();
			halo.IsHitTestVisible = true;
			halo.BeginAnimation(UIElement.OpacityProperty, Anim.To(1.0, 260.0));
			for (int i = 0; i < ringTiles.Count; i++)
			{
				Border border = ringTiles[i];
				TransformGroup transformGroup = (TransformGroup)border.RenderTransform;
				ScaleTransform scaleTransform = (ScaleTransform)transformGroup.Children[0];
				TranslateTransform translateTransform = (TranslateTransform)transformGroup.Children[1];
				ResetTile(border);
				border.Visibility = Visibility.Visible;
				border.IsHitTestVisible = true;
				double delayMs = i * 22;
				QuadraticEase ease = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				};
				translateTransform.BeginAnimation(TranslateTransform.XProperty, Anim.To(ringPoints[i].X, 470.0, delayMs, ease));
				translateTransform.BeginAnimation(TranslateTransform.YProperty, Anim.To(ringPoints[i].Y, 470.0, delayMs, ease));
				scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(1.0, 470.0, delayMs, ease));
				scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(1.0, 470.0, delayMs, ease));
				border.BeginAnimation(UIElement.OpacityProperty, Anim.To(1.0, 180.0, delayMs));
			}
		}
	}

	private bool IsOutsideRing(Native.POINT p)
	{
		double scale = Native.DipScale();
		double dx = (double)p.X / scale - Cfg.X;
		double dy = (double)p.Y / scale - Cfg.Y;
		return Math.Sqrt(dx * dx + dy * dy) > ringRadius + 51.6;
	}

	public void CloseRing(bool animate = true)
	{
		if (!IsForcedOpen)
		{
			CloseRingCore(animate);
		}
	}

	private void CloseRingCore(bool animate)
	{
		if (!open)
		{
			return;
		}
		open = false;
		Sel.Clear(this);
		int token = ++ringTransition;
		halo.IsHitTestVisible = false;
		if (!animate)
		{
			ParkRingTiles();
			halo.BeginAnimation(UIElement.OpacityProperty, null);
			halo.Opacity = 0.0;
			return;
		}
		halo.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 220.0));
		for (int i = 0; i < ringTiles.Count; i++)
		{
			Border border = ringTiles[i];
			border.IsHitTestVisible = false;
			TransformGroup transformGroup = (TransformGroup)border.RenderTransform;
			ScaleTransform scaleTransform = (ScaleTransform)transformGroup.Children[0];
			TranslateTransform translateTransform = (TranslateTransform)transformGroup.Children[1];
			double delayMs = (ringTiles.Count - 1 - i) * 10;
			QuadraticEase ease = new QuadraticEase
			{
				EasingMode = EasingMode.EaseIn
			};
			translateTransform.BeginAnimation(TranslateTransform.XProperty, Anim.To(0.0, 230.0, delayMs, ease));
			translateTransform.BeginAnimation(TranslateTransform.YProperty, Anim.To(0.0, 230.0, delayMs, ease));
			scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, Anim.To(0.2, 230.0, delayMs, ease));
			scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, Anim.To(0.2, 230.0, delayMs, ease));
			border.BeginAnimation(UIElement.OpacityProperty, Anim.To(0.0, 230.0, delayMs));
		}
		Anim.After(280 + ringTiles.Count * 10, () =>
		{
			if (!open && token == ringTransition)
			{
				ParkRingTiles();
			}
		});
	}

	private static void ResetTile(Border tile)
	{
		TransformGroup transformGroup = (TransformGroup)tile.RenderTransform;
		ScaleTransform scaleTransform = (ScaleTransform)transformGroup.Children[0];
		TranslateTransform translateTransform = (TranslateTransform)transformGroup.Children[1];
		translateTransform.BeginAnimation(TranslateTransform.XProperty, null);
		translateTransform.BeginAnimation(TranslateTransform.YProperty, null);
		scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
		scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
		tile.BeginAnimation(UIElement.OpacityProperty, null);
		translateTransform.X = (translateTransform.Y = 0.0);
		scaleTransform.ScaleX = (scaleTransform.ScaleY = 0.2);
		tile.Opacity = 0.0;
	}

	private void ParkRingTiles()
	{
		foreach (Border ringTile in ringTiles)
		{
			ringTile.IsHitTestVisible = false;
			ringTile.Visibility = Visibility.Hidden;
			ResetTile(ringTile);
		}
		halo.BeginAnimation(UIElement.OpacityProperty, null);
		halo.Opacity = 0.0;
	}

	private void ShowHint()
	{
		Bounce();
		DoubleAnimationUsingKeyFrames doubleAnimationUsingKeyFrames = new DoubleAnimationUsingKeyFrames
		{
			Duration = TimeSpan.FromMilliseconds(2600L)
		};
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(200L))));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2200L))));
		doubleAnimationUsingKeyFrames.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2600L))));
		hintBox.BeginAnimation(UIElement.OpacityProperty, doubleAnimationUsingKeyFrames);
	}

	private List<MenuEntry> ItemExtras(DesktopItem item)
	{
		App app = App.Instance;
		List<string> targets = TargetsFor(item);
		string count = ((targets.Count > 1) ? $" ({targets.Count}개)" : "");
		List<MenuEntry> entries = new List<MenuEntry> { MenuEntry.Item("가방에서 꺼내기" + count, () =>
		{
			app.TakeOutManyToSide(targets, Cfg, Side.Left, CenterDip);
		}) };
		List<BagConfig> otherBags = app.Config.Bags.Where((BagConfig b) => b != Cfg).ToList();
		if (otherBags.Count > 0)
		{
			entries.Add(MenuEntry.Sub("다른 가방으로 옮기기" + count, otherBags.Select((BagConfig b) => MenuEntry.Item(b.Name, () =>
			{
				app.MoveManyToBag(targets, b);
			})).ToList()));
		}
		return entries;
	}

	private ContextMenu BagMenu()
	{
		App app = App.Instance;
		ContextMenu contextMenu = new ContextMenu();
		contextMenu.Items.Add(Tiles.Item("이름 바꾸기...", () =>
		{
			// 이름과 글자 크기를 함께 정함
			(string name, double fontSize)? result = BagNameDialog.Ask(Cfg.Name, Cfg.NameFontSize);
			if (result is var (newName, fontSize) && !string.IsNullOrWhiteSpace(newName))
			{
				Cfg.Name = newName;
				Cfg.NameFontSize = fontSize;
				Rebuild();
				app.Config.Save();
			}
		}));
		contextMenu.Items.Add(Tiles.Item("그림 바꾸기...", () =>
		{
			(string, string)? picked = BagLibraryWindow.Pick(createMode: false, Cfg.Name);
			if (picked.HasValue)
			{
				Cfg.ImagePath = picked.Value.Item1;
				Rebuild();
				app.Config.Save();
			}
		}));
		MenuItem sizeMenu = new MenuItem
		{
			Header = "크기"
		};
		(string, double)[] sizes = new (string, double)[4]
		{
			("작게", 64.0),
			("보통", 88.0),
			("크게", 120.0),
			("아주 크게", 160.0)
		};
		foreach (var (sizeName, px) in sizes)
		{
			MenuItem sizeItem = Tiles.Item(sizeName, () =>
			{
				Cfg.Size = px;
				Rebuild();
				app.Config.Save();
			});
			sizeItem.IsChecked = Math.Abs(Cfg.Size - px) < 0.5;
			sizeMenu.Items.Add(sizeItem);
		}
		contextMenu.Items.Add(sizeMenu);
		contextMenu.Items.Add(new Separator());
		contextMenu.Items.Add(Tiles.Item("가방 지우기", () =>
		{
			if (MessageBox.Show("'" + Cfg.Name + "' 가방을 지울까요?\n안에 있던 아이콘은 왼쪽 가장자리로 돌아가요. (파일은 지워지지 않아요)", "DesktopPop", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
			{
				app.DeleteBag(this);
			}
		}));
		return contextMenu;
	}

	private ImageSource LoadImage(string path)
	{
		try
		{
			FileInfo fileInfo = (string.IsNullOrEmpty(path) ? null : new FileInfo(path));
			if (fileInfo != null && fileInfo.Exists)
			{
				if (loadedImage != null && string.Equals(path, loadedImagePath, StringComparison.OrdinalIgnoreCase) && fileInfo.LastWriteTimeUtc == loadedImageStamp && fileInfo.Length == loadedImageLength)
				{
					return loadedImage;
				}
				BitmapImage bitmapImage = new BitmapImage();
				bitmapImage.BeginInit();
				bitmapImage.UriSource = new Uri(path);
				bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
				bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
				bitmapImage.DecodePixelWidth = 320;
				bitmapImage.EndInit();
				bitmapImage.Freeze();
				loadedImagePath = path;
				loadedImageStamp = fileInfo.LastWriteTimeUtc;
				loadedImageLength = fileInfo.Length;
				loadedImage = bitmapImage;
				return bitmapImage;
			}
		}
		catch (Exception ex)
		{
			App.Log(ex);
		}
		loadedImage = null;
		return fallbackImage;
	}

	private static ImageSource DefaultImage()
	{
		LinearGradientBrush brush = new LinearGradientBrush(Color.FromRgb(236, 72, 153), Color.FromRgb(124, 58, 237), 45.0);
		DrawingImage drawingImage = new DrawingImage(new DrawingGroup
		{
			Children = 
			{
				(Drawing)new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(8.0, 22.0, 84.0, 70.0), 16.0, 16.0)),
				(Drawing)new GeometryDrawing(null, new Pen(Brushes.White, 6.0), new RectangleGeometry(new Rect(32.0, 6.0, 36.0, 26.0), 10.0, 10.0)),
				(Drawing)new GeometryDrawing(new SolidColorBrush(Color.FromArgb(85, byte.MaxValue, byte.MaxValue, byte.MaxValue)), null, new RectangleGeometry(new Rect(8.0, 44.0, 84.0, 10.0)))
			}
		});
		drawingImage.Freeze();
		return drawingImage;
	}
}
