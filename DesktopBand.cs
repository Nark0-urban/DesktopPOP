using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DesktopPop;

/// <summary>
/// 화면 어디서든 끌면 나오는 선택 네모 (원래 바탕화면처럼).
/// 빈 바탕화면, 가장자리 판의 빈 곳, 펼친 가방의 빈 곳 어디서 시작해도 같은 네모.
/// 네모에 걸린 아이콘(열려 있는 가장자리 판 / 펼친 가방)이 선택됨.
/// </summary>
internal static class DesktopBand
{
    static BandOverlay? overlay;

    // ── 상태: 마우스 감시가 신호가 온 순서 그대로 "바로" 바꿈 (그래서 아무리 빨리 눌러도 순서가 안 꼬임) ──
    static bool buttonDown;                 // 지금 왼쪽 버튼이 눌려 있는지
    static bool down, active;
    static Native.POINT start, cur;
    static Action? onClick;
    static ISelHost? baseHost;
    static List<string> baseKeys = new();

    // ── 그리기·선택 계산은 무거워서 나중에 한 번에 ──
    readonly record struct Snap(Native.POINT A, Native.POINT B, ISelHost? BaseHost, List<string> BaseKeys);
    static Snap? finishSnap;               // 막 끝난 네모 (마지막 선택을 반영해야 함)
    static bool renderQueued, overlayShown;

    public static bool IsActive => down;

    static bool KeyDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
    static bool AddKeys() => KeyDown(0x11) || KeyDown(0x10); // Ctrl 또는 Shift

    /// <summary>가장자리 판 / 펼친 가방의 빈 곳을 눌렀을 때 (창 쪽에서 호출).</summary>
    public static void Begin(Native.POINT p, bool addToSelection, Action? clickAction)
    {
        // 그 사이 이미 버튼을 뗐으면(아주 빠른 클릭) 끌기가 아니라 그냥 클릭
        bool pressed = hook != IntPtr.Zero ? buttonDown : KeyDown(0x01);
        if (!pressed) { clickAction?.Invoke(); return; }
        if (!down) Start(p, addToSelection, clickAction);
    }

    /// <summary>안전장치용: 버튼을 뗐는데 네모가 남아 있으면 끝냄.</summary>
    public static void End()
    {
        buttonDown = false;
        if (down) OnUp();
    }

    static void Start(Native.POINT p, bool add, Action? clickAction)
    {
        down = true;
        active = false;
        start = cur = p;
        onClick = clickAction;
        baseHost = add ? Sel.Host : null;
        baseKeys = baseHost != null ? Sel.Ordered(baseHost) : new List<string>();
    }

    static void OnMove(Native.POINT p)
    {
        if (!down) return;
        cur = p;
        if (!active && Math.Abs(p.X - start.X) + Math.Abs(p.Y - start.Y) < 6) return;
        active = true;
        QueueRender();
    }

    static void OnUp()
    {
        down = false;
        if (active)
        {
            finishSnap = new Snap(start, cur, baseHost, baseKeys);
            active = false;
            QueueRender();
        }
        else
        {
            var click = onClick; // 끌지 않고 그냥 클릭
            dispatcher?.InvokeAsync(() => click?.Invoke(), DispatcherPriority.Input);
        }
    }

    static void QueueRender()
    {
        if (renderQueued || dispatcher == null) return;
        renderQueued = true;
        dispatcher.InvokeAsync(Render, DispatcherPriority.Input);
    }

    /// <summary>지금 상태대로 네모를 그리고 선택을 반영.</summary>
    static void Render()
    {
        renderQueued = false;
        try
        {
            if (finishSnap is Snap f) { finishSnap = null; ApplySelection(f); }
            if (down && active)
            {
                overlay ??= new BandOverlay();
                if (!overlayShown) { overlay.ShowBand(); overlayShown = true; }
                overlay.SetBand(start, cur);
                ApplySelection(new Snap(start, cur, baseHost, baseKeys));
            }
            else if (overlayShown)
            {
                overlay?.HideBand();
                overlayShown = false;
            }
        }
        catch (Exception ex) { App.Log(ex); }
    }

    /// <summary>네모(물리 픽셀)에 걸린 아이콘 선택 — 가장 많이 걸린 곳 하나만.</summary>
    static void ApplySelection(Snap s)
    {
        var band = new Rect(new Point(s.A.X, s.A.Y), new Point(s.B.X, s.B.Y));
        ISelHost? best = null;
        var bestHits = new List<string>();
        foreach (var host in App.Instance.SelHosts())
        {
            if (!host.BandSelectable) continue;
            var hits = host.OrderedKeys.Where(k => host.TileOf(k) is { } t && ScreenRect(t) is { } r && r.IntersectsWith(band)).ToList();
            if (hits.Count > bestHits.Count) { best = host; bestHits = hits; }
        }
        if (best != null) Sel.Set(best, best == s.BaseHost ? s.BaseKeys.Concat(bestHits) : bestHits);
        else if (s.BaseHost != null) Sel.Set(s.BaseHost, s.BaseKeys);
        else Sel.Clear();
    }

    static Rect? ScreenRect(FrameworkElement el)
    {
        if (!el.IsVisible || PresentationSource.FromVisual(el) == null) return null;
        try
        {
            var a = el.PointToScreen(new Point(0, 0));
            var b = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            return new Rect(a, b);
        }
        catch { return null; }
    }

    // ---------- 마우스 전역 감시 ----------

    delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc fn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    static IntPtr hook;
    static LowLevelMouseProc? proc; // 가비지 수집되지 않게 붙잡아 둠
    static Dispatcher? dispatcher;

    public static void Install()
    {
        if (hook != IntPtr.Zero) return;
        dispatcher = Dispatcher.CurrentDispatcher;
        proc = HookProc;
        hook = SetWindowsHookEx(14 /*WH_MOUSE_LL*/, proc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) App.Log($"마우스 감시 설치 실패 ({Marshal.GetLastWin32Error()})");
        buttonDown = KeyDown(0x01);
    }

    public static void Uninstall()
    {
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }

    static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var pt = Marshal.PtrToStructure<Native.POINT>(lParam); // MSLLHOOKSTRUCT 맨 앞이 좌표
                switch (wParam.ToInt32())
                {
                    case 0x201: // 왼쪽 버튼 누름
                        buttonDown = true;
                        // 빈 바탕화면에서 시작하는 네모는 가장자리가 열려 있거나 가방이 펼쳐져 있을 때만
                        if (!down && Native.IsDesktopAt(pt) && App.Instance.SelHosts().Any(h => h.BandSelectable))
                            Start(pt, AddKeys(), () => Sel.Clear());
                        break;
                    case 0x200: // 움직임
                        if (down) OnMove(pt);
                        break;
                    case 0x202: // 왼쪽 버튼 뗌
                        buttonDown = false;
                        if (down) OnUp();
                        break;
                }
            }
            catch { }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }
}

// <summary>선택 네모를 그리는 화면 전체 투명 창 (마우스는 뚫고 지나감).</summary>
internal sealed class BandOverlay : Window
{
    readonly Canvas canvas = new();
    readonly Rectangle rect;

    public BandOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Title = "DesktopPop Band";
        rect = new Rectangle { StrokeThickness = 1, RadiusX = 3, RadiusY = 3, IsHitTestVisible = false };
        canvas.Children.Add(rect);
        Content = canvas;
        SourceInitialized += (s, e) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            Native.MakeToolWindow(hwnd);
            const int GWL_EXSTYLE = -20;
            long ex = Native.GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
            ex |= 0x20 | 0x80000 | 0x08000000; // 마우스가 뚫고 지나감
            Native.SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
        };
    }

    public void ShowBand()
    {
        hideToken++;
        rect.Fill = Theme.Brush(Theme.Accent, 0x30);
        rect.Stroke = Theme.Brush(Theme.Accent, 0xCC);
        rect.Width = rect.Height = 0;
        rect.Visibility = Visibility.Visible;
        Left = 0; Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        if (!IsVisible) Show();
        Native.SetTopmost(new WindowInteropHelper(this).Handle);
    }

    public void SetBand(Native.POINT a, Native.POINT b)
    {
        double s = Native.DipScale();
        var r = new Rect(new Point(a.X / s, a.Y / s), new Point(b.X / s, b.Y / s));
        Canvas.SetLeft(rect, r.X);
        Canvas.SetTop(rect, r.Y);
        rect.Width = r.Width;
        rect.Height = r.Height;
    }

    int hideToken;

    public void HideBand()
    {
        rect.Visibility = Visibility.Hidden;
        rect.Width = rect.Height = 0;
        int token = ++hideToken;
        // 빈 화면이 한 번 그려진 다음에 창을 숨김
        Dispatcher.InvokeAsync(() => { if (token == hideToken) Hide(); }, DispatcherPriority.Background);
    }
}
