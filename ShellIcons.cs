using System;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopPop;

[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItem
{
    void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
    void GetParent(out IShellItem ppsi);
    void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
    void GetAttributes(uint mask, out uint attribs);
    void Compare(IShellItem psi, uint hint, out int order);
}

[ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellItemImageFactory
{
    [PreserveSig] int GetImage(Native.SIZE size, int flags, out IntPtr phbm);
}

/// <summary>윈도우 탐색기와 같은 이름·아이콘(썸네일)을 가져옴.</summary>
internal static class ShellIcons
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [In] ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    public static (string? name, BitmapSource? icon) Load(string parsingName, int px)
    {
        IShellItem? item = null;
        try
        {
            var iid = typeof(IShellItem).GUID;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out item);
            string? name = null;
            try { item.GetDisplayName(0 /*SIGDN_NORMALDISPLAY*/, out name); } catch { }

            BitmapSource? icon = null;
            if (item is IShellItemImageFactory f &&
                f.GetImage(new Native.SIZE { cx = px, cy = px }, 0, out var hbmp) == 0 && hbmp != IntPtr.Zero)
            {
                try { icon = FromHBitmap(hbmp); }
                finally { Native.DeleteObject(hbmp); }
            }
            return (name, icon);
        }
        catch
        {
            return (null, null);
        }
        finally
        {
            if (item != null) Marshal.ReleaseComObject(item);
        }
    }

    static BitmapSource? FromHBitmap(IntPtr hbmp)
    {
        Native.GetObject(hbmp, Marshal.SizeOf<Native.BITMAP>(), out var bm);
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        if (w <= 0 || h <= 0) return null;
        var bi = new Native.BITMAPINFO { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bits = new byte[w * h * 4];
        var dc = Native.GetDC(IntPtr.Zero);
        try
        {
            if (Native.GetDIBits(dc, hbmp, 0, (uint)h, bits, ref bi, 0) == 0) return null;
        }
        finally { Native.ReleaseDC(IntPtr.Zero, dc); }

        bool hasAlpha = false;
        for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { hasAlpha = true; break; }
        if (!hasAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;

        var src = BitmapSource.Create(w, h, 96, 96, hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgra32, null, bits, w * 4);
        src.Freeze();
        return src;
    }
}
