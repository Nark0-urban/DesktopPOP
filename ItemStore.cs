using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace DesktopPop;

internal enum Side { Left, Right }

internal sealed class DesktopItem
{
    public string Key = "";
    public string Path = "";
    public string Name = "";
    public ImageSource? Icon;
    public bool IsVirtual;
    public DateTime Stamp;
}

/// <summary>바탕화면 폴더의 파일들 + 가방에 넣은 파일들을 읽어 둠.</summary>
internal sealed class ItemStore
{
    public const string RecycleBinKey = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    static readonly StringComparer Cmp = StringComparer.OrdinalIgnoreCase;

    readonly Dictionary<string, DesktopItem> items = new(Cmp);
    readonly List<string> desktopKeys = new();
    readonly HashSet<string> desktopSet = new(Cmp);

    public static string[] DesktopDirs => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        }
        .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d))
        .Distinct(Cmp).ToArray();

    public void Refresh(AppConfig cfg)
    {
        desktopKeys.Clear();
        desktopSet.Clear();
        AddDesktopKey(RecycleBinKey);
        foreach (var dir in DesktopDirs)
        {
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    try
                    {
                        var attr = File.GetAttributes(entry);
                        if ((attr & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    }
                    catch { continue; }
                    AddDesktopKey(entry);
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        var wanted = new HashSet<string>(desktopSet, Cmp);
        foreach (var bag in cfg.Bags)
            foreach (var k in bag.Items)
                if (Exists(k)) wanted.Add(k);

        foreach (var k in items.Keys.Where(k => !wanted.Contains(k)).ToList()) items.Remove(k);
        foreach (var k in wanted)
        {
            var stamp = StampOf(k);
            if (!items.TryGetValue(k, out var it) || it.Stamp != stamp)
                items[k] = Load(k, stamp);
        }
    }

    void AddDesktopKey(string k)
    {
        if (desktopSet.Add(k)) desktopKeys.Add(k);
    }

    static bool Exists(string k) => k.StartsWith("::") || File.Exists(k) || Directory.Exists(k);

    static DateTime StampOf(string k)
    {
        if (k.StartsWith("::")) return DateTime.MinValue;
        try { return File.GetLastWriteTimeUtc(k); } catch { return DateTime.MinValue; }
    }

    static DesktopItem Load(string key, DateTime stamp)
    {
        var (name, icon) = ShellIcons.Load(key, 96);
        return new DesktopItem
        {
            Key = key,
            Path = key,
            Name = string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileNameWithoutExtension(key) : name!,
            Icon = icon,
            IsVirtual = key.StartsWith("::"),
            Stamp = stamp
        };
    }

    public DesktopItem? Get(string key) => items.TryGetValue(key, out var i) ? i : null;
    public bool IsOnDesktop(string key) => desktopSet.Contains(key);
    public IReadOnlyList<string> DesktopKeyList => desktopKeys;
    public IEnumerable<DesktopItem> DesktopItems => desktopKeys.Select(Get).Where(i => i != null).Select(i => i!);

    /// <summary>어디에도 배정 안 된 새 아이콘은 왼쪽 가장자리 끝에 붙임.</summary>
    public void AssignNew(AppConfig cfg)
    {
        var used = new HashSet<string>(cfg.Left.Concat(cfg.Right).Concat(cfg.Bags.SelectMany(b => b.Items)), Cmp);
        foreach (var k in desktopKeys)
            if (used.Add(k)) cfg.Left.Add(k);
    }

    public List<DesktopItem> VisibleFor(AppConfig cfg, Side side)
    {
        var list = side == Side.Left ? cfg.Left : cfg.Right;
        var seen = new HashSet<string>(Cmp);
        var result = new List<DesktopItem>();
        foreach (var k in list)
        {
            if (!desktopSet.Contains(k) || !seen.Add(k)) continue;
            var it = Get(k);
            if (it != null) result.Add(it);
        }
        return result;
    }
}

internal static class Shell
{
    public static void Launch(DesktopItem item)
    {
        try
        {
            if (item.Key == ItemStore.RecycleBinKey)
                Process.Start(new ProcessStartInfo("explorer.exe", "shell:RecycleBinFolder") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { /* 사용자가 취소 */ }
        catch (Exception ex)
        {
            MessageBox.Show($"열 수 없어요:\n{item.Name}\n\n{ex.Message}", "DesktopPop");
        }
    }

    public static void OpenLocation(DesktopItem item)
    {
        if (item.IsVirtual) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.Path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); }
    }
}
