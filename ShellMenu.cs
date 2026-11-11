using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DesktopPop;

/// <summary>DesktopPop 전용 메뉴 항목 (Windows 원래 메뉴 위에 붙음).</summary>
internal sealed class MenuEntry
{
    public string Text = "";
    public Action? Run;
    public List<MenuEntry>? Children;
    public bool IsSeparator;

    public static MenuEntry Item(string text, Action run) => new() { Text = text, Run = run };
    public static MenuEntry Sub(string text, List<MenuEntry> children) => new() { Text = text, Children = children };
    public static MenuEntry Separator => new() { IsSeparator = true };
}

/// <summary>
/// 원래 바탕화면에서 아이콘 우클릭할 때 나오는 Windows 메뉴를 그대로 띄움.
/// (열기, 연결 프로그램, 보내기, 잘라내기, 복사, 삭제, 이름 바꾸기, 속성 …)
/// </summary>
internal static class ShellMenu
{
    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr eaten, out IntPtr pidl, IntPtr attrs);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint inOut);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref Guid riid, IntPtr reserved, out IntPtr ppv);
        [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr pidlOut);
    }

    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
    }

    [ComImport, Guid("000214F4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu2
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport, Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IContextMenu3
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint type, IntPtr reserved, IntPtr name, uint cchMax);
        [PreserveSig] int HandleMenuMsg(uint msg, IntPtr wParam, IntPtr lParam);
        [PreserveSig] int HandleMenuMsg2(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.LPStr)] public string? lpTitle;
        public IntPtr lpVerbW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParametersW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectoryW;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpTitleW;
        public Native.POINT ptInvoke;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHParseDisplayName(string name, IntPtr pbc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
    [DllImport("shell32.dll")] static extern int SHBindToParent(IntPtr pidl, [In] ref Guid riid, out IntPtr ppv, out IntPtr pidlLast);
    [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr pv);
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool InsertMenu(IntPtr h, uint pos, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenu(IntPtr h, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] static extern int TrackPopupMenuEx(IntPtr h, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);

    const uint MF_STRING = 0, MF_POPUP = 0x10, MF_SEPARATOR = 0x800, MF_BYPOSITION = 0x400;
    const uint CMF_NORMAL = 0, CMF_CANRENAME = 0x10, CMF_EXTENDEDVERBS = 0x100;
    const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 0x2;
    const uint ShellFirst = 1, ShellLast = 0x7FFF, OurFirst = 0x8000;

    static IContextMenu2? cm2;
    static IContextMenu3? cm3;

    /// <summary>
    /// paths 들에 대한 Windows 원래 메뉴를 띄움. extras 는 맨 위에 붙는 DesktopPop 전용 항목.
    /// onRename: "이름 바꾸기"를 고르면 대신 할 일 (탐색기 창이 아니라서 Windows가 직접 못 함).
    /// 실패하면 false (그때는 예전 메뉴로).
    /// </summary>
    public static bool Show(Window owner, IReadOnlyList<string> paths, List<MenuEntry> extras, Action? onRename)
    {
        var hwnd = new WindowInteropHelper(owner).Handle;
        if (hwnd == IntPtr.Zero || paths.Count == 0) return false;

        // 같은 폴더에 있는 것끼리만 한 메뉴로 (내 바탕화면 / 공용 바탕화면이 섞이면 우클릭한 쪽 기준)
        string ParentOf(string p) => p.StartsWith("::") ? "::" : Path.GetDirectoryName(p.TrimEnd('\\')) ?? "";
        var parentDir = ParentOf(paths[0]);
        var group = paths.Where(p => string.Equals(ParentOf(p), parentDir, StringComparison.OrdinalIgnoreCase)).ToList();

        var absPidls = new List<IntPtr>();
        IShellFolder? parent = null;
        IContextMenu? cm = null;
        IntPtr hMenu = IntPtr.Zero;
        var ourActions = new Dictionary<uint, Action>();
        HwndSource? source = null;
        try
        {
            var children = new List<IntPtr>();
            var iidFolder = typeof(IShellFolder).GUID;
            foreach (var p in group)
            {
                if (SHParseDisplayName(p, IntPtr.Zero, out var pidl, 0, out _) != 0 || pidl == IntPtr.Zero) continue;
                absPidls.Add(pidl);
                if (SHBindToParent(pidl, ref iidFolder, out var ppv, out var child) != 0) continue;
                if (parent == null) parent = (IShellFolder)Marshal.GetObjectForIUnknown(ppv);
                Marshal.Release(ppv);
                children.Add(child); // 부모 pidl 안을 가리키는 포인터라 따로 해제 안 함
            }
            if (parent == null || children.Count == 0) return false;

            var iidMenu = typeof(IContextMenu).GUID;
            if (parent.GetUIObjectOf(hwnd, (uint)children.Count, children.ToArray(), ref iidMenu, IntPtr.Zero, out var pcm) != 0) return false;
            cm = (IContextMenu)Marshal.GetObjectForIUnknown(pcm);
            Marshal.Release(pcm);
            cm2 = cm as IContextMenu2;
            cm3 = cm as IContextMenu3;

            hMenu = CreatePopupMenu();
            uint flags = CMF_NORMAL | (children.Count == 1 ? CMF_CANRENAME : 0);
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) flags |= CMF_EXTENDEDVERBS; // Shift+우클릭 = 확장 메뉴
            if (cm.QueryContextMenu(hMenu, 0, ShellFirst, ShellLast, flags) < 0) return false;

            // DesktopPop 전용 항목을 맨 위에
            uint nextId = OurFirst;
            uint pos = 0;
            foreach (var e in extras) InsertEntry(hMenu, ref pos, e, ref nextId, ourActions);
            if (extras.Count > 0) InsertMenu(hMenu, pos, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);

            // "보내기", "연결 프로그램" 같은 하위 메뉴가 채워지려면 메뉴 메시지를 넘겨 줘야 함
            source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(Hook);

            Native.GetCursorPos(out var pt);
            SetForegroundWindow(hwnd);
            App.OpenMenus++;
            int cmd;
            try { cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.X, pt.Y, hwnd, IntPtr.Zero); }
            finally { App.OpenMenus = Math.Max(0, App.OpenMenus - 1); }
            PostMessage(hwnd, 0 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero);
            source?.RemoveHook(Hook);
            source = null;

            if (cmd <= 0) return true;
            if (ourActions.TryGetValue((uint)cmd, out var act))
            {
                owner.Dispatcher.InvokeAsync(act);
                return true;
            }

            var verb = VerbOf(cm, (uint)cmd - ShellFirst);
            if (string.Equals(verb, "rename", StringComparison.OrdinalIgnoreCase) && onRename != null)
            {
                owner.Dispatcher.InvokeAsync(onRename);
                return true;
            }

            var info = new CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                fMask = 0x4000 /*UNICODE*/ | 0x20000000 /*PTINVOKE*/
                        | ((Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 0x40000000u : 0)
                        | ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? 0x10000000u : 0),
                hwnd = hwnd,
                lpVerb = new IntPtr(cmd - ShellFirst),
                lpVerbW = new IntPtr(cmd - ShellFirst),
                nShow = 1,
                ptInvoke = pt
            };
            cm.InvokeCommand(ref info);
            App.Instance.ScheduleRefresh();
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return false;
        }
        finally
        {
            source?.RemoveHook(Hook);
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            cm2 = null;
            cm3 = null;
            if (cm != null) Marshal.ReleaseComObject(cm);
            if (parent != null) Marshal.ReleaseComObject(parent);
            foreach (var p in absPidls) CoTaskMemFree(p);
        }
    }

    static void InsertEntry(IntPtr menu, ref uint pos, MenuEntry e, ref uint nextId, Dictionary<uint, Action> actions)
    {
        if (e.IsSeparator)
        {
            InsertMenu(menu, pos++, MF_BYPOSITION | MF_SEPARATOR, UIntPtr.Zero, null);
            return;
        }
        if (e.Children != null)
        {
            var sub = CreatePopupMenu(); // 부모 메뉴를 없앨 때 같이 없어짐
            uint subPos = 0;
            foreach (var c in e.Children) InsertEntry(sub, ref subPos, c, ref nextId, actions);
            InsertMenu(menu, pos++, MF_BYPOSITION | MF_POPUP | MF_STRING, (UIntPtr)(ulong)sub.ToInt64(), e.Text);
            return;
        }
        uint id = nextId++;
        if (e.Run != null) actions[id] = e.Run;
        InsertMenu(menu, pos++, MF_BYPOSITION | MF_STRING, (UIntPtr)id, e.Text);
    }

    static string? VerbOf(IContextMenu cm, uint offset)
    {
        var buf = Marshal.AllocHGlobal(520);
        try
        {
            return cm.GetCommandString((UIntPtr)offset, 4 /*GCS_VERBW*/, IntPtr.Zero, buf, 260) == 0 ? Marshal.PtrToStringUni(buf) : null;
        }
        catch { return null; }
        finally { Marshal.FreeHGlobal(buf); }
    }

    static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_INITMENUPOPUP, WM_DRAWITEM, WM_MEASUREITEM, WM_MENUCHAR
        if (msg is not (0x117 or 0x2B or 0x2C or 0x120)) return IntPtr.Zero;
        try
        {
            if (cm3 != null && cm3.HandleMenuMsg2((uint)msg, wParam, lParam, out var r) == 0) { handled = true; return r; }
            if (cm2 != null && msg != 0x120 && cm2.HandleMenuMsg((uint)msg, wParam, lParam) == 0) { handled = true; return IntPtr.Zero; }
        }
        catch { }
        return IntPtr.Zero;
    }
}
