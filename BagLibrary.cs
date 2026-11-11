using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace DesktopPop;

/// <summary>가방 그림 보관함: data\library 폴더에 그림을 모아 둠.</summary>
internal static class BagLibrary
{
    public static string Dir => Path.Combine(AppConfig.Dir, "library");
    static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff" };

    public static bool IsImage(string path) => Exts.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>처음 한 번: 기본 그림을 만들어 넣고, 지금 가방들이 쓰는 그림도 넣어 둠.</summary>
    public static void EnsureSeeded(AppConfig cfg)
    {
        Directory.CreateDirectory(Dir);
        if (cfg.LibrarySeeded) return;
        WriteDefaults();
        var images = Path.Combine(AppConfig.Dir, "images");
        if (Directory.Exists(images))
        {
            int n = 1;
            var seen = new HashSet<string>();
            foreach (var f in Directory.GetFiles(images).Where(IsImage).OrderBy(File.GetCreationTimeUtc))
            {
                // 똑같은 그림은 한 번만
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)));
                if (seen.Add(hash)) CopyUnique(f, $"내 그림 {n++}");
            }
        }
        cfg.LibrarySeeded = true;
        cfg.Save();
    }

    /// <summary>기본 그림을 (다시) 그려서 넣음. 같은 이름이면 덮어씀.</summary>
    public static void WriteDefaults()
    {
        Directory.CreateDirectory(Dir);
        foreach (var (name, drawing) in DefaultIcons.All())
        {
            try { RenderPng(drawing, Path.Combine(Dir, name + ".png")); }
            catch (Exception ex) { App.Log(ex); }
        }
    }

    public static List<string> List()
    {
        Directory.CreateDirectory(Dir);
        return Directory.GetFiles(Dir).Where(IsImage)
            .OrderBy(f => File.GetCreationTimeUtc(f))
            .ThenBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>보관함에 보이는 이름 ("01 백팩" → "백팩").</summary>
    public static string DisplayName(string path)
    {
        var n = Path.GetFileNameWithoutExtension(path);
        return n.Length > 3 && char.IsDigit(n[0]) && char.IsDigit(n[1]) && n[2] == ' ' ? n[3..] : n;
    }

    public static List<string> Add(IEnumerable<string> files)
    {
        var added = new List<string>();
        foreach (var f in files.Where(f => File.Exists(f) && IsImage(f)))
        {
            try { added.Add(CopyUnique(f, Path.GetFileNameWithoutExtension(f))); }
            catch (Exception ex) { App.Log(ex); }
        }
        return added;
    }

    static string CopyUnique(string src, string baseName)
    {
        var ext = Path.GetExtension(src);
        var dest = Path.Combine(Dir, baseName + ext);
        for (int i = 2; File.Exists(dest); i++) dest = Path.Combine(Dir, $"{baseName} ({i}){ext}");
        File.Copy(src, dest);
        File.SetCreationTimeUtc(dest, DateTime.UtcNow); // 추가한 순서대로 뒤에 붙게
        return dest;
    }

    /// <summary>가방이 쓸 그림은 따로 복사해 둠 (보관함에서 지워도 가방 그림은 그대로 남게).</summary>
    public static string UseForBag(string libraryPath)
    {
        var dir = Path.Combine(AppConfig.Dir, "images");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, Guid.NewGuid().ToString("N") + Path.GetExtension(libraryPath));
        File.Copy(libraryPath, dest);
        return dest;
    }

    static void RenderPng(Drawing drawing, string path)
    {
        const int size = 256;
        var img = new Image
        {
            Source = new DrawingImage(drawing), Width = 222, Height = 222, Stretch = Stretch.Uniform,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 5, Direction = 270, Opacity = 0.35, Color = Colors.Black }
        };
        var host = new Grid { Width = size, Height = size };
        host.Children.Add(img);
        host.Measure(new Size(size, size));
        host.Arrange(new Rect(0, 0, size, size));
        host.UpdateLayout();
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(host);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}

/// <summary>기본으로 들어 있는 가방 그림들 (100×100 칸에 그린 벡터 그림).</summary>
internal static class DefaultIcons
{
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    static Brush V(string top, string bottom) => new LinearGradientBrush(C(top), C(bottom), new Point(0, 0), new Point(0, 1));
    static Brush S(string hex) => new SolidColorBrush(C(hex));
    static Pen P(string hex, double w = 1.8) => new(S(hex), w) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    static DrawingGroup Icon(params Drawing[] parts)
    {
        var g = new DrawingGroup();
        // 모든 그림이 같은 크기로 보이게 100×100 투명 바탕
        g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
        foreach (var p in parts) g.Children.Add(p);
        return g;
    }

    static GeometryDrawing D(string path, Brush? fill, Pen? pen = null) => new(fill, pen, Geometry.Parse(path));
    static GeometryDrawing R(double x, double y, double w, double h, double r, Brush? fill, Pen? pen = null) =>
        new(fill, pen, new RectangleGeometry(new Rect(x, y, w, h), r, r));
    static GeometryDrawing E(double cx, double cy, double rx, double ry, Brush? fill, Pen? pen = null) =>
        new(fill, pen, new EllipseGeometry(new Point(cx, cy), rx, ry));
    static Brush Shine => S("#40FFFFFF");

    public static IEnumerable<(string name, Drawing drawing)> All()
    {
        yield return ("폴더", Icon(
            D("M10,26 Q10,19 17,19 L40,19 L48,27 L83,27 Q90,27 90,34 L90,80 Q90,87 83,87 L17,87 Q10,87 10,80 Z", V("#F2B53A", "#C98A12"), P("#8A5A08")),
            D("M10,42 Q10,36 16,36 L84,36 Q90,36 90,42 L90,80 Q90,87 83,87 L17,87 Q10,87 10,80 Z", V("#FFD56A", "#F0A928"), P("#8A5A08")),
            R(16, 40, 68, 5, 2.5, Shine)));
    }
}
