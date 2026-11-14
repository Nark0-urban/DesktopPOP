using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace DesktopPop;

internal static class ShellMenu
{
	public static bool Show(Window owner, IReadOnlyList<string> paths, List<MenuEntry> extras, Action? onRename)
	{
		if (paths.Count == 0)
		{
			return false;
		}
		string[] targets = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		string[] files = targets.Where((string p) => !p.StartsWith("::", StringComparison.Ordinal)).ToArray();
		ContextMenu contextMenu = new ContextMenu
		{
			PlacementTarget = owner,
			Placement = PlacementMode.MousePoint
		};
		string text = ((targets.Length > 1) ? $" ({targets.Length}개)" : "");
		contextMenu.Items.Add(ActionItem("열기" + text, () =>
		{
			Sel.Clear();
			string[] array2 = targets;
			for (int i = 0; i < array2.Length; i++)
			{
				Shell.Launch(Item(array2[i]));
			}
		}));
		if (files.Length != 0)
		{
			contextMenu.Items.Add(ActionItem("파일 위치 열기", () =>
			{
				Shell.OpenLocation(Item(files[0]));
			}));
			contextMenu.Items.Add(new Separator());
			contextMenu.Items.Add(ActionItem("잘라내기" + text, () =>
			{
				CopyPaths(files, cut: true);
			}));
			contextMenu.Items.Add(ActionItem("복사" + text, () =>
			{
				CopyPaths(files, cut: false);
			}));
			contextMenu.Items.Add(ActionItem("삭제" + text, () =>
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
			string[] array = targets;
			foreach (string path in array)
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
		if (files.Length != 0)
		{
			MenuItem menuItem3 = ActionItem("탐색기에서 추가 메뉴...", () =>
			{
				Shell.OpenLocation(Item(files[0]));
			});
			menuItem3.ToolTip = "탐색기에서 선택된 파일을 우클릭하면 압축·공유·연결 프로그램 등의 메뉴를 사용할 수 있어요.";
			contextMenu.Items.Add(menuItem3);
		}
		App.Track(contextMenu);
		contextMenu.IsOpen = true;
		return true;
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
