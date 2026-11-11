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

namespace DesktopPop;

/// <summary>
/// 가방 그림 보관함 창. 새 가방 만들기 / 그림 바꾸기 때 열림.
/// 그림 클릭 = 고르기, 두 번 클릭 = 바로 확정, ＋ = 추가(여러 장), ✕ = 보관함에서 지우기, 파일 끌어다 놓기 = 추가.
/// </summary>
internal sealed class BagLibraryWindow : Window
{
    readonly bool createMode;
    readonly WrapPanel grid = new();
    readonly TextBox nameBox = new();
    readonly Border okButton;
    readonly Dictionary<string, Border> cards = new(StringComparer.OrdinalIgnoreCase);
    string? selected;
    (string image, string name)? result;

    static readonly Brush TextBrush = Brushes.White;
    static Brush Soft(byte a) => Theme.Brush(Colors.White, a);

    BagLibraryWindow(bool createMode, string currentName)
    {
        this.createMode = createMode;
        Title = createMode ? "새 가방 만들기 — 그림 고르기" : "가방 그림 바꾸기";
        Width = 660; Height = 560; MinWidth = 420; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Background = Theme.Brush(Theme.Shade(Theme.Panel, 0.04));
        AllowDrop = true;
        DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (s, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
        };
        PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) Close();
            if (e.Key == Key.Enter) Confirm();
        };

        var root = new DockPanel { Margin = new Thickness(16) };

        var header = new TextBlock
        {
            Text = "가방으로 쓸 그림을 골라 주세요",
            Foreground = TextBrush, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(4, 0, 0, 4)
        };
        var sub = new TextBlock
        {
            Text = "두 번 클릭하면 바로 정해져요 · 그림 파일을 이 창에 끌어다 놓아도 추가돼요",
            Foreground = Soft(0x99), FontSize = 12, Margin = new Thickness(4, 0, 0, 10)
        };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(sub, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(sub);

        // 아래 버튼 줄
        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var restore = new TextBlock
        {
            Text = "기본 그림 다시 넣기", Foreground = Soft(0x99), FontSize = 12, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center, TextDecorations = TextDecorations.Underline
        };
        restore.MouseLeftButtonUp += (s, e) => { BagLibrary.WriteDefaults(); Reload(); };
        DockPanel.SetDock(restore, Dock.Left);
        bottom.Children.Add(restore);

        var cancel = MakeButton("취소", false, Close);
        okButton = MakeButton(createMode ? "만들기" : "이 그림으로 바꾸기", true, Confirm);
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(okButton, Dock.Right);
        bottom.Children.Add(cancel);
        bottom.Children.Add(okButton);
        if (createMode)
        {
            nameBox.Text = currentName;
            nameBox.Width = 150;
            nameBox.Padding = new Thickness(6, 4, 6, 4);
            nameBox.Background = Soft(0x22);
            nameBox.Foreground = TextBrush;
            nameBox.CaretBrush = TextBrush;
            nameBox.BorderBrush = Soft(0x44);
            nameBox.VerticalAlignment = VerticalAlignment.Center;
            nameBox.Margin = new Thickness(0, 0, 12, 0);
            var label = new TextBlock { Text = "이름", Foreground = Soft(0xCC), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            DockPanel.SetDock(nameBox, Dock.Right);
            DockPanel.SetDock(label, Dock.Right);
            bottom.Children.Add(nameBox);
            bottom.Children.Add(label);
        }
        root.Children.Add(bottom);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Soft(0x0A),
            Content = grid,
            Padding = new Thickness(6)
        };
        root.Children.Add(scroll);
        Content = root;

        Reload();
        // 새 가방이면 첫 그림을 미리 골라 둠 → 바로 "만들기"만 눌러도 됨
        if (createMode) Select(BagLibrary.List().FirstOrDefault());
        else UpdateOk();
    }

    /// <summary>창을 열고 고른 결과를 돌려줌 (취소하면 null). image는 가방 전용으로 복사된 경로.</summary>
    public static (string image, string name)? Pick(bool createMode, string currentName)
    {
        try { BagLibrary.EnsureSeeded(App.Instance.Config); } catch (Exception ex) { App.Log(ex); }
        var w = new BagLibraryWindow(createMode, currentName);
        w.ShowDialog();
        return w.result;
    }

    void Reload()
    {
        grid.Children.Clear();
        cards.Clear();
        grid.Children.Add(MakeAddCard());
        foreach (var path in BagLibrary.List())
            grid.Children.Add(MakeCard(path));
        if (selected != null && cards.TryGetValue(selected, out var c)) c.BorderBrush = Theme.Brush(Theme.Accent);
        else selected = null;
        UpdateOk();
    }

    Border MakeCard(string path)
    {
        var img = new Image { Width = 72, Height = 72, Stretch = Stretch.Uniform, Margin = new Thickness(0, 10, 0, 4), Source = Load(path) };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        var name = new TextBlock
        {
            Text = BagLibrary.DisplayName(path), Foreground = Soft(0xDD), FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(6, 0, 6, 0)
        };
        var stack = new StackPanel();
        stack.Children.Add(img);
        stack.Children.Add(name);

        var del = new Border
        {
            Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Theme.Brush(Color.FromRgb(0xE5, 0x48, 0x4D)),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0),
            Visibility = Visibility.Collapsed, Cursor = Cursors.Hand, ToolTip = "보관함에서 지우기",
            Child = new TextBlock { Text = "✕", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        del.MouseLeftButtonDown += (s, e) => e.Handled = true;
        del.MouseLeftButtonUp += (s, e) =>
        {
            e.Handled = true;
            var r = MessageBox.Show(this, $"'{BagLibrary.DisplayName(path)}' 그림을 보관함에서 지울까요?\n(이미 이 그림을 쓰는 가방은 그대로예요)",
                "가방 그림 보관함", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
            try { File.Delete(path); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "가방 그림 보관함"); }
            if (string.Equals(selected, path, StringComparison.OrdinalIgnoreCase)) selected = null;
            Reload();
        };

        var cell = new Grid();
        cell.Children.Add(stack);
        cell.Children.Add(del);
        var card = new Border
        {
            Width = 106, Height = 120, Margin = new Thickness(6), CornerRadius = new CornerRadius(12),
            Background = Soft(0x14), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent,
            Cursor = Cursors.Hand, Child = cell, ToolTip = BagLibrary.DisplayName(path)
        };
        card.MouseEnter += (s, e) => { card.Background = Soft(0x2A); del.Visibility = Visibility.Visible; };
        card.MouseLeave += (s, e) => { card.Background = Soft(0x14); del.Visibility = Visibility.Collapsed; };
        card.MouseLeftButtonUp += (s, e) => Select(path);
        card.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount == 2) { Select(path); Confirm(); e.Handled = true; }
        };
        cards[path] = card;
        return card;
    }

    Border MakeAddCard()
    {
        var dash = new Rectangle
        {
            RadiusX = 12, RadiusY = 12, Stroke = Theme.Brush(Theme.Accent, 0xAA), StrokeThickness = 2,
            StrokeDashArray = new DoubleCollection { 4, 3 }
        };
        var text = new TextBlock
        {
            Text = "＋\n그림 추가", Foreground = Theme.Brush(Theme.Accent), FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        var cell = new Grid();
        cell.Children.Add(dash);
        cell.Children.Add(text);
        var card = new Border
        {
            Width = 106, Height = 120, Margin = new Thickness(6), CornerRadius = new CornerRadius(12),
            Background = Soft(0x08), Cursor = Cursors.Hand, Child = cell, ToolTip = "그림 파일 고르기 (여러 장 한 번에 가능)"
        };
        card.MouseEnter += (s, e) => card.Background = Theme.Brush(Theme.Accent, 0x22);
        card.MouseLeave += (s, e) => card.Background = Soft(0x08);
        card.MouseLeftButtonUp += (s, e) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "보관함에 넣을 그림을 골라 주세요 (여러 장 가능)",
                Filter = "그림 파일|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.ico;*.tif;*.tiff|모든 파일|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) == true) AddFiles(dlg.FileNames);
        };
        return card;
    }

    void AddFiles(string[] files)
    {
        var added = BagLibrary.Add(files);
        if (added.Count == 0) return;
        Reload();
        Select(added[^1]); // 마지막으로 넣은 그림을 골라 둠
        if (cards.TryGetValue(added[^1], out var c)) c.BringIntoView();
    }

    void Select(string? path)
    {
        if (selected != null && cards.TryGetValue(selected, out var old)) old.BorderBrush = Brushes.Transparent;
        selected = path;
        if (path != null && cards.TryGetValue(path, out var c)) c.BorderBrush = Theme.Brush(Theme.Accent);
        UpdateOk();
    }

    void UpdateOk()
    {
        okButton.Opacity = selected != null ? 1 : 0.4;
        okButton.IsHitTestVisible = selected != null;
    }

    void Confirm()
    {
        if (selected == null || !File.Exists(selected)) return;
        try
        {
            var image = BagLibrary.UseForBag(selected);
            var name = createMode ? nameBox.Text.Trim() : "";
            result = (image, string.IsNullOrEmpty(name) ? "가방" : name);
            Close();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "가방 그림 보관함"); }
    }

    static Border MakeButton(string text, bool primary, Action onClick)
    {
        var idle = primary ? Theme.Brush(Theme.Accent) : Soft(0x22);
        var hover = primary ? Theme.Brush(Theme.Shade(Theme.Accent, 0.15)) : Soft(0x38);
        var b = new Border
        {
            Background = idle, CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 7, 16, 7),
            Margin = new Thickness(8, 0, 0, 0), Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13,
                Foreground = primary ? Theme.Brush(Theme.Shade(Theme.Panel, -0.3)) : Brushes.White
            }
        };
        b.MouseEnter += (s, e) => b.Background = hover;
        b.MouseLeave += (s, e) => b.Background = idle;
        b.MouseLeftButtonUp += (s, e) => onClick();
        return b;
    }

    static ImageSource? Load(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path);
            bi.CacheOption = BitmapCacheOption.OnLoad;      // 파일을 잡고 있지 않게 (지우기 가능)
            bi.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bi.DecodePixelWidth = 144;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }
}
