using System;
using System.Globalization;
using System.Windows.Media;

namespace DesktopPop;

/// <summary>색상 테마 (강조색·판 배경색과 거기서 나오는 색들).</summary>
internal static class Theme
{
	public const string DefaultAccent = "#FF6EB4";

	public const string DefaultPanel = "#1A1026";

	public static readonly (string name, string accent, string panel)[] Presets;

	public static Color Accent { get; private set; }

	public static Color Badge { get; private set; }

	public static Color Glow { get; private set; }

	public static Color Panel { get; private set; }

	public static Color Halo { get; private set; }

	public static Color Backdrop { get; private set; }

	public static Color Hint { get; private set; }

	public static Color TrayA { get; private set; }

	public static Color TrayB { get; private set; }

	static Theme()
	{
		Presets = new (string, string, string)[7]
		{
			("기본 (자주·분홍)", "#FF6EB4", "#1A1026"),
			("하늘 파랑", "#4FC3F7", "#0E1A26"),
			("민트 초록", "#4ADE80", "#0E1F17"),
			("노을 주황", "#FF9F43", "#241A10"),
			("빨강", "#FF5A5F", "#241012"),
			("라벤더 보라", "#B388FF", "#17112A"),
			("흰색·회색", "#E5E7EB", "#18181B")
		};
		Apply(null, null);
	}

	public static void Apply(string? accentHex, string? panelHex)
	{
		Color color = Parse(accentHex) ?? Parse("#FF6EB4").Value;
		Color color2 = Parse(panelHex) ?? Parse("#1A1026").Value;
		Accent = color;
		Panel = color2;
		Color value = color;
		Color? color3 = Parse("#FF6EB4");
		bool defaultAccent = value == color3;
		value = color2;
		color3 = Parse("#1A1026");
		bool defaultPanel = value == color3;
		Badge = (defaultAccent ? Color.FromRgb(224, 69, 123) : Shade(color, -0.15));
		Glow = (defaultAccent ? Color.FromRgb(byte.MaxValue, 150, 210) : Shade(color, 0.3));
		TrayA = (defaultAccent ? Color.FromRgb(236, 72, 153) : color);
		TrayB = (defaultAccent ? Color.FromRgb(124, 58, 237) : Shade(color, -0.4));
		Halo = (defaultPanel ? Color.FromRgb(20, 10, 32) : Shade(color2, -0.25));
		Backdrop = (defaultPanel ? Color.FromRgb(24, 16, 34) : color2);
		Hint = (defaultPanel ? Color.FromRgb(34, 22, 48) : Shade(color2, 0.06));
	}

	public static Brush Brush(Color c, byte alpha = byte.MaxValue)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	public static Color WithAlpha(Color c, byte alpha)
	{
		return Color.FromArgb(alpha, c.R, c.G, c.B);
	}

	public static Color Shade(Color c, double amount)
	{
		return Color.FromRgb(F(c.R), F(c.G), F(c.B));
		byte F(byte v)
		{
			if (!(amount >= 0.0))
			{
				return (byte)Math.Round((double)(int)v * (1.0 + amount));
			}
			return (byte)Math.Round((double)(int)v + (double)(255 - v) * amount);
		}
	}

	public static Color? Parse(string? hex)
	{
		if (string.IsNullOrWhiteSpace(hex))
		{
			return null;
		}
		string digits = hex.Trim().TrimStart('#');
		if (digits.Length != 6 || !int.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
		{
			return null;
		}
		return Color.FromRgb((byte)(result >> 16), (byte)(result >> 8), (byte)result);
	}

	public static string ToHex(Color c)
	{
		return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
	}
}
