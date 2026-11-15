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
			int num = 1;
			HashSet<string> hashSet = new HashSet<string>();
			foreach (string item2 in Directory.GetFiles(path).Where(IsImage).OrderBy(File.GetCreationTimeUtc))
			{
				string item = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(item2)));
				if (hashSet.Add(item))
				{
					CopyUnique(item2, $"내 그림 {num++}");
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
		bool flag = true;
		foreach (var item3 in CyberIcons.All().Concat(SteampunkIcons.All()))
		{
			string item = item3.name;
			Drawing item2 = item3.drawing;
			string path = Path.Combine(Dir, item + ".png");
			if (!File.Exists(path))
			{
				try
				{
					RenderPng(item2, path);
				}
				catch (Exception ex)
				{
					flag = false;
					App.Log(ex);
				}
			}
		}
		if (flag)
		{
			cfg.CyberLibrarySeeded = true;
			cfg.Save();
		}
	}

	/// <summary>그림 파일로 프로그램 안에 들어 있는 기본 가방 그림 (보관함 이름, 리소스 이름).</summary>
	private static readonly (string name, string resource)[] BundledImages = new (string, string)[1] { ("00 카프카", "DesktopPop.Assets.kafka.png") };

	/// <summary>들어 있는 그림 파일을 보관함에 꺼내 둠. 보관함 맨 앞에 보이게 만든 날짜를 앞당김.</summary>
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
				File.SetCreationTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
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
		WriteBundled(overwrite);
		Directory.CreateDirectory(Dir);
		foreach (var item3 in DefaultIcons.All().Concat(CyberIcons.All()).Concat(SteampunkIcons.All()))
		{
			string item = item3.name;
			Drawing item2 = item3.drawing;
			string path = Path.Combine(Dir, item + ".png");
			if (overwrite || !File.Exists(path))
			{
				try
				{
					RenderPng(item2, path);
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
		string text = Path.Combine(Dir, baseName + extension);
		int num = 2;
		while (File.Exists(text))
		{
			text = Path.Combine(Dir, $"{baseName} ({num}){extension}");
			num++;
		}
		File.Copy(src, text);
		File.SetCreationTimeUtc(text, DateTime.UtcNow);
		return text;
	}

	public static string UseForBag(string libraryPath)
	{
		string text = Path.Combine(AppConfig.Dir, "images");
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, Guid.NewGuid().ToString("N") + Path.GetExtension(libraryPath));
		File.Copy(libraryPath, text2);
		return text2;
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
