using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopPop;

/// <summary>
/// 단축키 직접 정하기: 창이 떠 있는 동안 원하는 키 조합을 누르면 그대로 잡아서 보여 줌.
/// </summary>
internal sealed class HotkeyDialog : Window
{
	private readonly TextBlock shown;

	private readonly TextBlock hint;

	private readonly Button save;

	private string? captured;

	private HotkeyDialog(string current)
	{
		Title = "펼치기/접기 단축키";
		Width = 360.0;
		SizeToContent = SizeToContent.Height;
		ResizeMode = ResizeMode.NoResize;
		WindowStartupLocation = WindowStartupLocation.CenterScreen;
		Topmost = true;
		StackPanel root = new StackPanel
		{
			Margin = new Thickness(16.0)
		};
		root.Children.Add(new TextBlock
		{
			Text = "쓰고 싶은 키 조합을 지금 눌러 주세요.",
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0)
		});
		root.Children.Add(new TextBlock
		{
			Text = "Ctrl, Alt, Win 중 하나는 같이 눌러야 해요. (예: Ctrl+Alt+K, Alt+F2)",
			Foreground = Brushes.Gray,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
		});
		shown = new TextBlock
		{
			Text = string.IsNullOrEmpty(current) ? "(없음)" : current,
			FontSize = 20.0,
			FontWeight = FontWeights.SemiBold,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
		Border box = new Border
		{
			BorderBrush = Brushes.LightGray,
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(4.0),
			Padding = new Thickness(8.0),
			Child = shown
		};
		root.Children.Add(box);
		hint = new TextBlock
		{
			Text = "",
			Foreground = Brushes.IndianRed,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0)
		};
		root.Children.Add(hint);
		StackPanel buttons = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			HorizontalAlignment = HorizontalAlignment.Right,
			Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
		};
		save = new Button
		{
			Content = "저장",
			Width = 72.0,
			IsEnabled = false,
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
			Focusable = false
		};
		save.Click += (object s, RoutedEventArgs e) =>
		{
			DialogResult = true;
		};
		Button cancel = new Button
		{
			Content = "취소",
			Width = 72.0,
			Focusable = false
		};
		cancel.Click += (object s, RoutedEventArgs e) =>
		{
			DialogResult = false;
		};
		buttons.Children.Add(save);
		buttons.Children.Add(cancel);
		root.Children.Add(buttons);
		Content = root;
		PreviewKeyDown += OnKey;
	}

	private void OnKey(object sender, KeyEventArgs e)
	{
		e.Handled = true; // Alt 누를 때 메뉴로 가거나, Enter·Esc 가 버튼을 누르지 않게 키는 전부 여기서 받음
		Key key = e.Key switch
		{
			Key.System => e.SystemKey,
			Key.ImeProcessed => e.ImeProcessedKey,
			_ => e.Key
		};
		bool win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
		ModifierKeys mods = Keyboard.Modifiers;
		if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
		{
			// 보조키만 누른 중: 지금까지 누른 것만 보여 줌
			shown.Text = ((mods & ModifierKeys.Control) != 0 ? "Ctrl+" : "") + ((mods & ModifierKeys.Alt) != 0 ? "Alt+" : "") + ((mods & ModifierKeys.Shift) != 0 ? "Shift+" : "") + (win ? "Win+" : "") + "…";
			return;
		}
		if (key == Key.Escape && mods == ModifierKeys.None && !win)
		{
			DialogResult = false; // 보조키 없이 Esc = 취소
			return;
		}
		string? combo = GlobalHotkey.Format(mods, win, key);
		if (combo == null)
		{
			captured = null;
			save.IsEnabled = false;
			shown.Text = key.ToString();
			hint.Text = "Ctrl, Alt, Win 중 하나와 같이 눌러 주세요. (Shift 는 F1~F24 같은 기능키와만 돼요)";
			return;
		}
		captured = combo;
		shown.Text = combo;
		hint.Text = "";
		save.IsEnabled = true;
	}

	/// <summary>새 단축키를 받음. 취소하면 null.</summary>
	public static string? Ask(string current)
	{
		using (KeyboardHook.Suspend())
		{
			HotkeyDialog dialog = new HotkeyDialog(current);
			return (dialog.ShowDialog() == true) ? dialog.captured : null;
		}
	}
}
