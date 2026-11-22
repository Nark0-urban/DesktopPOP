using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace DesktopPop;

/// <summary>가방 그림 보관함(data\library): 기본 그림 넣기, 그림 추가·복사, 목록 순서.</summary>
internal static class BagLibrary
{
	private static readonly string[] Exts = new string[9] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff" };

	public static string Dir => Path.Combine(AppConfig.Dir, "library");

	public static bool IsImage(string path)
	{
		return Exts.Contains(Path.GetExtension(path).ToLowerInvariant());
	}

	public static void EnsureSeeded(AppConfig cfg)
	{
		Directory.CreateDirectory(Dir);
		if (cfg.LibrarySeeded)
		{
			return;
		}
		WriteDefaults(overwrite: false);
		string path = Path.Combine(AppConfig.Dir, "images");
		if (Directory.Exists(path))
		{
			// 가방들이 이미 쓰던 그림도 보관함에 "내 그림 N" 으로 넣어 둠 (똑같은 그림은 한 번만)
			int n = 1;
			HashSet<string> seenHashes = new HashSet<string>();
			foreach (string image in Directory.GetFiles(path).Where(IsImage).OrderBy(File.GetCreationTimeUtc))
			{
				string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(image)));
				if (seenHashes.Add(hash))
				{
					CopyUnique(image, $"내 그림 {n++}");
				}
			}
		}
		cfg.LibrarySeeded = true;
		cfg.Save();
	}

	public static void EnsureCyberSeeded(AppConfig cfg)
	{
		if (cfg.CyberLibrarySeeded)
		{
			return;
		}
		Directory.CreateDirectory(Dir);
		bool allWritten = true;
		foreach (var (name, drawing) in CyberIcons.All().Concat(SteampunkIcons.All()))
		{
			string path = Path.Combine(Dir, name + ".png");
			if (!File.Exists(path))
			{
				try
				{
					RenderPng(drawing, path);
				}
				catch (Exception ex)
				{
					allWritten = false;
					App.Log(ex);
				}
			}
		}
		if (allWritten)
		{
			cfg.CyberLibrarySeeded = true;
			cfg.Save();
		}
	}

	/// <summary>그림 파일로 프로그램 안에 들어 있는 기본 가방 그림 (보관함 이름, 리소스 이름).</summary>
	private static readonly (string name, string resource)[] BundledImages = new (string, string)[1] { ("00 카프카", "DesktopPop.Assets.kafka.png") };

	/// <summary>1.1.5 에서 카프카를 맨 앞에 두려고 쓰던 만든 날짜 (이 날짜면 새 자리로 옮김).</summary>
	private static readonly DateTime OldPinnedFirst = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	/// <summary>
	/// 보관함은 만든 날짜 순서로 보여서, 기본 그림(폴더·사이버·스팀펑크) 중 가장 늦은 것보다 1초 뒤로 두면
	/// 기본 그림들 맨 끝, 사용자가 추가한 그림들 앞에 옴.
	/// </summary>
	private static DateTime AfterBuiltIns()
	{
		List<DateTime> times = (from i in DefaultIcons.All().Concat(CyberIcons.All()).Concat(SteampunkIcons.All())
			select Path.Combine(Dir, i.name + ".png") into p
			where File.Exists(p)
			select File.GetCreationTimeUtc(p)).ToList();
		if (times.Count != 0)
		{
			return times.Max().AddSeconds(1.0);
		}
		return DateTime.UtcNow;
	}

	/// <summary>들어 있는 그림 파일을 보관함에 꺼내 둠. 기본 그림들 맨 끝에 보이게 함.</summary>
	private static void WriteBundled(bool overwrite)
	{
		Directory.CreateDirectory(Dir);
		foreach (var (name, resource) in BundledImages)
		{
			string path = Path.Combine(Dir, name + ".png");
			if (!overwrite && File.Exists(path))
			{
				continue;
			}
			try
			{
				using Stream? stream = typeof(BagLibrary).Assembly.GetManifestResourceStream(resource);
				if (stream == null)
				{
					App.Log("기본 그림 리소스를 찾지 못함: " + resource);
					continue;
				}
				using (FileStream file = File.Create(path))
				{
					stream.CopyTo(file);
				}
				File.SetCreationTimeUtc(path, AfterBuiltIns());
			}
			catch (Exception ex)
			{
				App.Log(ex);
			}
		}
	}

	/// <summary>새로 추가된 기본 그림(카프카)을 이미 쓰던 사람 보관함에도 한 번 넣어 줌. 지운 뒤엔 다시 안 넣음.</summary>
	public static void EnsureBundledSeeded(AppConfig cfg)
	{
		// 1.1.5 에서 맨 앞에 넣었던 카프카를 기본 그림들 맨 끝으로 옮김
		foreach (var (name, _) in BundledImages)
		{
			string path = Path.Combine(Dir, name + ".png");
			try
			{
				if (File.Exists(path) && File.GetCreationTimeUtc(path) < OldPinnedFirst)
				{
					File.SetCreationTimeUtc(path, AfterBuiltIns());
				}
			}
			catch (Exception ex)
			{
				App.Log(ex);
			}
		}
		if (cfg.KafkaLibrarySeeded)
		{
			return;
		}
		WriteBundled(overwrite: false);
		cfg.KafkaLibrarySeeded = true;
		cfg.Save();
	}

	public static void WriteDefaults(bool overwrite = true)
	{
		WriteDrawn(overwrite);
		WriteBundled(overwrite); // 그려 넣는 기본 그림들 다음 (맨 끝)
	}

	private static void WriteDrawn(bool overwrite)
	{
		Directory.CreateDirectory(Dir);
		foreach (var (name, drawing) in DefaultIcons.All().Concat(CyberIcons.All()).Concat(SteampunkIcons.All()))
		{
			string path = Path.Combine(Dir, name + ".png");
			if (overwrite || !File.Exists(path))
			{
				try
				{
					RenderPng(drawing, path);
				}
				catch (Exception ex)
				{
					App.Log(ex);
				}
			}
		}
	}

	public static List<string> List()
	{
		Directory.CreateDirectory(Dir);
		return (from f in Directory.GetFiles(Dir).Where(IsImage)
			orderby File.GetCreationTimeUtc(f)
			select f).ThenBy((string f) => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase).ToList();
	}

	public static string DisplayName(string path)
	{
		string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
		if (fileNameWithoutExtension.Length <= 3 || !char.IsDigit(fileNameWithoutExtension[0]) || !char.IsDigit(fileNameWithoutExtension[1]) || fileNameWithoutExtension[2] != ' ')
		{
			return fileNameWithoutExtension;
		}
		return fileNameWithoutExtension.Substring(3);
	}

	public static List<string> Add(IEnumerable<string> files)
	{
		List<string> list = new List<string>();
		foreach (string item in files.Where((string f) => File.Exists(f) && IsImage(f)))
		{
			try
			{
				list.Add(CopyUnique(item, Path.GetFileNameWithoutExtension(item)));
			}
			catch (Exception ex)
			{
				App.Log(ex);
			}
		}
		return list;
	}

	private static string CopyUnique(string src, string baseName)
	{
		string extension = Path.GetExtension(src);
		string dest = Path.Combine(Dir, baseName + extension);
		int n = 2;
		while (File.Exists(dest))
		{
			dest = Path.Combine(Dir, $"{baseName} ({n}){extension}");
			n++;
		}
		File.Copy(src, dest);
		File.SetCreationTimeUtc(dest, DateTime.UtcNow); // 추가한 순서대로 뒤에 붙게
		return dest;
	}

	public static string UseForBag(string libraryPath)
	{
		// 가방이 쓸 그림은 따로 복사해 둠 (보관함에서 지워도 가방 그림은 그대로 남게)
		string images = Path.Combine(AppConfig.Dir, "images");
		Directory.CreateDirectory(images);
		string copy = Path.Combine(images, Guid.NewGuid().ToString("N") + Path.GetExtension(libraryPath));
		File.Copy(libraryPath, copy);
		return copy;
	}

	private static void RenderPng(Drawing drawing, string path)
	{
		Image element = new Image
		{
			Source = new DrawingImage(drawing),
			Width = 344.0,
			Height = 344.0,
			Stretch = Stretch.Uniform,
			Effect = new DropShadowEffect
			{
				BlurRadius = 14.0,
				ShadowDepth = 5.0,
				Direction = 270.0,
				Opacity = 0.35,
				Color = Colors.Black
			}
		};
		Grid grid = new Grid
		{
			Width = 384.0,
			Height = 384.0
		};
		grid.Children.Add(element);
		grid.Measure(new Size(384.0, 384.0));
		grid.Arrange(new Rect(0.0, 0.0, 384.0, 384.0));
		grid.UpdateLayout();
		RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap(384, 384, 96.0, 96.0, PixelFormats.Pbgra32);
		renderTargetBitmap.Render(grid);
		PngBitmapEncoder pngBitmapEncoder = new PngBitmapEncoder();
		pngBitmapEncoder.Frames.Add(BitmapFrame.Create(renderTargetBitmap));
		using FileStream stream = File.Create(path);
		pngBitmapEncoder.Save(stream);
	}
}
