using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DesktopPop;

/// <summary>설정 파일(data\config.json): 판별 아이콘 순서, 가방, 색상, 단축키 등. 저장 위치는 exe 옆 data 폴더 (쓸 수 없으면 사용자 폴더).</summary>
internal sealed class AppConfig
{
	private static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	public List<string> Left { get; set; } = new List<string>();

	public List<string> Right { get; set; } = new List<string>();

	public List<PanelConfig> Panels { get; set; } = new List<PanelConfig>();

	public bool PanelLayoutMigrated { get; set; }

	public string PrimaryMonitorId { get; set; } = "";

	public List<BagConfig> Bags { get; set; } = new List<BagConfig>();

	public bool HideDesktopIcons { get; set; } = true;

	public bool ShowStartupAnimation { get; set; } = true;

	public bool LibrarySeeded { get; set; }

	public bool CyberLibrarySeeded { get; set; }

	public bool KafkaLibrarySeeded { get; set; }

	public bool SingleClickLaunch { get; set; }

	/// <summary>아이콘 우클릭에 Windows 원래 메뉴(압축·보내기 등)를 띄움. 끄면 DesktopPop 간단 메뉴.</summary>
	public bool UseWindowsMenu { get; set; } = true;

	public string? AccentColor { get; set; }

	public string? PanelColor { get; set; }

	public int EdgeDelayMs { get; set; } = 150;

	public bool LayoutImported { get; set; }

	public bool FirstRunDone { get; set; }

	/// <summary>모든 패널과 가방을 펼치기/접기 단축키. 빈 문자열이면 끔.</summary>
	public string ToggleHotkey { get; set; } = GlobalHotkey.Default;

	/// <summary>
	/// 설정은 실행 파일 옆 data 폴더에 저장 (폴더만 지우면 깨끗하게 제거되게).
	/// 단, 그 폴더에 쓸 수 없으면(Program Files 등) 사용자 폴더 %LOCALAPPDATA%\DesktopPop\data 로 대신 저장.
	/// </summary>
	public static string Dir => dir.Value;

	public static bool UsingFallbackDir => !string.Equals(Dir, PortableDir, StringComparison.OrdinalIgnoreCase);

	private static string PortableDir => Path.Combine(AppContext.BaseDirectory, "data");

	private static readonly Lazy<string> dir = new Lazy<string>(() =>
	{
		if (CanWrite(PortableDir))
		{
			return PortableDir;
		}
		string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPop", "data");
		try
		{
			Directory.CreateDirectory(fallback);
			// 예전에 읽기 전용 폴더 쪽에 남아 있던 설정이 있으면 처음 한 번 가져옴
			string oldConfig = Path.Combine(PortableDir, "config.json");
			string newConfig = Path.Combine(fallback, "config.json");
			if (!File.Exists(newConfig) && File.Exists(oldConfig))
			{
				File.Copy(oldConfig, newConfig);
			}
		}
		catch
		{
		}
		return fallback;
	});

	private static bool CanWrite(string folder)
	{
		try
		{
			Directory.CreateDirectory(folder);
			string probe = Path.Combine(folder, ".write-test-" + Environment.ProcessId);
			File.WriteAllText(probe, "");
			File.Delete(probe);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string FilePath => Path.Combine(Dir, "config.json");

	private static string OldDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopPop");

	public PanelConfig GetPanel(string monitorId, Side side)
	{
		PanelConfig panelConfig = Panels.FirstOrDefault((PanelConfig p) => p.Side == side && string.Equals(p.MonitorId, monitorId, StringComparison.OrdinalIgnoreCase));
		if (panelConfig != null)
		{
			return panelConfig;
		}
		panelConfig = new PanelConfig
		{
			MonitorId = monitorId,
			Side = side
		};
		Panels.Add(panelConfig);
		return panelConfig;
	}

	public void EnsurePanelLayout(string primaryId, IEnumerable<string> attachedMonitorIds)
	{
		PrimaryMonitorId = primaryId;
		foreach (string item in attachedMonitorIds.Append(primaryId).Distinct(StringComparer.OrdinalIgnoreCase))
		{
			GetPanel(item, Side.Left);
			GetPanel(item, Side.Right);
		}
		if (PanelLayoutMigrated)
		{
			return;
		}
		// 1.1.0 전 설정(왼쪽/오른쪽 목록 하나씩)을 주 모니터 판으로 옮김
		bool hadOldLayout = Left.Count > 0 || Right.Count > 0;
		HashSet<string> alreadyPlaced = new HashSet<string>(Panels.SelectMany((PanelConfig p) => p.Items).Concat(Bags.SelectMany((BagConfig b) => b.Items)), StringComparer.OrdinalIgnoreCase);
		(List<string>, Side)[] oldLists = new (List<string>, Side)[2]
		{
			(Left, Side.Left),
			(Right, Side.Right)
		};
		foreach (var (oldList, side) in oldLists)
		{
			foreach (string key in oldList)
			{
				if (!string.IsNullOrWhiteSpace(key) && alreadyPlaced.Add(key))
				{
					GetPanel(primaryId, side).Items.Add(key);
				}
			}
		}
		Left.Clear();
		Right.Clear();
		PanelLayoutMigrated = true;
		if (hadOldLayout)
		{
			LayoutImported = true;
		}
	}

	public IEnumerable<List<string>> ItemLists()
	{
		yield return Left;
		yield return Right;
		foreach (PanelConfig panel in Panels)
		{
			yield return panel.Items;
		}
		foreach (BagConfig bag in Bags)
		{
			yield return bag.Items;
		}
	}

	public void RemoveItemEverywhere(string key)
	{
		foreach (List<string> item in ItemLists())
		{
			item.RemoveAll((string k) => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
		}
	}

	public void MoveItemsToPanel(IEnumerable<string> keys, PanelConfig target, string? beforeKey = null)
	{
		List<string> list = keys.Where((string k) => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		foreach (string item in list)
		{
			RemoveItemEverywhere(item);
		}
		int insertAt = ((beforeKey == null) ? (-1) : target.Items.FindIndex((string k) => string.Equals(k, beforeKey, StringComparison.OrdinalIgnoreCase)));
		if (insertAt >= 0)
		{
			target.Items.InsertRange(insertAt, list);
		}
		else
		{
			target.Items.AddRange(list);
		}
	}

	private static void MigrateFromOldDir()
	{
		try
		{
			string oldConfig = Path.Combine(OldDir, "config.json");
			if (File.Exists(FilePath) || !File.Exists(oldConfig))
			{
				return;
			}
			Directory.CreateDirectory(Dir);
			File.Copy(oldConfig, FilePath);
			string path = Path.Combine(OldDir, "images");
			if (!Directory.Exists(path))
			{
				return;
			}
			string newImages = Path.Combine(Dir, "images");
			Directory.CreateDirectory(newImages);
			string[] files = Directory.GetFiles(path);
			foreach (string image in files)
			{
				string dest = Path.Combine(newImages, Path.GetFileName(image));
				if (!File.Exists(dest))
				{
					File.Copy(image, dest);
				}
			}
		}
		catch (Exception ex)
		{
			App.Log(ex);
		}
	}

	private void FixImagePaths()
	{
		foreach (BagConfig bag in Bags)
		{
			if (!string.IsNullOrEmpty(bag.ImagePath) && !File.Exists(bag.ImagePath))
			{
				string local = Path.Combine(Dir, "images", Path.GetFileName(bag.ImagePath));
				if (File.Exists(local))
				{
					bag.ImagePath = local;
				}
			}
		}
	}

	public static AppConfig Load()
	{
		MigrateFromOldDir();
		try
		{
			if (File.Exists(FilePath))
			{
				AppConfig loaded = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts) ?? new AppConfig();
				loaded.FixImagePaths();
				return loaded;
			}
		}
		catch (Exception ex)
		{
			App.Log(ex);
			try
			{
				File.Copy(FilePath, FilePath + ".broken", overwrite: true);
			}
			catch
			{
			}
		}
		return new AppConfig();
	}

	public void Save()
	{
		try
		{
			Directory.CreateDirectory(Dir);
			// 임시 파일에 다 쓴 뒤 바꿔치기 (쓰다가 꺼져도 설정이 반쯤 깨지지 않게)
			string temp = FilePath + ".tmp";
			File.WriteAllText(temp, JsonSerializer.Serialize(this, Opts));
			File.Move(temp, FilePath, overwrite: true);
		}
		catch (Exception ex)
		{
			App.Log(ex);
		}
	}
}
