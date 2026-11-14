using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace DesktopPop;

internal sealed class BagLibraryWindow : Window
{
	private readonly bool createMode;

	private readonly WrapPanel grid = new WrapPanel();

	private readonly TextBox nameBox = new TextBox();

	private readonly Border okButton;

	private readonly Dictionary<string, Border> cards = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);

	private string? selected;

	private (string image, string name)? result;

	private static readonly Brush TextBrush = Brushes.White;

	private static Brush Soft(byte a)
	{
		return Theme.Brush(Colors.White, a);
	}

	private BagLibraryWindow(bool createMode, string currentName)
	{
		this.createMode = createMode;
		Title = (createMode ? "새 가방 만들기 — 그림 고르기" : "가방 그림 바꾸기");
		Width = 660.0;
		Height = 560.0;
		MinWidth = 420.0;
		MinHeight = 360.0;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		Topmost = true;
		Background = Theme.Brush(Theme.Shade(Theme.Panel, 0.04));
		AllowDrop = true;
		DragOver += (object s, DragEventArgs e) =>
		{
			e.Effects = (e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None);
			e.Handled = true;
		};
		Drop += (object s, DragEventArgs e) =>
		{
			if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
			{
				AddFiles(files);
			}
		};
		PreviewKeyDown += (object s, KeyEventArgs e) =>
		{
			if (e.Key == Key.Escape)
			{
				Close();
			}
			if (e.Key == Key.Return)
			{
				Confirm();
			}
		};
		DockPanel dockPanel = new DockPanel
		{
			Margin = new Thickness(16.0)
		};
		TextBlock element = new TextBlock
		{
			Text = "가방으로 쓸 그림을 골라 주세요",
			Foreground = TextBrush,
			FontSize = 15.0,
			FontWeight = FontWeights.SemiBold,
			Margin = new Thickness(4.0, 0.0, 0.0, 4.0)
		};
		TextBlock element2 = new TextBlock
		{
			Text = "두 번 클릭하면 바로 정해져요 · 그림 파일을 이 창에 끌어다 놓아도 추가돼요",
			Foreground = Soft(153),
			FontSize = 12.0,
			Margin = new Thickness(4.0, 0.0, 0.0, 10.0)
		};
		DockPanel.SetDock(element, Dock.Top);
		DockPanel.SetDock(element2, Dock.Top);
		dockPanel.Children.Add(element);
		dockPanel.Children.Add(element2);
		DockPanel dockPanel2 = new DockPanel
		{
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0),
			LastChildFill = false
		};
		DockPanel.SetDock(dockPanel2, Dock.Bottom);
		TextBlock textBlock = new TextBlock
		{
			Text = "기본 그림 다시 넣기",
			Foreground = Soft(153),
			FontSize = 12.0,
			Cursor = Cursors.Hand,
			VerticalAlignment = VerticalAlignment.Center,
			TextDecorations = TextDecorations.Underline
		};
		textBlock.MouseLeftButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			BagLibrary.WriteDefaults();
			Reload();
		};
		DockPanel.SetDock(textBlock, Dock.Left);
		dockPanel2.Children.Add(textBlock);
		Border element3 = MakeButton("취소", primary: false, Close);
		okButton = MakeButton(createMode ? "만들기" : "이 그림으로 바꾸기", primary: true, Confirm);
		DockPanel.SetDock(element3, Dock.Right);
		DockPanel.SetDock(okButton, Dock.Right);
		dockPanel2.Children.Add(element3);
		dockPanel2.Children.Add(okButton);
		if (createMode)
		{
			nameBox.Text = currentName;
			nameBox.Width = 150.0;
			nameBox.Padding = new Thickness(6.0, 4.0, 6.0, 4.0);
			nameBox.Background = Soft(34);
			nameBox.Foreground = TextBrush;
			nameBox.CaretBrush = TextBrush;
			nameBox.BorderBrush = Soft(68);
			nameBox.VerticalAlignment = VerticalAlignment.Center;
			nameBox.Margin = new Thickness(0.0, 0.0, 12.0, 0.0);
			TextBlock element4 = new TextBlock
			{
				Text = "이름",
				Foreground = Soft(204),
				VerticalAlignment = VerticalAlignment.Center,
				Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
			};
			DockPanel.SetDock(nameBox, Dock.Right);
			DockPanel.SetDock(element4, Dock.Right);
			dockPanel2.Children.Add(nameBox);
			dockPanel2.Children.Add(element4);
		}
		dockPanel.Children.Add(dockPanel2);
		ScrollViewer element5 = new ScrollViewer
		{
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			Background = Soft(10),
			Content = grid,
			Padding = new Thickness(6.0)
		};
		dockPanel.Children.Add(element5);
		Content = dockPanel;
		Reload();
		if (createMode)
		{
			Select(BagLibrary.List().FirstOrDefault());
		}
		else
		{
			UpdateOk();
		}
	}

	public static (string image, string name)? Pick(bool createMode, string currentName)
	{
		using (KeyboardHook.Suspend())
		{
			try
			{
				BagLibrary.EnsureSeeded(App.Instance.Config);
			}
			catch (Exception ex)
			{
				App.Log(ex);
			}
			BagLibraryWindow bagLibraryWindow = new BagLibraryWindow(createMode, currentName);
			bagLibraryWindow.ShowDialog();
			return bagLibraryWindow.result;
		}
	}

	private void Reload()
	{
		grid.Children.Clear();
		cards.Clear();
		grid.Children.Add(MakeAddCard());
		foreach (string item in BagLibrary.List())
		{
			grid.Children.Add(MakeCard(item));
		}
		if (selected != null && cards.TryGetValue(selected, out Border value))
		{
			value.BorderBrush = Theme.Brush(Theme.Accent);
		}
		else
		{
			selected = null;
		}
		UpdateOk();
	}

	private Border MakeCard(string path)
	{
		Image image = new Image
		{
			Width = 72.0,
			Height = 72.0,
			Stretch = Stretch.Uniform,
			Margin = new Thickness(0.0, 10.0, 0.0, 4.0),
			Source = Load(path)
		};
		RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
		TextBlock element = new TextBlock
		{
			Text = BagLibrary.DisplayName(path),
			Foreground = Soft(221),
			FontSize = 11.0,
			TextTrimming = TextTrimming.CharacterEllipsis,
			TextAlignment = TextAlignment.Center,
			Margin = new Thickness(6.0, 0.0, 6.0, 0.0)
		};
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(image);
		stackPanel.Children.Add(element);
		Border del = new Border
		{
			Width = 22.0,
			Height = 22.0,
			CornerRadius = new CornerRadius(11.0),
			Background = Theme.Brush(Color.FromRgb(229, 72, 77)),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Top,
			Margin = new Thickness(0.0, 4.0, 4.0, 0.0),
			Visibility = Visibility.Collapsed,
			Cursor = Cursors.Hand,
			ToolTip = "보관함에서 지우기",
			Child = new TextBlock
			{
				Text = "✕",
				Foreground = Brushes.White,
				FontSize = 11.0,
				FontWeight = FontWeights.Bold,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			}
		};
		del.MouseLeftButtonDown += (object s, MouseButtonEventArgs e) =>
		{
			e.Handled = true;
		};
		del.MouseLeftButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			e.Handled = true;
			if (MessageBox.Show(this, "'" + BagLibrary.DisplayName(path) + "' 그림을 보관함에서 지울까요?\n(이미 이 그림을 쓰는 가방은 그대로예요)", "가방 그림 보관함", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
			{
				try
				{
					File.Delete(path);
				}
				catch (Exception ex)
				{
					MessageBox.Show(this, ex.Message, "가방 그림 보관함");
				}
				if (string.Equals(selected, path, StringComparison.OrdinalIgnoreCase))
				{
					selected = null;
				}
				Reload();
			}
		};
		Grid grid = new Grid();
		grid.Children.Add(stackPanel);
		grid.Children.Add(del);
		Border card = new Border
		{
			Width = 106.0,
			Height = 120.0,
			Margin = new Thickness(6.0),
			CornerRadius = new CornerRadius(12.0),
			Background = Soft(20),
			BorderThickness = new Thickness(2.0),
			BorderBrush = Brushes.Transparent,
			Cursor = Cursors.Hand,
			Child = grid,
			ToolTip = BagLibrary.DisplayName(path)
		};
		card.MouseEnter += (object s, MouseEventArgs e) =>
		{
			card.Background = Soft(42);
			del.Visibility = Visibility.Visible;
		};
		card.MouseLeave += (object s, MouseEventArgs e) =>
		{
			card.Background = Soft(20);
			del.Visibility = Visibility.Collapsed;
		};
		card.MouseLeftButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			Select(path);
		};
		card.MouseLeftButtonDown += (object s, MouseButtonEventArgs e) =>
		{
			if (e.ClickCount == 2)
			{
				Select(path);
				Confirm();
				e.Handled = true;
			}
		};
		cards[path] = card;
		return card;
	}

	private Border MakeAddCard()
	{
		Rectangle element = new Rectangle
		{
			RadiusX = 12.0,
			RadiusY = 12.0,
			Stroke = Theme.Brush(Theme.Accent, 170),
			StrokeThickness = 2.0,
			StrokeDashArray = new DoubleCollection { 4.0, 3.0 }
		};
		TextBlock element2 = new TextBlock
		{
			Text = "＋\n그림 추가",
			Foreground = Theme.Brush(Theme.Accent),
			FontSize = 13.0,
			FontWeight = FontWeights.SemiBold,
			TextAlignment = TextAlignment.Center,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid grid = new Grid();
		grid.Children.Add(element);
		grid.Children.Add(element2);
		Border card = new Border
		{
			Width = 106.0,
			Height = 120.0,
			Margin = new Thickness(6.0),
			CornerRadius = new CornerRadius(12.0),
			Background = Soft(8),
			Cursor = Cursors.Hand,
			Child = grid,
			ToolTip = "그림 파일 고르기 (여러 장 한 번에 가능)"
		};
		card.MouseEnter += (object s, MouseEventArgs e) =>
		{
			card.Background = Theme.Brush(Theme.Accent, 34);
		};
		card.MouseLeave += (object s, MouseEventArgs e) =>
		{
			card.Background = Soft(8);
		};
		card.MouseLeftButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = "보관함에 넣을 그림을 골라 주세요 (여러 장 가능)",
				Filter = "그림 파일|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.ico;*.tif;*.tiff|모든 파일|*.*",
				Multiselect = true
			};
			if (openFileDialog.ShowDialog(this) == true)
			{
				AddFiles(openFileDialog.FileNames);
			}
		};
		return card;
	}

	private void AddFiles(string[] files)
	{
		List<string> list = BagLibrary.Add(files);
		if (list.Count != 0)
		{
			Reload();
			Select(list[list.Count - 1]);
			if (cards.TryGetValue(list[list.Count - 1], out Border value))
			{
				value.BringIntoView();
			}
		}
	}

	private void Select(string? path)
	{
		if (selected != null && cards.TryGetValue(selected, out Border value))
		{
			value.BorderBrush = Brushes.Transparent;
		}
		selected = path;
		if (path != null && cards.TryGetValue(path, out Border value2))
		{
			value2.BorderBrush = Theme.Brush(Theme.Accent);
		}
		UpdateOk();
	}

	private void UpdateOk()
	{
		okButton.Opacity = ((selected != null) ? 1.0 : 0.4);
		okButton.IsHitTestVisible = selected != null;
	}

	private void Confirm()
	{
		if (selected == null || !File.Exists(selected))
		{
			return;
		}
		try
		{
			string item = BagLibrary.UseForBag(selected);
			string text = (createMode ? nameBox.Text.Trim() : "");
			result = (item, string.IsNullOrEmpty(text) ? "가방" : text);
			Close();
		}
		catch (Exception ex)
		{
			MessageBox.Show(this, ex.Message, "가방 그림 보관함");
		}
	}

	private static Border MakeButton(string text, bool primary, Action onClick)
	{
		Brush idle = (primary ? Theme.Brush(Theme.Accent) : Soft(34));
		Brush hover = (primary ? Theme.Brush(Theme.Shade(Theme.Accent, 0.15)) : Soft(56));
		Border b = new Border
		{
			Background = idle,
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(16.0, 7.0, 16.0, 7.0),
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
			Cursor = Cursors.Hand,
			Child = new TextBlock
			{
				Text = text,
				FontWeight = FontWeights.SemiBold,
				FontSize = 13.0,
				Foreground = (primary ? Theme.Brush(Theme.Shade(Theme.Panel, -0.3)) : Brushes.White)
			}
		};
		b.MouseEnter += (object s, MouseEventArgs e) =>
		{
			b.Background = hover;
		};
		b.MouseLeave += (object s, MouseEventArgs e) =>
		{
			b.Background = idle;
		};
		b.MouseLeftButtonUp += (object s, MouseButtonEventArgs e) =>
		{
			onClick();
		};
		return b;
	}

	private static ImageSource? Load(string path)
	{
		try
		{
			BitmapImage bitmapImage = new BitmapImage();
			bitmapImage.BeginInit();
			bitmapImage.UriSource = new Uri(path);
			bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
			bitmapImage.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
			bitmapImage.DecodePixelWidth = 144;
			bitmapImage.EndInit();
			bitmapImage.Freeze();
			return bitmapImage;
		}
		catch
		{
			return null;
		}
	}
}
