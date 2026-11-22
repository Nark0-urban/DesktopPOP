using System;
using System.Collections.Generic;
using System.IO;

namespace DesktopPop;

/// <summary>파일 이름·경로 관련 계산 (복사본 이름, 이름 검사, 이름 바꾼 뒤 경로 고치기).</summary>
internal static class FileOperations
{
	/// <summary>
	/// 같은 폴더에 복사할 때 쓸 새 경로: "이름.txt" → "이름 - 복사본.txt" → "이름 - 복사본 (2).txt" …
	/// </summary>
	public static string UniqueCopyPath(string source, string destinationDirectory, bool isDirectory, Func<string, bool>? exists = null)
	{
		if (exists == null)
		{
			exists = (string path) => File.Exists(path) || Directory.Exists(path);
		}
		string fileName = Path.GetFileName(source.TrimEnd(new char[2]
		{
			Path.DirectorySeparatorChar,
			Path.AltDirectorySeparatorChar
		}));
		string extension = (isDirectory ? "" : Path.GetExtension(fileName));
		string stem = (isDirectory ? fileName : Path.GetFileNameWithoutExtension(fileName));
		string candidate = Path.Combine(destinationDirectory, fileName);
		if (!exists(candidate))
		{
			return candidate;
		}
		candidate = Path.Combine(destinationDirectory, stem + " - 복사본" + extension);
		int n = 2;
		while (exists(candidate))
		{
			candidate = Path.Combine(destinationDirectory, $"{stem} - 복사본 ({n}){extension}");
			n++;
		}
		return candidate;
	}

	/// <summary>새 폴더·이름 바꾸기에 쓸 수 있는 이름인지 (경로 문자, 끝의 점·공백, CON·COM1 같은 예약 이름 금지).</summary>
	public static bool TryValidateName(string name, out string error)
	{
		error = "";
		bool emptyOrDots = string.IsNullOrWhiteSpace(name) || name == "." || name == "..";
		if (emptyOrDots || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') || name.EndsWith('.') || name.EndsWith(' '))
		{
			error = "이름에는 경로나 \\ / : * ? \" < > | 문자를 쓸 수 없고, 끝에 점이나 공백을 넣을 수 없어요.";
			return false;
		}
		// 확장자와 상관없이 예약된 장치 이름 (예: "con.txt" 도 안 됨)
		string baseName = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
		bool reserved = baseName is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
			|| (baseName.Length == 4 && (baseName.StartsWith("COM", StringComparison.Ordinal) || baseName.StartsWith("LPT", StringComparison.Ordinal))
				&& baseName[3] is '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9' or '²' or '³' or '¹');
		if (reserved)
		{
			error = "Windows에서 예약한 장치 이름은 사용할 수 없어요.";
			return false;
		}
		return true;
	}

	/// <summary>oldPath(또는 그 안쪽) 경로를 newPath 기준으로 바꿈. 상관없는 경로는 그대로.</summary>
	public static string RemapPath(string path, string oldPath, string newPath)
	{
		oldPath = oldPath.TrimEnd(new char[2] { '\\', '/' });
		newPath = newPath.TrimEnd(new char[2] { '\\', '/' });
		if (string.Equals(path, oldPath, StringComparison.OrdinalIgnoreCase))
		{
			return newPath;
		}
		bool inside = path.Length > oldPath.Length && path.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase)
			&& path[oldPath.Length] is '/' or '\\';
		if (inside)
		{
			return newPath + path.Substring(oldPath.Length);
		}
		return path;
	}

	/// <summary>
	/// 판·가방 목록 안의 경로들을 이름 바꾼 뒤 경로로 고침. 고친 결과와 같은 경로가 이미 따로 들어 있으면 그건 빼서
	/// 같은 아이콘이 두 번 나오지 않게 함. 바뀐 게 있으면 true.
	/// </summary>
	public static bool RemapConfigPaths(AppConfig config, string oldPath, string newPath)
	{
		IEnumerable<List<string>> lists = config.ItemLists();
		HashSet<string> remappedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (List<string> keys in lists)
		{
			foreach (string key in keys)
			{
				string remapped = RemapPath(key, oldPath, newPath);
				if (remapped != key)
				{
					remappedTargets.Add(remapped);
				}
			}
		}
		bool changed = false;
		foreach (List<string> keys in lists)
		{
			for (int i = keys.Count - 1; i >= 0; i--)
			{
				string remapped = RemapPath(keys[i], oldPath, newPath);
				if (remapped != keys[i])
				{
					keys[i] = remapped;
					changed = true;
				}
				else if (remappedTargets.Contains(keys[i]))
				{
					keys.RemoveAt(i);
					changed = true;
				}
			}
		}
		return changed;
	}
}
