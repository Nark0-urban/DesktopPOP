using System;
using System.Globalization;
using System.Windows.Media;

namespace DesktopPop;

/// <summary>
/// 전체 색상. 강조색(반짝임·고리·숫자 배지·끌어 넣기 표시)과 판 배경색 두 가지만 고르면
/// 나머지는 거기서 알아서 만들어짐. 기본값은 처음부터 쓰던 자주·분홍 그대로.
/// </summary>
internal static class Theme
{
    public const string DefaultAccent = "#FF6EB4";
    public const string DefaultPanel = "#1A1026";

    public static readonly (string name, string accent, string panel)[] Presets =
    {
        ("기본 (자주·분홍)", DefaultAccent, DefaultPanel),
        ("하늘 파랑", "#4FC3F7", "#0E1A26"),
        ("민트 초록", "#4ADE80", "#0E1F17"),
        ("노을 주황", "#FF9F43", "#241A10"),
        ("빨강", "#FF5A5F", "#241012"),
        ("라벤더 보라", "#B388FF", "#17112A"),
        ("흰색·회색", "#E5E7EB", "#18181B"),
    };

    public static Color Accent { get; private set; }   // 고리, 끌어 넣기 표시
    public static Color Badge { get; private set; }    // 가방 숫자 배지
    public static Color Glow { get; private set; }     // 가방 빛남
    public static Color Panel { get; private set; }    // 가장자리 판 배경
    public static Color Halo { get; private set; }     // 가방 펼칠 때 뒤에 깔리는 원
    public static Color Backdrop { get; private set; } // 가방에서 펼쳐진 아이콘 뒤 배경
    public static Color Hint { get; private set; }     // 빈 가방 안내 말풍선
    public static Color TrayA { get; private set; }    // 트레이 아이콘 그라데이션
    public static Color TrayB { get; private set; }

    static Theme() => Apply(null, null);

    public static void Apply(string? accentHex, string? panelHex)
    {
        var accent = Parse(accentHex) ?? Parse(DefaultAccent)!.Value;
        var panel = Parse(panelHex) ?? Parse(DefaultPanel)!.Value;
        Accent = accent;
        Panel = panel;

        bool defaultAccent = accent == Parse(DefaultAccent);
        bool defaultPanel = panel == Parse(DefaultPanel);

        // 기본 색이면 처음 쓰던 값 그대로, 아니면 고른 색에서 만들어 냄
        Badge = defaultAccent ? Color.FromRgb(0xE0, 0x45, 0x7B) : Shade(accent, -0.15);
        Glow = defaultAccent ? Color.FromRgb(0xFF, 0x96, 0xD2) : Shade(accent, 0.3);
        TrayA = defaultAccent ? Color.FromRgb(0xEC, 0x48, 0x99) : accent;
        TrayB = defaultAccent ? Color.FromRgb(0x7C, 0x3A, 0xED) : Shade(accent, -0.4);
        Halo = defaultPanel ? Color.FromRgb(0x14, 0x0A, 0x20) : Shade(panel, -0.25);
        Backdrop = defaultPanel ? Color.FromRgb(0x18, 0x10, 0x22) : panel;
        Hint = defaultPanel ? Color.FromRgb(0x22, 0x16, 0x30) : Shade(panel, 0.06);
    }

    public static Brush Brush(Color c, byte alpha = 255)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>amount &gt; 0 이면 흰색 쪽으로, &lt; 0 이면 검은색 쪽으로.</summary>
    public static Color Shade(Color c, double amount)
    {
        byte F(byte v) => amount >= 0
            ? (byte)Math.Round(v + (255 - v) * amount)
            : (byte)Math.Round(v * (1 + amount));
        return Color.FromRgb(F(c.R), F(c.G), F(c.B));
    }

    public static Color? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return null;
        return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
