using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DesktopPop;

internal sealed class BagConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "가방";
    public string ImagePath { get; set; } = "";
    public double X { get; set; }          // 가방 중심 (화면 좌표)
    public double Y { get; set; }
    public double Size { get; set; } = 88;
    public List<string> Items { get; set; } = new();
}

internal sealed class AppConfig
{
    public List<string> Left { get; set; } = new();
    public List<string> Right { get; set; } = new();
    public List<BagConfig> Bags { get; set; } = new();
    public bool HideDesktopIcons { get; set; } = true;
    public bool DisableInFullscreen { get; set; } = true;
    public bool BlockWhenMaximized { get; set; } = true;
    public bool ShowStartupAnimation { get; set; } = true;
    public bool LibrarySeeded { get; set; }
    public bool SingleClickLaunch { get; set; } // 기본: 두 번 클릭해야 실행 (원래 바탕화면처럼)
    public string? AccentColor { get; set; }   // 비어 있으면 기본 자주·분홍
    public string? PanelColor { get; set; }
    public int EdgeDelayMs { get; set; } = 150;
    public bool LayoutImported { get; set; }
    public bool FirstRunDone { get; set; }

    /// <summary>
    /// 설정은 실행 파일 옆 data 폴더에 저장.
    /// (AppData에 두면 어떤 프로그램이 실행했느냐에 따라 Windows가 다른 곳으로 몰래 옮겨 저장하는 경우가 있어서)
    /// </summary>
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "data");
    static string FilePath => Path.Combine(Dir, "config.json");
    static string OldDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopPop");

    /// <summary>예전 위치(AppData)에 설정이 있으면 data 폴더로 옮겨 옴.</summary>
    static void MigrateFromOldDir()
    {
        try
        {
            var old = Path.Combine(OldDir, "config.json");
            if (File.Exists(FilePath) || !File.Exists(old)) return;
            Directory.CreateDirectory(Dir);
            File.Copy(old, FilePath);
            var oldImages = Path.Combine(OldDir, "images");
            if (Directory.Exists(oldImages))
            {
                var newImages = Path.Combine(Dir, "images");
                Directory.CreateDirectory(newImages);
                foreach (var f in Directory.GetFiles(oldImages))
                {
                    var dest = Path.Combine(newImages, Path.GetFileName(f));
                    if (!File.Exists(dest)) File.Copy(f, dest);
                }
            }
        }
        catch (Exception ex) { App.Log(ex); }
    }

    /// <summary>가방 그림 경로가 옛 위치를 가리키면 data\images 안의 같은 파일로 고침.</summary>
    void FixImagePaths()
    {
        foreach (var b in Bags)
        {
            if (string.IsNullOrEmpty(b.ImagePath) || File.Exists(b.ImagePath)) continue;
            var local = Path.Combine(Dir, "images", Path.GetFileName(b.ImagePath));
            if (File.Exists(local)) b.ImagePath = local;
        }
    }

    static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static AppConfig Load()
    {
        MigrateFromOldDir();
        try
        {
            if (File.Exists(FilePath))
            {
                var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Opts) ?? new AppConfig();
                cfg.FixImagePaths();
                return cfg;
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
            try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
        }
        return new AppConfig();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Opts));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex) { App.Log(ex); }
    }
}
