using System.Windows;
using System.Windows.Controls;

namespace DesktopPop;

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
		StackPanel stackPanel = new StackPanel
		{
			Margin = new Thickness(16.0)
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = prompt,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		});
		box = new TextBox
		{
			Text = initial,
			Padding = new Thickness(4.0)
		};
		stackPanel.Children.Add(box);
		StackPanel stackPanel2 = new StackPanel
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
		stackPanel2.Children.Add(button);
		stackPanel2.Children.Add(element);
		stackPanel.Children.Add(stackPanel2);
		Content = stackPanel;
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
