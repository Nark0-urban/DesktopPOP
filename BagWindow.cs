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
			nint num = DesktopShell.FindProgman();
			if (num != IntPtr.Zero)
			{
				new WindowInteropHelper(this).Owner = num;
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
			Point? point = downPt;
			if (point.HasValue)
			{
				Point valueOrDefault = point.GetValueOrDefault();
				if (e.LeftButton == MouseButtonState.Pressed)
				{
					Point position = e.GetPosition(this);
					if (!(Math.Abs(position.X - valueOrDefault.X) + Math.Abs(position.Y - valueOrDefault.Y) < 6.0))
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
			App instance = App.Instance;
			string[] array = Tiles.KeysFrom(e.Data);
			if (array != null)
			{
				instance.MoveManyToBag(array, Cfg);
			}
			else if (e.Data.GetData(DataFormats.FileDrop) is string[] keys)
			{
				instance.MoveManyToBag(keys, Cfg);
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
		bool flag = open;
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
		if ((flag || IsForcedOpen) && ringTiles.Count > 0)
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
		Point point = BagLayout.ClampCenter(CenterDip, Cfg.Size, WorkAreaAtCenter());
		Cfg.X = point.X;
		Cfg.Y = point.Y;
		layoutWorkArea = WorkAreaAtCenter();
		Color c = Theme.Halo;
		RadialGradientBrush radialGradientBrush = new RadialGradientBrush();
		radialGradientBrush.GradientStops.Add(new GradientStop(Theme.WithAlpha(c, 154), 0.0));
		radialGradientBrush.GradientStops.Add(new GradientStop(Theme.WithAlpha(c, 112), 0.75));
		radialGradientBrush.GradientStops.Add(new GradientStop(Theme.WithAlpha(c, 0), 1.0));
		radialGradientBrush.Freeze();
		halo.Fill = radialGradientBrush;
		badge.Background = Theme.Brush(Theme.Badge);
		hintBox.Background = Theme.Brush(Theme.Hint, 230);
		double size = Cfg.Size;
		double num = size + 24.0;
		double num2 = Math.Max(num, 120.0);
		bagVisual.Width = num2;
		bagVisual.Height = num + 24.0;
		bagVisual.RenderTransformOrigin = new Point(0.5, num / 2.0 / (num + 24.0));
		hit.Width = num;
		hit.Height = num;
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
		badge.Margin = new Thickness(0.0, 2.0, (num2 - num) / 2.0 + 2.0, 0.0);
		List<DesktopItem> list = Items();
		badgeText.Text = list.Count.ToString();
		badge.Visibility = ((list.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
		List<Point> list2 = (ringPoints = ComputeRing(list.Count));
		double num3 = (ringRadius = ((list2.Count == 0) ? 0.0 : list2.Max((Point p) => Math.Sqrt(p.X * p.X + p.Y * p.Y))));
		double num4 = Math.Max(size + 260.0, 2.0 * (num3 + 86.0));
		center = num4 / 2.0;
		Width = num4;
		Height = num4;
		Left = Cfg.X - center;
		Top = Cfg.Y - center;
		Canvas.SetLeft(bagVisual, center - num2 / 2.0);
		Canvas.SetTop(bagVisual, center - num / 2.0);
		double num5 = num3 + 73.1;
		Ellipse ellipse = halo;
		double width = (halo.Height = 2.0 * num5);
		ellipse.Width = width;
		Canvas.SetLeft(halo, center - num5);
		Canvas.SetTop(halo, center - num5);
		Canvas.SetLeft(hintBox, center - hintBox.Width / 2.0);
		Canvas.SetTop(hintBox, center - num / 2.0 - 72.0);
		PrepareRingTiles(list);
	}

	private List<Point> ComputeRing(int n)
	{
		if (n == 0)
		{
			return new List<Point>();
		}
		return BagLayout.ComputeRing(n, Cfg.Size, CenterDip, layoutWorkArea, 86.0, 98.0);
	}

	private Rect WorkAreaAtCenter()
	{
		double num = Native.DipScale();
		return Native.ToDipRect(Native.MonitorAt(new Native.POINT
		{
			X = (int)Math.Round(Cfg.X * num),
			Y = (int)Math.Round(Cfg.Y * num)
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
		for (int num = 0; num < items.Count; num++)
		{
			DesktopItem item = items[num];
			Border border = Tiles.Create(item, backdrop: true);
			ScaleTransform value = new ScaleTransform(0.2, 0.2);
			TranslateTransform value2 = new TranslateTransform();
			TransformGroup transformGroup = new TransformGroup();
			transformGroup.Children.Add(value);
			transformGroup.Children.Add(value2);
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
				double num2 = Native.DipScale();
				Point point = new Point((double)p.X / num2, (double)p.Y / num2);
				Side side = ((point.X >= Cfg.X) ? Side.Right : Side.Left);
				App.Instance.TakeOutManyToSide(keys, Cfg, side, point);
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
		double num = Native.DipScale();
		double num2 = (double)p.X / num - Cfg.X;
		double num3 = (double)p.Y / num - Cfg.Y;
		return Math.Sqrt(num2 * num2 + num3 * num3) > ringRadius + 51.6;
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
		double x = (translateTransform.Y = 0.0);
		translateTransform.X = x;
		x = (scaleTransform.ScaleY = 0.2);
		scaleTransform.ScaleX = x;
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
		string text = ((targets.Count > 1) ? $" ({targets.Count}개)" : "");
		List<MenuEntry> list = new List<MenuEntry> { MenuEntry.Item("가방에서 꺼내기" + text, () =>
		{
			app.TakeOutManyToSide(targets, Cfg, Side.Left, CenterDip);
		}) };
		List<BagConfig> list2 = app.Config.Bags.Where((BagConfig b) => b != Cfg).ToList();
		if (list2.Count > 0)
		{
			list.Add(MenuEntry.Sub("다른 가방으로 옮기기" + text, list2.Select((BagConfig b) => MenuEntry.Item(b.Name, () =>
			{
				app.MoveManyToBag(targets, b);
			})).ToList()));
		}
		return list;
	}

	private ContextMenu BagMenu()
	{
		App app = App.Instance;
		ContextMenu contextMenu = new ContextMenu();
		contextMenu.Items.Add(Tiles.Item("이름 바꾸기...", () =>
		{
			string text = InputDialog.Ask("가방 이름", "새 이름을 입력해 주세요", Cfg.Name);
			if (!string.IsNullOrWhiteSpace(text))
			{
				Cfg.Name = text;
				Rebuild();
				app.Config.Save();
			}
		}));
		contextMenu.Items.Add(Tiles.Item("그림 바꾸기...", () =>
		{
			(string, string)? tuple2 = BagLibraryWindow.Pick(createMode: false, Cfg.Name);
			if (tuple2.HasValue)
			{
				Cfg.ImagePath = tuple2.Value.Item1;
				Rebuild();
				app.Config.Save();
			}
		}));
		MenuItem menuItem = new MenuItem
		{
			Header = "크기"
		};
		(string, double)[] array = new (string, double)[4]
		{
			("작게", 64.0),
			("보통", 88.0),
			("크게", 120.0),
			("아주 크게", 160.0)
		};
		for (int num = 0; num < array.Length; num++)
		{
			(string, double) tuple = array[num];
			string item = tuple.Item1;
			double px = tuple.Item2;
			MenuItem menuItem2 = Tiles.Item(item, () =>
			{
				Cfg.Size = px;
				Rebuild();
				app.Config.Save();
			});
			menuItem2.IsChecked = Math.Abs(Cfg.Size - px) < 0.5;
			menuItem.Items.Add(menuItem2);
		}
		contextMenu.Items.Add(menuItem);
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
