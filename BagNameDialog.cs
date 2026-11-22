using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopPop;

/// <summary>가방 이름 바꾸기 창: 이름과 그 아래 글자 크기를 고르고, 실제 가방 이름처럼 미리 보여 줌.</summary>
internal sealed class BagNameDialog : Window
{
	/// <summary>고를 수 있는 글자 크기 (이름, 크기).</summary>
	public static readonly (string label, double size)[] Sizes = new (string, double)[5]
	{
		("작게", 12.5),
		("보통", 16.0),
		("크게", 20.0),
		("아주 크게", 26.0),
		("특대", 32.0)
	};

	private readonly TextBox nameBox;

	private readonly TextBlock preview;

	private readonly List<RadioButton> sizeButtons = new List<RadioButton>();

	private double selectedSize;

	private BagNameDialog(string currentName, double currentSize)
	{
		Title = "가방 이름";
		Width = 380.0;
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		Topmost = true;
		StackPanel layout = new StackPanel
		{
			Margin = new Thickness(16.0)
		};
		layout.Children.Add(new TextBlock
		{
			Text = "새 이름을 입력해 주세요",
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		nameBox = new TextBox
		{
			Text = currentName,
			Padding = new Thickness(4.0)
		};
		nameBox.TextChanged += (object s, TextChangedEventArgs e) => UpdatePreview();
		layout.Children.Add(nameBox);
		layout.Children.Add(new TextBlock
		{
			Text = "글자 크기",
			Margin = new Thickness(0.0, 14.0, 0.0, 6.0)
		});
		// 지금 크기와 가장 가까운 것을 골라 둠
		selectedSize = Sizes.OrderBy(((string label, double size) s) => Math.Abs(s.size - currentSize)).First().size;
		WrapPanel sizeRow = new WrapPanel();
		foreach (var (sizeLabel, size) in Sizes)
		{
			RadioButton option = new RadioButton
			{
				Content = sizeLabel,
				GroupName = "nameSize",
				IsChecked = size == selectedSize,
				Margin = new Thickness(0.0, 0.0, 14.0, 4.0),
				Tag = size
			};
			option.Checked += (object s, RoutedEventArgs e) =>
			{
				selectedSize = (double)option.Tag;
				UpdatePreview();
			};
			sizeButtons.Add(option);
			sizeRow.Children.Add(option);
		}
		layout.Children.Add(sizeRow);
		// 미리보기: 가방 이름처럼 어두운 바탕에 흰 글자 + 그림자
		preview = new TextBlock
		{
			Foreground = Brushes.White,
			FontWeight = FontWeights.SemiBold,
			TextAlignment = TextAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis,
			Effect = new DropShadowEffect
			{
				ShadowDepth = 1.0,
				BlurRadius = 4.0,
				Opacity = 0.95,
				Color = Colors.Black
			}
		};
		if (UiFont.BagNameFamily is FontFamily bagNameFont)
		{
			preview.FontFamily = bagNameFont;
		}
		layout.Children.Add(new Border
		{
			Background = Theme.Brush(Theme.Panel),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(10.0, 12.0, 10.0, 12.0),
			Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
			MinHeight = 70.0,
			Child = preview
		});
		StackPanel buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 14.0, 0.0, 0.0)
		};
		Button ok = new Button
		{
			Content = "확인",
			Width = 72.0,
			IsDefault = true,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		ok.Click += (object s, RoutedEventArgs e) =>
		{
			DialogResult = true;
		};
		buttons.Children.Add(ok);
		buttons.Children.Add(new Button
		{
			Content = "취소",
			Width = 72.0,
			IsCancel = true
		});
		layout.Children.Add(buttons);
		Content = layout;
		UpdatePreview();
		Loaded += (object s, RoutedEventArgs e) =>
		{
			nameBox.Focus();
			nameBox.SelectAll();
		};
	}

	private void UpdatePreview()
	{
		preview.Text = string.IsNullOrWhiteSpace(nameBox.Text) ? "가방" : nameBox.Text.Trim();
		preview.FontSize = selectedSize;
	}

	/// <summary>(새 이름, 글자 크기). 취소하면 null.</summary>
	public static (string name, double fontSize)? Ask(string currentName, double currentSize)
	{
		using (KeyboardHook.Suspend())
		{
			BagNameDialog dialog = new BagNameDialog(currentName, currentSize);
			if (dialog.ShowDialog() != true)
			{
				return null;
			}
			return (dialog.nameBox.Text.Trim(), dialog.selectedSize);
		}
	}
}
