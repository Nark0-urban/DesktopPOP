using System;
using System.Collections.Generic;
using System.IO;

namespace DesktopPop;

internal static class FileOperations
{
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
		string text = (isDirectory ? "" : Path.GetExtension(fileName));
		string text2 = (isDirectory ? fileName : Path.GetFileNameWithoutExtension(fileName));
		string text3 = Path.Combine(destinationDirectory, fileName);
		if (!exists(text3))
		{
			return text3;
		}
		text3 = Path.Combine(destinationDirectory, text2 + " - 복사본" + text);
		int num = 2;
		while (exists(text3))
		{
			text3 = Path.Combine(destinationDirectory, $"{text2} - 복사본 ({num}){text}");
			num++;
		}
		return text3;
	}

	public static bool TryValidateName(string name, out string error)
	{
		error = "";
		bool flag = string.IsNullOrWhiteSpace(name);
		bool flag2;
		if (!flag)
		{
			flag2 = ((name == "." || name == "..") ? true : false);
			flag = flag2;
		}
		if (flag || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\') || name.EndsWith('.') || name.EndsWith(' '))
		{
			error = "이름에는 경로나 \\ / : * ? \" < > | 문자를 쓸 수 없고, 끝에 점이나 공백을 넣을 수 없어요.";
			return false;
		}
		string text = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
		switch (text)
		{
		case "CON":
		case "PRN":
		case "AUX":
		case "NUL":
		case "CONIN$":
		case "CONOUT$":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		flag2 = flag;
		if (!flag2)
		{
			bool flag3 = text.Length == 4 && (text.StartsWith("COM", StringComparison.Ordinal) || text.StartsWith("LPT", StringComparison.Ordinal));
			if (flag3)
			{
				bool flag4;
				switch (text[3])
				{
				case '1':
				case '2':
				case '3':
				case '4':
				case '5':
				case '6':
				case '7':
				case '8':
				case '9':
				case '²':
				case '³':
				case '¹':
					flag4 = true;
					break;
				default:
					flag4 = false;
					break;
				}
				flag3 = flag4;
			}
			flag2 = flag3;
		}
		if (flag2)
		{
			error = "Windows에서 예약한 장치 이름은 사용할 수 없어요.";
			return false;
		}
		return true;
	}

	public static string RemapPath(string path, string oldPath, string newPath)
	{
		oldPath = oldPath.TrimEnd(new char[2] { '\\', '/' });
		newPath = newPath.TrimEnd(new char[2] { '\\', '/' });
		if (string.Equals(path, oldPath, StringComparison.OrdinalIgnoreCase))
		{
			return newPath;
		}
		bool flag = path.Length > oldPath.Length && path.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase);
		if (flag)
		{
			char c = path[oldPath.Length];
			bool flag2 = ((c == '/' || c == '\\') ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			return newPath + path.Substring(oldPath.Length);
		}
		return path;
	}

	public static bool RemapConfigPaths(AppConfig config, string oldPath, string newPath)
	{
		IEnumerable<List<string>> enumerable = config.ItemLists();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (List<string> item in enumerable)
		{
			foreach (string item2 in item)
			{
				string text = RemapPath(item2, oldPath, newPath);
				if (text != item2)
				{
					hashSet.Add(text);
				}
			}
		}
		bool result = false;
		foreach (List<string> item3 in enumerable)
		{
			for (int num = item3.Count - 1; num >= 0; num--)
			{
				string text2 = RemapPath(item3[num], oldPath, newPath);
				if (text2 != item3[num])
				{
					item3[num] = text2;
					result = true;
				}
				else if (hashSet.Contains(item3[num]))
				{
					item3.RemoveAt(num);
					result = true;
				}
			}
		}
		return result;
	}
}
