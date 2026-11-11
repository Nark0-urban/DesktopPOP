using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace DesktopPop;

internal static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "DesktopPop";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(Name) != null;
        }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (k == null) return;
        if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(Name, false);
    }
}

/// <summary>작업 표시줄 오른쪽 아래(시계 옆) 아이콘과 메뉴.</summary>
internal sealed class Tray : IDisposable
{
    readonly WF.NotifyIcon icon;

    public Tray(App app)
    {
        var menu = new WF.ContextMenuStrip();
        menu.Items.Add("새 가방 만들기...", null, (s, e) => app.CreateBag(null));
        menu.Items.Add("바탕화면에 새 폴더 만들기...", null, (s, e) => app.CreateNewFolder(Side.Left, null));
        menu.Items.Add("바탕화면 폴더 열기", null, (s, e) => app.OpenDesktopFolder());
        menu.Items.Add(new WF.ToolStripSeparator());

        var hide = new WF.ToolStripMenuItem("원래 바탕화면 아이콘 숨기기") { Checked = app.Config.HideDesktopIcons, CheckOnClick = true };
        hide.Click += (s, e) => app.SetHideDesktopIcons(hide.Checked);
        menu.Items.Add(hide);

        var full = new WF.ToolStripMenuItem("게임·전체화면일 땐 안 튀어나오기") { Checked = app.Config.DisableInFullscreen, CheckOnClick = true };
        full.Click += (s, e) => { app.Config.DisableInFullscreen = full.Checked; app.Config.Save(); };
        menu.Items.Add(full);

        var max = new WF.ToolStripMenuItem("다른 창이 최대화돼 있을 땐 안 튀어나오기") { Checked = app.Config.BlockWhenMaximized, CheckOnClick = true };
        max.Click += (s, e) => { app.Config.BlockWhenMaximized = max.Checked; app.Config.Save(); };
        menu.Items.Add(max);

        var speed = new WF.ToolStripMenuItem("튀어나오는 반응 속도");
        foreach (var (name, ms) in new[] { ("바로", 0), ("보통", 150), ("느긋하게", 400) })
        {
            var m = new WF.ToolStripMenuItem(name) { Checked = app.Config.EdgeDelayMs == ms };
            m.Click += (s, e) =>
            {
                app.Config.EdgeDelayMs = ms;
                app.Config.Save();
                foreach (WF.ToolStripMenuItem x in speed.DropDownItems) x.Checked = x == m;
            };
            speed.DropDownItems.Add(m);
        }
        menu.Items.Add(speed);
        menu.Items.Add(BuildColorMenu(app));

        var single = new WF.ToolStripMenuItem("한 번 클릭으로 바로 실행") { Checked = app.Config.SingleClickLaunch, CheckOnClick = true };
        single.Click += (s, e) => { app.Config.SingleClickLaunch = single.Checked; app.Config.Save(); };
        menu.Items.Add(single);

        var splash = new WF.ToolStripMenuItem("켤 때 시작 애니메이션 보이기") { Checked = app.Config.ShowStartupAnimation, CheckOnClick = true };
        splash.Click += (s, e) => { app.Config.ShowStartupAnimation = splash.Checked; app.Config.Save(); };
        menu.Items.Add(splash);

        var auto = new WF.ToolStripMenuItem("윈도우 켤 때 자동 실행") { Checked = AutoStart.IsEnabled, CheckOnClick = true };
        auto.Click += (s, e) => AutoStart.Set(auto.Checked);
        menu.Items.Add(auto);

        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("바탕화면 배치 다시 읽어오기", null, (s, e) => app.RereadLayout());
        menu.Items.Add("새로고침", null, (s, e) => app.ScheduleRefresh());
        menu.Items.Add(new WF.ToolStripSeparator());
        menu.Items.Add("종료 (원래 아이콘 다시 보이기)", null, (s, e) => app.Quit());

        icon = new WF.NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "DesktopPop - 바탕화면 팝업",
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    public void Balloon(string title, string text) => icon.ShowBalloonTip(6000, title, text, WF.ToolTipIcon.Info);

    static Color ToDrawing(System.Windows.Media.Color c) => Color.FromArgb(c.R, c.G, c.B);

    /// <summary>"색상" 메뉴: 미리 만든 조합 + 직접 고르기.</summary>
    WF.ToolStripMenuItem BuildColorMenu(App app)
    {
        var root = new WF.ToolStripMenuItem("색상");
        void RefreshChecks()
        {
            var a = app.Config.AccentColor ?? Theme.DefaultAccent;
            var p = app.Config.PanelColor ?? Theme.DefaultPanel;
            foreach (WF.ToolStripItem it in root.DropDownItems)
                if (it is WF.ToolStripMenuItem mi && mi.Tag is (string pa, string pp))
                    mi.Checked = string.Equals(pa, a, StringComparison.OrdinalIgnoreCase) && string.Equals(pp, p, StringComparison.OrdinalIgnoreCase);
        }
        void Set(string? accent, string? panel)
        {
            app.SetTheme(accent, panel);
            icon.Icon = MakeIcon();
            RefreshChecks();
        }

        foreach (var (name, accent, panel) in Theme.Presets)
        {
            var mi = new WF.ToolStripMenuItem(name, Swatch(accent, panel)) { Tag = (accent, panel) };
            mi.Click += (s, e) => Set(accent, panel);
            root.DropDownItems.Add(mi);
        }
        root.DropDownItems.Add(new WF.ToolStripSeparator());
        root.DropDownItems.Add("강조색 직접 고르기... (반짝임·고리·배지)", null, (s, e) =>
        {
            var c = PickColor(Theme.Accent);
            if (c != null) Set(c, app.Config.PanelColor ?? Theme.DefaultPanel);
        });
        root.DropDownItems.Add("판 배경색 직접 고르기... (가장자리·가방 뒤)", null, (s, e) =>
        {
            var c = PickColor(Theme.Panel);
            if (c != null) Set(app.Config.AccentColor ?? Theme.DefaultAccent, c);
        });
        RefreshChecks();
        return root;
    }

    static string? PickColor(System.Windows.Media.Color current)
    {
        using var dlg = new WF.ColorDialog { FullOpen = true, AnyColor = true, Color = ToDrawing(current) };
        if (dlg.ShowDialog() != WF.DialogResult.OK) return null;
        return $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
    }

    /// <summary>메뉴 옆 작은 색 견본 (배경 네모 위에 강조색 동그라미).</summary>
    static Image Swatch(string accentHex, string panelHex)
    {
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var pb = new SolidBrush(ToDrawing(Theme.Parse(panelHex)!.Value))) g.FillRectangle(pb, 0, 0, 16, 16);
        using (var ab = new SolidBrush(ToDrawing(Theme.Parse(accentHex)!.Value))) g.FillEllipse(ab, 3, 3, 10, 10);
        return bmp;
    }

    static Icon MakeIcon()
    {
        // exe 에 들어 있는 앱 아이콘(작은 크기 = P 모양)을 알림 영역 크기에 맞춰 그대로 씀
        try
        {
            var exe = Environment.ProcessPath;
            int size = WF.SystemInformation.SmallIconSize.Width;
            if (exe != null && Icon.ExtractIcon(exe, 0, size) is { } appIcon) return appIcon;
        }
        catch { }

        // 혹시 못 꺼내면 예전처럼 직접 그림
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var br = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), ToDrawing(Theme.TrayA), ToDrawing(Theme.TrayB), 45f);
            g.FillEllipse(br, 1, 1, 30, 30);
            g.FillRectangle(Brushes.White, 9, 9, 6, 6);
            g.FillRectangle(Brushes.White, 17, 9, 6, 6);
            g.FillRectangle(Brushes.White, 9, 17, 6, 6);
            g.FillRectangle(Brushes.White, 17, 17, 6, 6);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
