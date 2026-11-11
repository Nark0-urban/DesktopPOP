using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DesktopPop;

/// <summary>
/// 선택된 아이콘에 원래 바탕화면 같은 단축키.
/// Delete = 휴지통, Shift+Delete = 완전 삭제, F2 = 이름 바꾸기, Enter = 열기,
/// Ctrl+A = 전체 선택, Ctrl+C / Ctrl+X = 복사 / 잘라내기, Esc = 선택 풀기.
/// 선택된 게 있고, 바탕화면이나 DesktopPop 창이 앞에 있을 때만 키를 가져감
/// (다른 프로그램에서 타자 칠 때는 절대 안 건드림).
/// </summary>
internal static class KeyboardHook
{
    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    static IntPtr hook;
    static LowLevelKeyboardProc? proc; // 가비지 수집되지 않게 붙잡아 둠
    static Dispatcher? dispatcher;

    public static void Install()
    {
        if (hook != IntPtr.Zero) return;
        dispatcher = Dispatcher.CurrentDispatcher;
        proc = HookProc;
        hook = SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, proc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) App.Log($"키보드 감시 설치 실패 ({Marshal.GetLastWin32Error()})");
    }

    public static void Uninstall()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    static bool Down(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>지금 키를 가져가도 되는지: 바탕화면 또는 DesktopPop 창이 맨 앞.</summary>
    static bool OurTurn()
    {
        if (Native.IsDesktopForeground()) return true;
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            int msg = wParam.ToInt32();
            if (nCode >= 0 && (msg == 0x100 || msg == 0x104) && Sel.Host is { } host && Sel.Keys.Count > 0 && OurTurn())
            {
                int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT 맨 앞이 키 코드
                bool ctrl = Down(0x11), shift = Down(0x10), alt = Down(0x12);
                Action? act = null;
                if (!ctrl && !alt)
                {
                    switch (vk)
                    {
                        case 0x2E: act = shift ? () => App.Instance.DeleteSelected(host, permanent: true)
                                               : () => App.Instance.DeleteSelected(host, permanent: false); break; // Delete
                        case 0x71: if (!shift) act = () => App.Instance.RenameSelected(host); break;                // F2
                        case 0x0D: if (!shift) act = () => App.Instance.OpenSelected(host); break;                  // Enter
                        case 0x1B: act = () => Sel.Clear(); break;                                                  // Esc
                    }
                }
                else if (ctrl && !alt && !shift)
                {
                    switch (vk)
                    {
                        case 0x41: act = () => Sel.Set(host, host.OrderedKeys); break;                // Ctrl+A
                        case 0x43: act = () => App.Instance.CopySelected(host, cut: false); break;     // Ctrl+C
                        case 0x58: act = () => App.Instance.CopySelected(host, cut: true); break;      // Ctrl+X
                    }
                }
                if (act != null)
                {
                    dispatcher?.InvokeAsync(act, DispatcherPriority.Input);
                    return new IntPtr(1); // 이 키는 우리가 처리 (숨겨진 원래 바탕화면이 엉뚱한 걸 지우지 않게)
                }
            }
        }
        catch { }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }
}
