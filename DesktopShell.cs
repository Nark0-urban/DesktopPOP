using System;
using System.Collections.Generic;
using System.Text;

namespace DesktopPop;

/// <summary>진짜 바탕화면(탐색기) 아이콘 숨기기/보이기, 기존 배치 읽기.</summary>
internal static class DesktopShell
{
    public static IntPtr FindProgman() => Native.FindWindow("Progman", null);

    static IntPtr FindDefView()
    {
        var progman = FindProgman();
        var dv = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (dv != IntPtr.Zero) return dv;
        IntPtr found = IntPtr.Zero;
        Native.EnumWindows((h, l) =>
        {
            var sb = new StringBuilder(32);
            Native.GetClassName(h, sb, 32);
            if (sb.ToString() != "WorkerW") return true;
            var d = Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (d != IntPtr.Zero) { found = d; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static IntPtr FindListView()
    {
        var dv = FindDefView();
        return dv == IntPtr.Zero ? IntPtr.Zero : Native.FindWindowEx(dv, IntPtr.Zero, "SysListView32", null);
    }

    public static bool? IconsVisible()
    {
        var lv = FindListView();
        if (lv == IntPtr.Zero) return null;
        return Native.IsWindowVisible(lv);
    }

    /// <summary>바탕화면 우클릭 → 보기 → '바탕 화면 아이콘 표시'와 같은 동작.</summary>
    public static void SetIconsVisible(bool visible)
    {
        var cur = IconsVisible();
        if (cur == null || cur == visible) return;
        Native.SendMessage(FindDefView(), 0x0111 /*WM_COMMAND*/, new IntPtr(0x7402), IntPtr.Zero);
    }

    /// <summary>바탕화면 아이콘 이름 → 화면 좌표. (처음 실행 때 왼쪽/오른쪽 배치를 그대로 가져오려고)</summary>
    public static Dictionary<string, Native.POINT>? ReadIconPositions()
    {
        var lv = FindListView();
        if (lv == IntPtr.Zero) return null;
        Native.GetWindowThreadProcessId(lv, out uint pid);
        var proc = Native.OpenProcess(0x0008 | 0x0010 | 0x0020 | 0x0400, false, pid);
        if (proc == IntPtr.Zero) return null;
        IntPtr mem = IntPtr.Zero;
        try
        {
            mem = Native.VirtualAllocEx(proc, IntPtr.Zero, (UIntPtr)4096, 0x3000, 0x04);
            if (mem == IntPtr.Zero) return null;
            Native.GetWindowRect(lv, out var lvRect);
            int count = (int)Native.SendMessage(lv, 0x1004 /*LVM_GETITEMCOUNT*/, IntPtr.Zero, IntPtr.Zero);
            var result = new Dictionary<string, Native.POINT>();
            var ptBuf = new byte[8];
            var textBuf = new byte[1024];
            IntPtr textAddr = mem + 512;
            for (int i = 0; i < count; i++)
            {
                Native.SendMessage(lv, 0x1010 /*LVM_GETITEMPOSITION*/, new IntPtr(i), mem);
                Native.ReadProcessMemory(proc, mem, ptBuf, (UIntPtr)8, out _);
                var pt = new Native.POINT
                {
                    X = BitConverter.ToInt32(ptBuf, 0) + lvRect.Left,
                    Y = BitConverter.ToInt32(ptBuf, 4) + lvRect.Top
                };

                // LVITEMW (64비트 레이아웃)
                var lvi = new byte[88];
                BitConverter.GetBytes(1u).CopyTo(lvi, 0);                 // mask = LVIF_TEXT
                BitConverter.GetBytes(i).CopyTo(lvi, 4);                  // iItem
                BitConverter.GetBytes(textAddr.ToInt64()).CopyTo(lvi, 24); // pszText
                BitConverter.GetBytes(260).CopyTo(lvi, 32);               // cchTextMax
                Native.WriteProcessMemory(proc, mem, lvi, (UIntPtr)lvi.Length, out _);
                int len = (int)Native.SendMessage(lv, 0x1073 /*LVM_GETITEMTEXTW*/, new IntPtr(i), mem);
                Native.ReadProcessMemory(proc, textAddr, textBuf, (UIntPtr)textBuf.Length, out _);
                var name = Encoding.Unicode.GetString(textBuf, 0, Math.Clamp(len, 0, 511) * 2);
                if (name.Length > 0 && !result.ContainsKey(name)) result[name] = pt;
            }
            return result;
        }
        finally
        {
            if (mem != IntPtr.Zero) Native.VirtualFreeEx(proc, mem, UIntPtr.Zero, 0x8000);
            Native.CloseHandle(proc);
        }
    }
}
