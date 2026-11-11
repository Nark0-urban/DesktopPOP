using System.Windows;
using System.Windows.Controls;

namespace DesktopPop;

internal sealed class InputDialog : Window
{
    readonly TextBox box;

    InputDialog(string title, string prompt, string initial)
    {
        Title = title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
        box = new TextBox { Text = initial, Padding = new Thickness(4) };
        sp.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "확인", Width = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (s, e) => DialogResult = true;
        var cancel = new Button { Content = "취소", Width = 72, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        sp.Children.Add(buttons);
        Content = sp;
        Loaded += (s, e) => { box.Focus(); box.SelectAll(); };
    }

    public static string? Ask(string title, string prompt, string initial)
    {
        var d = new InputDialog(title, prompt, initial);
        return d.ShowDialog() == true ? d.box.Text.Trim() : null;
    }
}
