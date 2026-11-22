using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DesktopPop;

/// <summary>바탕화면 폴더(내 것 + 공용)와 가방에 든 파일 목록, 그리고 각 아이콘 그림을 들고 있는 곳.</summary>
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
			(string, BitmapSource) loaded = ShellIcons.Load(key, px);
			return ((string? name, ImageSource? icon))(name: loaded.Item1, icon: loaded.Item2);
		}));
		queueIconWork = iconQueue ?? new Action<Action>(ShellIcons.QueueWork);
		desktopDirectories = directories ?? ((Func<string[]>)(() => DesktopDirs));
	}

	/// <summary>
	/// 바탕화면 폴더(내 것 + 공용)와 가방에 든 파일 목록을 다시 읽음. 바뀐 것만 아이콘을 새로 만들고,
	/// 아이콘 그림은 뒤에서 하나씩 읽어 오는 동안 임시 그림(폴더·파일 모양)을 보여 줌.
	/// </summary>
	public void Refresh(AppConfig cfg)
	{
		if (Application.Current is App { ShuttingDown: not false })
		{
			return;
		}
		desktopKeys.Clear();
		desktopSet.Clear();
		AddDesktopKey("::{645FF040-5081-101B-9F08-00AA002F954E}");
		Dictionary<string, Metadata> metadataByKey = new Dictionary<string, Metadata>(Cmp) { ["::{645FF040-5081-101B-9F08-00AA002F954E}"] = default };
		foreach (string path in desktopDirectories())
		{
			try
			{
				foreach (FileSystemInfo entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
				{
					try
					{
						FileAttributes attributes = entry.Attributes;
						if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0) // 숨김·시스템 파일(desktop.ini 등)은 빼고
						{
							metadataByKey[entry.FullName] = new Metadata(entry.LastWriteTimeUtc, (attributes & FileAttributes.Directory) != 0);
							AddDesktopKey(entry.FullName);
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
			// 가방에는 바탕화면 밖 파일도 들어 있을 수 있음
			foreach (string key in bag.Items)
			{
				if (!wanted.Contains(key) && TryMetadata(key, out var metadata))
				{
					wanted.Add(key);
					metadataByKey[key] = metadata;
				}
			}
		}
		foreach (string gone in items.Keys.Where((string key) => !wanted.Contains(key)).ToList())
		{
			items.Remove(gone);
			staleIcons.Remove(gone);
			CancelPending(gone);
		}
		// 화면 스레드에서 불렸으면 아이콘 그림은 뒤에서 읽고, 시작할 때(다른 스레드)는 그 자리에서 바로 읽음
		App? uiApp = ((Application.Current is App current && current.Dispatcher.CheckAccess() && !current.ShuttingDown) ? current : null);
		foreach (string key in wanted)
		{
			Metadata metadata = metadataByKey[key];
			bool iconWasStale = staleIcons.Remove(key);
			if (!items.TryGetValue(key, out DesktopItem existing) || !(existing.Stamp == metadata.Stamp) || existing.IsFolder != metadata.IsFolder || iconWasStale)
			{
				CancelPending(key);
				if (uiApp == null)
				{
					(string, ImageSource) loaded = loadIcon(key, 96);
					items[key] = CreateItem(key, metadata, loaded.Item1, loaded.Item2);
				}
				else
				{
					DesktopItem placeholder = CreateItem(key, metadata, existing?.Name, existing?.Icon ?? ShellIcons.Placeholder(metadata.IsFolder));
					items[key] = placeholder;
					QueueIcon(placeholder, metadata, uiApp);
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
				// 읽는 동안 파일이 바뀌었으면 그 그림은 "오래됨"으로 표시해 다음 새로고침 때 다시 읽게 함
				(string, ImageSource) loaded = default;
				bool current = TryMetadata(item.Key, out var before) && before == metadata;
				if (current)
				{
					try
					{
						loaded = loadIcon(item.Key, 96);
					}
					catch (Exception ex)
					{
						App.Log(ex);
					}
					current = TryMetadata(item.Key, out var after) && after == metadata;
				}
				if (!request.Cancelled && !app.ShuttingDown && !app.Dispatcher.HasShutdownStarted)
				{
					IconResult result = new IconResult(request, loaded.Item1, loaded.Item2, current);
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
				bool changed = false;
				// 60ms 동안 모인 아이콘들을 한 번에 반영 (하나 올 때마다 다시 그리지 않게)
				foreach (IconResult readyIcon in readyIcons)
				{
					PendingIcon request = readyIcon.Request;
					DesktopItem item = request.Item;
					if (!request.Cancelled && pendingIcons.TryGetValue(item.Key, out PendingIcon latest) && latest == request && items.TryGetValue(item.Key, out DesktopItem stillShown) && stillShown == item)
					{
						pendingIcons.Remove(item.Key);
						if (readyIcon.Current)
						{
							string name = (string.IsNullOrWhiteSpace(readyIcon.Name) ? item.Name : readyIcon.Name);
							ImageSource icon = readyIcon.Icon ?? item.Icon;
							if (name != item.Name || icon != item.Icon)
							{
								items[item.Key] = CreateItem(item.Key, new Metadata(item.Stamp, item.IsFolder), name, icon);
								changed = true;
							}
						}
						else
						{
							staleIcons.Add(item.Key);
							changed = true;
						}
					}
				}
				readyIcons.Clear();
				if (changed)
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
		// 이름이 같은 아이콘은 종류·경로를 같이 보여 줘서 구별되게
		foreach (IGrouping<string, DesktopItem> sameName in from @group in items.Values.GroupBy((DesktopItem item) => item.Name, Cmp)
			where @group.Count() > 1
			select @group)
		{
			foreach (DesktopItem duplicate in sameName)
			{
				duplicate.HasDuplicateName = true;
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

	/// <summary>판·가방 어디에도 없는 새 바탕화면 아이콘은 주 모니터 왼쪽 판 끝에 붙임.</summary>
	public void AssignNew(AppConfig cfg)
	{
		HashSet<string> placed = new HashSet<string>(cfg.ItemLists().SelectMany((List<string> items) => items), Cmp);
		List<string> primaryLeft = cfg.GetPanel(cfg.PrimaryMonitorId, Side.Left).Items;
		foreach (string desktopKey in desktopKeys)
		{
			if (placed.Add(desktopKey))
			{
				primaryLeft.Add(desktopKey);
			}
		}
	}

	/// <summary>이 판에 실제로 보여 줄 아이콘들 (지금 바탕화면에 있는 것만, 중복 없이, 판에 적힌 순서대로).</summary>
	public List<DesktopItem> VisibleFor(PanelConfig panel)
	{
		HashSet<string> seen = new HashSet<string>(Cmp);
		List<DesktopItem> visible = new List<DesktopItem>();
		foreach (string key in panel.Items)
		{
			if (desktopSet.Contains(key) && seen.Add(key))
			{
				DesktopItem desktopItem = Get(key);
				if (desktopItem != null)
				{
					visible.Add(desktopItem);
				}
			}
		}
		return visible;
	}
}
