using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace DesktopPop;

/// <summary>아이콘 우클릭 메뉴: Windows 원래 메뉴(도우미 프로세스) 또는 DesktopPop 간단 메뉴.</summary>
internal static class ShellMenu
{
	public static bool Show(Window owner, IReadOnlyList<string> paths, List<MenuEntry> extras, Action? onRename)
	{
		if (paths.Count == 0)
		{
			return false;
		}
		string[] targets = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		// 기본: Windows 원래 우클릭 메뉴 (압축 풀기·압축하기, 보내기 등). 도우미 프로세스에서 띄우고,
		// 못 띄우거나 도우미가 튕기면 간단 메뉴로. 꺼 두면 처음부터 간단 메뉴.
		if (App.Instance.Config.UseWindowsMenu)
		{
			bool extended = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
			NativeShellMenu.ShowAsync(owner, targets, extras, onRename, () => ShowSimple(owner, targets, extras, onRename), extended);
			return true;
		}
		ShowSimple(owner, targets, extras, onRename);
		return true;
	}

	/// <summary>DesktopPop 간단 메뉴 (다른 프로그램의 메뉴 확장을 안 불러서 튕길 일이 없음).</summary>
	private static void ShowSimple(Window owner, string[] targets, List<MenuEntry> extras, Action? onRename)
	{
		string[] files = targets.Where((string p) => !p.StartsWith("::", StringComparison.Ordinal)).ToArray();
		ContextMenu contextMenu = new ContextMenu
		{
			PlacementTarget = owner,
			Placement = PlacementMode.MousePoint
		};
		string count = ((targets.Length > 1) ? $" ({targets.Length}개)" : "");
		contextMenu.Items.Add(ActionItem("열기" + count, () =>
		{
			Sel.Clear();
			foreach (string target in targets)
			{
				Shell.Launch(Item(target));
			}
		}));
		if (files.Length != 0)
		{
			contextMenu.Items.Add(ActionItem("파일 위치 열기", () =>
			{
				Shell.OpenLocation(Item(files[0]));
			}));
			contextMenu.Items.Add(new Separator());
			contextMenu.Items.Add(ActionItem("잘라내기" + count, () =>
			{
				CopyPaths(files, cut: true);
			}));
			contextMenu.Items.Add(ActionItem("복사" + count, () =>
			{
				CopyPaths(files, cut: false);
			}));
			contextMenu.Items.Add(ActionItem("삭제" + count, () =>
			{
				Sel.Clear();
				App.Instance.RecycleFiles(files);
			}));
			if (targets.Length == 1 && onRename != null)
			{
				contextMenu.Items.Add(ActionItem("이름 바꾸기...", onRename));
			}
		}
		if (extras.Count > 0)
		{
			contextMenu.Items.Add(new Separator());
			foreach (MenuEntry extra in extras)
			{
				contextMenu.Items.Add(ToWpf(extra));
			}
		}
		contextMenu.Items.Add(new Separator());
		if (targets.Length == 1)
		{
			contextMenu.Items.Add(ActionItem("속성", () =>
			{
				Shell.ShowProperties(Item(targets[0]));
			}));
		}
		else
		{
			MenuItem menuItem = new MenuItem
			{
				Header = "속성 (파일 선택)"
			};
			foreach (string path in targets)
			{
				DesktopItem item = Item(path);
				MenuItem menuItem2 = ActionItem(item.DisplayLabel, () =>
				{
					Shell.ShowProperties(item);
				});
				menuItem2.ToolTip = item.ToolTipText;
				menuItem.Items.Add(menuItem2);
			}
			contextMenu.Items.Add(menuItem);
		}
		MenuItem menuItem3 = ActionItem("Windows 메뉴 (압축·보내기 등)...", () =>
		{
			// 이 메뉴가 닫힌 뒤 같은 자리에 Windows 원래 메뉴를 띄움 (도우미가 실패하면 탐색기에서 파일 위치를 열어 줌)
			NativeShellMenu.ShowAsync(owner, targets, new List<MenuEntry>(), onRename, () =>
			{
				if (files.Length != 0)
				{
					Shell.OpenLocation(Item(files[0]));
				}
			}, extended: false);
		});
		menuItem3.ToolTip = "압축 풀기·압축하기, 보내기, 연결 프로그램 같은 Windows 원래 우클릭 메뉴를 띄워요.";
		contextMenu.Items.Add(menuItem3);
		App.Track(contextMenu);
		contextMenu.IsOpen = true;
	}

	private static DesktopItem Item(string path)
	{
		return App.Instance.Store.Get(path) ?? new DesktopItem
		{
			Key = path,
			Path = path,
			Name = Path.GetFileName(path.TrimEnd('\\')),
			IsVirtual = path.StartsWith("::", StringComparison.Ordinal),
			IsFolder = Directory.Exists(path),
			IsShortcut = (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
		};
	}

	private static MenuItem ActionItem(string title, Action action)
	{
		MenuItem item = new MenuItem
		{
			Header = title
		};
		item.Click += (object _, RoutedEventArgs e) =>
		{
			e.Handled = true;
			item.Dispatcher.InvokeAsync(() =>
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					App.Log(ex);
					MessageBox.Show("작업을 완료하지 못했어요.\n" + ex.Message, "DesktopPop");
				}
			}, DispatcherPriority.Background);
		};
		return item;
	}

	private static object ToWpf(MenuEntry entry)
	{
		if (entry.IsSeparator)
		{
			return new Separator();
		}
		if (entry.Children == null)
		{
			return ActionItem(entry.Text, entry.Run ?? ((Action)(() =>
			{
			})));
		}
		MenuItem menuItem = new MenuItem
		{
			Header = entry.Text
		};
		foreach (MenuEntry child in entry.Children)
		{
			menuItem.Items.Add(ToWpf(child));
		}
		return menuItem;
	}

	internal static void CopyPaths(IEnumerable<string> paths, bool cut)
	{
		StringCollection stringCollection = new StringCollection();
		stringCollection.AddRange(paths.ToArray());
		if (stringCollection.Count != 0)
		{
			DataObject dataObject = new DataObject();
			dataObject.SetFileDropList(stringCollection);
			dataObject.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((!cut) ? 1 : 2)));
			Clipboard.SetDataObject(dataObject, copy: true);
		}
	}
}
