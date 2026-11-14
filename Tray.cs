using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Media;

namespace DesktopPop;

internal sealed class Tray : IDisposable
{
	private readonly NotifyIcon icon;

	public Tray(App app)
	{
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip
		{
			Items = { 
			{
				"모든 패널과 가방 펼치기 / 접기",
				(Image?)null,
				(EventHandler?)((object? s, EventArgs e) =>
				{
					app.ToggleAllPanels();
				})
			} }
		};
		ToolStripMenuItem button = new ToolStripMenuItem("떠 있는 패널 버튼 표시")
		{
			Checked = app.Config.ShowPanelButton,
			CheckOnClick = true
		};
		button.Click += (object? s, EventArgs e) =>
		{
			app.SetPanelButtonVisible(button.Checked);
		};
		contextMenuStrip.Items.Add(button);
		ToolStripMenuItem toggleItem = (ToolStripMenuItem)contextMenuStrip.Items[0];
		ToolStripMenuItem hotkey = BuildHotkeyMenu(app);
		contextMenuStrip.Items.Add(hotkey);
		contextMenuStrip.Opening += (object? s, CancelEventArgs e) =>
		{
			button.Checked = app.Config.ShowPanelButton;
			toggleItem.ShortcutKeyDisplayString = GlobalHotkey.IsRegistered ? app.Config.ToggleHotkey : null;
			foreach (ToolStripItem it in hotkey.DropDownItems)
			{
				if (it is ToolStripMenuItem mi && mi.Tag is string combo)
				{
					mi.Checked = combo.Length == 0 ? !GlobalHotkey.IsRegistered : (GlobalHotkey.IsRegistered && string.Equals(combo, app.Config.ToggleHotkey, StringComparison.OrdinalIgnoreCase));
				}
			}
		};
		contextMenuStrip.Opened += (object? s, EventArgs e) =>
		{
			App.OpenMenus++;
		};
		contextMenuStrip.Closed += (object? s, ToolStripDropDownClosedEventArgs e) =>
		{
			App.OpenMenus = Math.Max(0, App.OpenMenus - 1);
		};
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add("새 가방 만들기...", null, (object? s, EventArgs e) =>
		{
			app.CreateBag(null);
		});
		contextMenuStrip.Items.Add("바탕화면에 새 폴더 만들기...", null, (object? s, EventArgs e) =>
		{
			app.CreateNewFolder(Side.Left, null);
		});
		contextMenuStrip.Items.Add("바탕화면 폴더 열기", null, (object? s, EventArgs e) =>
		{
			app.OpenDesktopFolder();
		});
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		ToolStripMenuItem hide = new ToolStripMenuItem("원래 바탕화면 아이콘 숨기기")
		{
			Checked = app.Config.HideDesktopIcons,
			CheckOnClick = true
		};
		hide.Click += (object? s, EventArgs e) =>
		{
			app.SetHideDesktopIcons(hide.Checked);
		};
		contextMenuStrip.Items.Add(hide);
		contextMenuStrip.Items.Add(new ToolStripMenuItem("자동 열기: 해당 가장자리가 바탕화면일 때만")
		{
			Enabled = false
		});
		ToolStripMenuItem speed = new ToolStripMenuItem("튀어나오는 반응 속도");
		(string, int)[] array = new (string, int)[3]
		{
			("바로", 0),
			("보통", 150),
			("느긋하게", 400)
		};
		for (int num = 0; num < array.Length; num++)
		{
			(string, int) tuple = array[num];
			string item = tuple.Item1;
			int ms = tuple.Item2;
			ToolStripMenuItem m = new ToolStripMenuItem(item)
			{
				Checked = (app.Config.EdgeDelayMs == ms)
			};
			m.Click += (object? s, EventArgs e) =>
			{
				app.Config.EdgeDelayMs = ms;
				app.Config.Save();
				foreach (ToolStripMenuItem dropDownItem in speed.DropDownItems)
				{
					dropDownItem.Checked = dropDownItem == m;
				}
			};
			speed.DropDownItems.Add(m);
		}
		contextMenuStrip.Items.Add(speed);
		contextMenuStrip.Items.Add(BuildColorMenu(app));
		ToolStripMenuItem single = new ToolStripMenuItem("한 번 클릭으로 바로 실행")
		{
			Checked = app.Config.SingleClickLaunch,
			CheckOnClick = true
		};
		single.Click += (object? s, EventArgs e) =>
		{
			app.Config.SingleClickLaunch = single.Checked;
			app.Config.Save();
		};
		contextMenuStrip.Items.Add(single);
		ToolStripMenuItem splash = new ToolStripMenuItem("켤 때 시작 애니메이션 보이기")
		{
			Checked = app.Config.ShowStartupAnimation,
			CheckOnClick = true
		};
		splash.Click += (object? s, EventArgs e) =>
		{
			app.Config.ShowStartupAnimation = splash.Checked;
			app.Config.Save();
		};
		contextMenuStrip.Items.Add(splash);
		ToolStripMenuItem auto = new ToolStripMenuItem("윈도우 켤 때 자동 실행")
		{
			Checked = AutoStart.IsEnabled,
			CheckOnClick = true
		};
		auto.Click += (object? s, EventArgs e) =>
		{
			AutoStart.Set(auto.Checked);
		};
		contextMenuStrip.Items.Add(auto);
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add("바탕화면 배치 다시 읽어오기", null, (object? s, EventArgs e) =>
		{
			app.RereadLayout();
		});
		contextMenuStrip.Items.Add("새로고침", null, (object? s, EventArgs e) =>
		{
			app.ScheduleRefresh();
		});
		contextMenuStrip.Items.Add(new ToolStripSeparator());
		contextMenuStrip.Items.Add("종료 (원래 아이콘 다시 보이기)", null, (object? s, EventArgs e) =>
		{
			app.Quit();
		});
		icon = new NotifyIcon
		{
			Icon = MakeIcon(),
			Text = "DesktopPop - 바탕화면 팝업",
			Visible = true,
			ContextMenuStrip = contextMenuStrip
		};
	}

	public void Balloon(string title, string text)
	{
		icon.ShowBalloonTip(6000, title, text, ToolTipIcon.Info);
	}

	private static System.Drawing.Color ToDrawing(System.Windows.Media.Color c)
	{
		return System.Drawing.Color.FromArgb(c.R, c.G, c.B);
	}

	/// <summary>"펼치기/접기 단축키" 메뉴: 미리 정한 몇 가지 중 고르거나 끄기.</summary>
	private ToolStripMenuItem BuildHotkeyMenu(App app)
	{
		ToolStripMenuItem root = new ToolStripMenuItem("펼치기/접기 단축키");
		foreach (string combo in GlobalHotkey.Presets)
		{
			string c = combo;
			ToolStripMenuItem mi = new ToolStripMenuItem(c + (c == GlobalHotkey.Default ? "  (기본)" : "")) { Tag = c };
			mi.Click += (object? s, EventArgs e) =>
			{
				if (!app.SetToggleHotkey(c, notifyOnFail: false))
				{
					System.Windows.MessageBox.Show(c + " 는 다른 프로그램이 이미 쓰고 있어서 쓸 수 없어요.\n다른 키를 골라 주세요.", "DesktopPop");
					app.SetToggleHotkey(app.Config.ToggleHotkey, notifyOnFail: false); // 원래 키 다시 등록
				}
			};
			root.DropDownItems.Add(mi);
		}
		root.DropDownItems.Add(new ToolStripSeparator());
		ToolStripMenuItem custom = new ToolStripMenuItem("직접 정하기...");
		custom.Click += (object? s, EventArgs e) =>
		{
			string old = app.Config.ToggleHotkey;
			GlobalHotkey.Unregister(); // 키를 누르는 동안 지금 단축키가 먹지 않게 잠깐 풀어 둠
			string? picked = HotkeyDialog.Ask(old);
			if (picked == null)
			{
				app.SetToggleHotkey(old, notifyOnFail: false); // 취소: 원래대로
				return;
			}
			if (!app.SetToggleHotkey(picked, notifyOnFail: false))
			{
				System.Windows.MessageBox.Show(picked + " 는 Windows 나 다른 프로그램이 이미 쓰고 있어서 쓸 수 없어요.\n다른 조합을 골라 주세요.", "DesktopPop");
				app.SetToggleHotkey(old, notifyOnFail: false);
			}
		};
		root.DropDownItems.Add(custom);
		root.DropDownOpening += (object? s, EventArgs e) =>
		{
			string cur = app.Config.ToggleHotkey;
			bool isCustom = GlobalHotkey.IsRegistered && Array.FindIndex(GlobalHotkey.Presets, p => string.Equals(p, cur, StringComparison.OrdinalIgnoreCase)) < 0;
			custom.Text = isCustom ? "직접 정하기: " + cur + "  (바꾸기...)" : "직접 정하기...";
			custom.Checked = isCustom;
		};
		root.DropDownItems.Add(new ToolStripSeparator());
		ToolStripMenuItem off = new ToolStripMenuItem("끄기") { Tag = "" };
		off.Click += (object? s, EventArgs e) =>
		{
			app.SetToggleHotkey("", notifyOnFail: false);
		};
		root.DropDownItems.Add(off);
		return root;
	}

	private ToolStripMenuItem BuildColorMenu(App app)
	{
		ToolStripMenuItem root = new ToolStripMenuItem("색상");
		(string, string, string)[] presets = Theme.Presets;
		for (int i = 0; i < presets.Length; i++)
		{
			(string, string, string) tuple = presets[i];
			string item = tuple.Item1;
			string accent = tuple.Item2;
			string panel = tuple.Item3;
			ToolStripMenuItem toolStripMenuItem = new ToolStripMenuItem(item, Swatch(accent, panel))
			{
				Tag = (accent, panel)
			};
			toolStripMenuItem.Click += (object? s, EventArgs e) =>
			{
				Set(accent, panel);
			};
			root.DropDownItems.Add(toolStripMenuItem);
		}
		root.DropDownItems.Add(new ToolStripSeparator());
		root.DropDownItems.Add("강조색 직접 고르기... (반짝임·고리·배지)", null, (object? s, EventArgs e) =>
		{
			string text = PickColor(Theme.Accent);
			if (text != null)
			{
				Set(text, app.Config.PanelColor ?? "#1A1026");
			}
		});
		root.DropDownItems.Add("판 배경색 직접 고르기... (가장자리·가방 뒤)", null, (object? s, EventArgs e) =>
		{
			string text = PickColor(Theme.Panel);
			if (text != null)
			{
				Set(app.Config.AccentColor ?? "#FF6EB4", text);
			}
		});
		RefreshChecks();
		return root;
		void RefreshChecks()
		{
			string b = app.Config.AccentColor ?? "#FF6EB4";
			string b2 = app.Config.PanelColor ?? "#1A1026";
			foreach (ToolStripItem dropDownItem in root.DropDownItems)
			{
				if (dropDownItem is ToolStripMenuItem { Tag: ITuple { Length: 2 } tag } toolStripMenuItem2 && tag[0] is string a && tag[1] is string a2)
				{
					toolStripMenuItem2.Checked = string.Equals(a, b, StringComparison.OrdinalIgnoreCase) && string.Equals(a2, b2, StringComparison.OrdinalIgnoreCase);
				}
			}
		}
		void Set(string? accent2, string? panel2)
		{
			app.SetTheme(accent2, panel2);
			icon.Icon = MakeIcon();
			RefreshChecks();
		}
	}

	private static string? PickColor(System.Windows.Media.Color current)
	{
		using ColorDialog colorDialog = new ColorDialog
		{
			FullOpen = true,
			AnyColor = true,
			Color = ToDrawing(current)
		};
		if (colorDialog.ShowDialog() != DialogResult.OK)
		{
			return null;
		}
		return $"#{colorDialog.Color.R:X2}{colorDialog.Color.G:X2}{colorDialog.Color.B:X2}";
	}

	private static Image Swatch(string accentHex, string panelHex)
	{
		Bitmap bitmap = new Bitmap(16, 16);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		using (SolidBrush brush = new SolidBrush(ToDrawing(Theme.Parse(panelHex).Value)))
		{
			graphics.FillRectangle(brush, 0, 0, 16, 16);
		}
		using (SolidBrush brush2 = new SolidBrush(ToDrawing(Theme.Parse(accentHex).Value)))
		{
			graphics.FillEllipse(brush2, 3, 3, 10, 10);
		}
		return bitmap;
	}

	private static Icon MakeIcon()
	{
		try
		{
			string processPath = Environment.ProcessPath;
			int width = SystemInformation.SmallIconSize.Width;
			if (processPath != null)
			{
				Icon icon = Icon.ExtractIcon(processPath, 0, width);
				if (icon != null)
				{
					return icon;
				}
			}
		}
		catch
		{
		}
		Bitmap bitmap = new Bitmap(32, 32);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using System.Drawing.Drawing2D.LinearGradientBrush brush = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 32, 32), ToDrawing(Theme.TrayA), ToDrawing(Theme.TrayB), 45f);
			graphics.FillEllipse(brush, 1, 1, 30, 30);
			graphics.FillRectangle(System.Drawing.Brushes.White, 9, 9, 6, 6);
			graphics.FillRectangle(System.Drawing.Brushes.White, 17, 9, 6, 6);
			graphics.FillRectangle(System.Drawing.Brushes.White, 9, 17, 6, 6);
			graphics.FillRectangle(System.Drawing.Brushes.White, 17, 17, 6, 6);
		}
		return Icon.FromHandle(bitmap.GetHicon());
	}

	public void Dispose()
	{
		icon.Visible = false;
		icon.Dispose();
	}
}
