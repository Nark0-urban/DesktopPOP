using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace DesktopPop;

/// <summary>
/// 사용자가 data\fonts 폴더에 넣은 폰트(.ttf/.otf)로 가방 이름 글자를 바꿈 (다른 글자는 Windows 기본 글꼴 그대로).
/// 한글이 없는 폰트(영문용)를 앞에, 한글이 있는 폰트를 뒤에 두어서 영문·숫자는 영문 폰트로, 한글은 한글 폰트로 보임.
/// 폰트 파일은 배포 zip 에 넣지 않음 (폰트마다 라이선스가 달라서, 각자 가진 폰트를 넣어 쓰게).
/// </summary>
internal static class UiFont
{
	public static string Dir => Path.Combine(AppConfig.Dir, "fonts");

	/// <summary>적용한 폰트 이름들 (없으면 빈 목록).</summary>
	public static IReadOnlyList<string> Applied { get; private set; } = Array.Empty<string>();

	/// <summary>가방 이름에 쓸 글꼴 (폰트를 안 넣었으면 null = 기본 글꼴).</summary>
	public static FontFamily? BagNameFamily { get; private set; }

	/// <summary>가방 창을 만들기 전에 불러야 함.</summary>
	public static void Apply()
	{
		try
		{
			if (!Directory.Exists(Dir))
			{
				return;
			}
			// 폰트 파일에 적힌 이름이 아니라 WPF 가 읽은 이름을 써야 찾을 수 있음
			// (예: 굵기가 "9 Black" 인 폰트는 WPF 에서 "SDK_KR_Web 9" 같은 이름이 됨)
			Uri folder = new Uri(Dir.TrimEnd('\\') + "\\");
			List<(string source, bool hasHangul)> fonts = new List<(string, bool)>();
			foreach (FontFamily found in Fonts.GetFontFamilies(folder))
			{
				try
				{
					bool hasHangul = false;
					bool usable = false;
					foreach (Typeface typeface in found.GetTypefaces())
					{
						if (typeface.TryGetGlyphTypeface(out GlyphTypeface glyphs))
						{
							usable = true;
							hasHangul |= glyphs.CharacterToGlyphMap.ContainsKey(0xAC00); // '가'
						}
					}
					if (usable && !fonts.Any(((string source, bool hasHangul) x) => x.source == found.Source))
					{
						fonts.Add((found.Source, hasHangul));
					}
				}
				catch (Exception ex)
				{
					App.Log($"폰트를 읽지 못함: {found.Source} ({ex.Message})");
				}
			}
			if (fonts.Count == 0)
			{
				return;
			}
			// 영문용(한글 없음) → 한글용 → Windows 기본 (빠진 글자는 뒤 폰트에서 찾음)
			List<string> ordered = fonts.OrderBy(((string source, bool hasHangul) f) => f.hasHangul).Select(((string source, bool hasHangul) f) => f.source).ToList();
			string spec = string.Join(", ", ordered) + ", Malgun Gothic, Segoe UI";
			FontFamily family2 = new FontFamily(folder, spec);
			// 가방 이름에만 씀 (BagWindow 가 BagNameFamily 를 읽음). 나머지 글자는 Windows 기본 글꼴 그대로.
			BagNameFamily = family2;
			Applied = ordered;
			App.Diag("  폰트 적용: " + string.Join(", ", ordered));
		}
		catch (Exception ex2)
		{
			App.Log(ex2);
		}
	}
}
