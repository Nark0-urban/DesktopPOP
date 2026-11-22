using System.Windows;
using System.Windows.Controls;

namespace DesktopPop;

/// <summary>이름을 입력받는 작은 창 (새 폴더, 이름 바꾸기, 가방 이름).</summary>
internal sealed class InputDialog : Window
{
	private readonly TextBox box;

	private InputDialog(string title, string prompt, string initial)
	{
		Title = title;
		Width = 340.0;
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
			Text = prompt,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		box = new TextBox
		{
			Text = initial,
			Padding = new Thickness(4.0)
		};
		layout.Children.Add(box);
		StackPanel buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		Button button = new Button
		{
			Content = "확인",
			Width = 72.0,
			IsDefault = true,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		button.Click += (object s, RoutedEventArgs e) =>
		{
			DialogResult = true;
		};
		Button element = new Button
		{
			Content = "취소",
			Width = 72.0,
			IsCancel = true
		};
		buttons.Children.Add(button);
		buttons.Children.Add(element);
		layout.Children.Add(buttons);
		Content = layout;
		Loaded += (object s, RoutedEventArgs e) =>
		{
			box.Focus();
			box.SelectAll();
		};
	}

	public static string? Ask(string title, string prompt, string initial)
	{
		using (KeyboardHook.Suspend())
		{
			InputDialog inputDialog = new InputDialog(title, prompt, initial);
			return (inputDialog.ShowDialog() == true) ? inputDialog.box.Text.Trim() : null;
		}
	}
}
