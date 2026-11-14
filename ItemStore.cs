using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopPop;

internal sealed class ItemStore
{
	private readonly record struct Metadata(DateTime Stamp, bool IsFolder);

	private sealed class PendingIcon
	{
		public required DesktopItem Item;

		public volatile bool Cancelled;
	}

	private sealed record IconResult(PendingIcon Request, string? Name, ImageSource? Icon, bool Current);

	public const string RecycleBinKey = "::{645FF040-5081-101B-9F08-00AA002F954E}";

	private static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

	private readonly Dictionary<string, DesktopItem> items = new Dictionary<string, DesktopItem>(Cmp);

	private readonly List<string> desktopKeys = new List<string>();

	private readonly HashSet<string> desktopSet = new HashSet<string>(Cmp);

	private readonly Dictionary<string, PendingIcon> pendingIcons = new Dictionary<string, PendingIcon>(Cmp);

	private readonly HashSet<string> staleIcons = new HashSet<string>(Cmp);

	private readonly List<IconResult> readyIcons = new List<IconResult>();

	private readonly Func<string, int, (string? name, ImageSource? icon)> loadIcon;

	private readonly Action<Action> queueIconWork;

	private readonly Func<string[]> desktopDirectories;

	private DispatcherTimer? iconBatchTimer;

	public static string[] DesktopDirs => new string[2]
	{
		Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
		Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
	}.Where((string d) => !string.IsNullOrEmpty(d) && Directory.Exists(d)).Distinct(Cmp).ToArray();

	public IReadOnlyList<string> DesktopKeyList => desktopKeys;

	public IEnumerable<DesktopItem> DesktopItems => from item in desktopKeys.Select(Get)
		where item != null
		select (item);

	internal ItemStore()
		: this(null, null, null)
	{
	}

	internal ItemStore(Func<string, int, (string? name, ImageSource? icon)>? iconLoader = null, Action<Action>? iconQueue = null, Func<string[]>? directories = null)
	{
		loadIcon = iconLoader ?? ((Func<string, int, (string, ImageSource)>)((string key, int px) =>
		{
			(string, BitmapSource) tuple = ShellIcons.Load(key, px);
			return ((string? name, ImageSource? icon))(name: tuple.Item1, icon: tuple.Item2);
		}));
		queueIconWork = iconQueue ?? new Action<Action>(ShellIcons.QueueWork);
		desktopDirectories = directories ?? ((Func<string[]>)(() => DesktopDirs));
	}

	public void Refresh(AppConfig cfg)
	{
		if (Application.Current is App { ShuttingDown: not false })
		{
			return;
		}
		desktopKeys.Clear();
		desktopSet.Clear();
		AddDesktopKey("::{645FF040-5081-101B-9F08-00AA002F954E}");
		Dictionary<string, Metadata> dictionary = new Dictionary<string, Metadata>(Cmp) { ["::{645FF040-5081-101B-9F08-00AA002F954E}"] = default };
		string[] array = desktopDirectories();
		foreach (string path in array)
		{
			try
			{
				foreach (FileSystemInfo item in new DirectoryInfo(path).EnumerateFileSystemInfos())
				{
					try
					{
						FileAttributes attributes = item.Attributes;
						if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
						{
							dictionary[item.FullName] = new Metadata(item.LastWriteTimeUtc, (attributes & FileAttributes.Directory) != 0);
							AddDesktopKey(item.FullName);
						}
					}
					catch
					{
					}
				}
			}
			catch (Exception ex)
			{
				App.Log(ex);
			}
		}
		HashSet<string> wanted = new HashSet<string>(desktopSet, Cmp);
		foreach (BagConfig bag in cfg.Bags)
		{
			foreach (string item2 in bag.Items)
			{
				if (!wanted.Contains(item2) && TryMetadata(item2, out var metadata))
				{
					wanted.Add(item2);
					dictionary[item2] = metadata;
				}
			}
		}
		foreach (string item3 in items.Keys.Where((string key) => !wanted.Contains(key)).ToList())
		{
			items.Remove(item3);
			staleIcons.Remove(item3);
			CancelPending(item3);
		}
		App app2 = ((Application.Current is App app3 && app3.Dispatcher.CheckAccess() && !app3.ShuttingDown) ? app3 : null);
		foreach (string item4 in wanted)
		{
			Metadata metadata2 = dictionary[item4];
			bool flag = staleIcons.Remove(item4);
			if (!items.TryGetValue(item4, out DesktopItem value) || !(value.Stamp == metadata2.Stamp) || value.IsFolder != metadata2.IsFolder || flag)
			{
				CancelPending(item4);
				if (app2 == null)
				{
					(string, ImageSource) tuple = loadIcon(item4, 96);
					items[item4] = CreateItem(item4, metadata2, tuple.Item1, tuple.Item2);
				}
				else
				{
					DesktopItem desktopItem = CreateItem(item4, metadata2, value?.Name, value?.Icon ?? ShellIcons.Placeholder(metadata2.IsFolder));
					items[item4] = desktopItem;
					QueueIcon(desktopItem, metadata2, app2);
				}
			}
		}
		MarkDuplicateNames();
	}

	private void CancelPending(string key)
	{
		if (pendingIcons.Remove(key, out PendingIcon value))
		{
			value.Cancelled = true;
		}
	}

	private void QueueIcon(DesktopItem item, Metadata metadata, App app)
	{
		PendingIcon request = new PendingIcon
		{
			Item = item
		};
		pendingIcons[item.Key] = request;
		queueIconWork(() =>
		{
			if (!request.Cancelled && !app.ShuttingDown && !app.Dispatcher.HasShutdownStarted)
			{
				(string, ImageSource) tuple = default;
				bool flag = TryMetadata(item.Key, out var metadata2) && metadata2 == metadata;
				if (flag)
				{
					try
					{
						tuple = loadIcon(item.Key, 96);
					}
					catch (Exception ex)
					{
						App.Log(ex);
					}
					flag = TryMetadata(item.Key, out var metadata3) && metadata3 == metadata;
				}
				if (!request.Cancelled && !app.ShuttingDown && !app.Dispatcher.HasShutdownStarted)
				{
					IconResult result = new IconResult(request, tuple.Item1, tuple.Item2, flag);
					app.Dispatcher.InvokeAsync(() =>
					{
						if (!request.Cancelled && !app.ShuttingDown)
						{
							readyIcons.Add(result);
							if (iconBatchTimer == null)
							{
								iconBatchTimer = CreateBatchTimer(app);
							}
							if (!iconBatchTimer.IsEnabled)
							{
								iconBatchTimer.Start();
							}
						}
					}, DispatcherPriority.Background);
				}
			}
		});
	}

	private DispatcherTimer CreateBatchTimer(App app)
	{
		DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromMilliseconds(60L)
		};
		timer.Tick += (object? sender, EventArgs args) =>
		{
			timer.Stop();
			if (app.ShuttingDown)
			{
				readyIcons.Clear();
			}
			else
			{
				bool flag = false;
				foreach (IconResult readyIcon in readyIcons)
				{
					PendingIcon request = readyIcon.Request;
					DesktopItem item = request.Item;
					if (!request.Cancelled && pendingIcons.TryGetValue(item.Key, out PendingIcon value) && value == request && items.TryGetValue(item.Key, out DesktopItem value2) && value2 == item)
					{
						pendingIcons.Remove(item.Key);
						if (readyIcon.Current)
						{
							string text = (string.IsNullOrWhiteSpace(readyIcon.Name) ? item.Name : readyIcon.Name);
							ImageSource imageSource = readyIcon.Icon ?? item.Icon;
							if (text != item.Name || imageSource != item.Icon)
							{
								items[item.Key] = CreateItem(item.Key, new Metadata(item.Stamp, item.IsFolder), text, imageSource);
								flag = true;
							}
						}
						else
						{
							staleIcons.Add(item.Key);
							flag = true;
						}
					}
				}
				readyIcons.Clear();
				if (flag)
				{
					MarkDuplicateNames();
					app.ScheduleRefresh();
				}
			}
		};
		return timer;
	}

	private void MarkDuplicateNames()
	{
		foreach (DesktopItem value in items.Values)
		{
			value.HasDuplicateName = false;
		}
		foreach (IGrouping<string, DesktopItem> item in from @group in items.Values.GroupBy((DesktopItem item) => item.Name, Cmp)
			where @group.Count() > 1
			select @group)
		{
			foreach (DesktopItem item2 in item)
			{
				item2.HasDuplicateName = true;
			}
		}
	}

	private void AddDesktopKey(string key)
	{
		if (desktopSet.Add(key))
		{
			desktopKeys.Add(key);
		}
	}

	private static bool TryMetadata(string key, out Metadata metadata)
	{
		metadata = default;
		if (key.StartsWith("::", StringComparison.Ordinal))
		{
			return true;
		}
		try
		{
			FileInfo fileInfo = new FileInfo(key);
			fileInfo.Refresh();
			FileAttributes attributes = fileInfo.Attributes;
			if (attributes == (FileAttributes)(-1))
			{
				return false;
			}
			metadata = new Metadata(fileInfo.LastWriteTimeUtc, (attributes & FileAttributes.Directory) != 0);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static DesktopItem CreateItem(string key, Metadata metadata, string? name, ImageSource? icon)
	{
		return new DesktopItem
		{
			Key = key,
			Path = key,
			Name = (string.IsNullOrWhiteSpace(name) ? Path.GetFileName(key.TrimEnd(Path.DirectorySeparatorChar)) : name),
			Icon = icon,
			IsVirtual = key.StartsWith("::", StringComparison.Ordinal),
			IsFolder = metadata.IsFolder,
			IsShortcut = (Path.GetExtension(key).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(key).Equals(".url", StringComparison.OrdinalIgnoreCase)),
			Stamp = metadata.Stamp
		};
	}

	public DesktopItem? Get(string key)
	{
		if (!items.TryGetValue(key, out DesktopItem value))
		{
			return null;
		}
		return value;
	}

	public bool IsOnDesktop(string key)
	{
		return desktopSet.Contains(key);
	}

	public void AssignNew(AppConfig cfg)
	{
		HashSet<string> hashSet = new HashSet<string>(cfg.ItemLists().SelectMany((List<string> items) => items), Cmp);
		List<string> list = cfg.GetPanel(cfg.PrimaryMonitorId, Side.Left).Items;
		foreach (string desktopKey in desktopKeys)
		{
			if (hashSet.Add(desktopKey))
			{
				list.Add(desktopKey);
			}
		}
	}

	public List<DesktopItem> VisibleFor(PanelConfig panel)
	{
		HashSet<string> hashSet = new HashSet<string>(Cmp);
		List<DesktopItem> list = new List<DesktopItem>();
		foreach (string item in panel.Items)
		{
			if (desktopSet.Contains(item) && hashSet.Add(item))
			{
				DesktopItem desktopItem = Get(item);
				if (desktopItem != null)
				{
					list.Add(desktopItem);
				}
			}
		}
		return list;
	}
}
