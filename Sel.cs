using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopPop;

/// <summary>지금 선택된 아이콘들 (한 번에 한 판·가방 안에서만).</summary>
internal static class Sel
{
	private static readonly List<string> keys = new List<string>();

	private static readonly HashSet<string> membership = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public static string? Anchor;

	public static ISelHost? Host { get; private set; }

	public static IReadOnlyList<string> Keys => keys;

	private static bool Eq(string a, string b)
	{
		return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	}

	public static bool Contains(ISelHost h, string key)
	{
		if (Host == h)
		{
			return membership.Contains(key);
		}
		return false;
	}

	private static void Change(ISelHost? host, IEnumerable<string> ks)
	{
		string[] unique = ks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (unique.Length == 0)
		{
			host = null;
		}
		if (Host != host || !keys.SequenceEqual(unique, StringComparer.OrdinalIgnoreCase))
		{
			ISelHost host2 = Host;
			Host = host;
			keys.Clear();
			keys.AddRange(unique);
			membership.Clear();
			membership.UnionWith(unique);
			if (host2 != null && host2 != Host)
			{
				host2.ApplySelection();
			}
			Host?.ApplySelection();
		}
	}

	public static void Clear(ISelHost? onlyIf = null)
	{
		if ((onlyIf == null || Host == onlyIf) && (Host != null || keys.Count != 0))
		{
			Change(null, Array.Empty<string>());
		}
	}

	public static void Set(ISelHost h, IEnumerable<string> ks)
	{
		Change(h, ks);
	}

	public static void Toggle(ISelHost h, string key)
	{
		List<string> list = ((Host == h) ? keys.ToList() : new List<string>());
		if (list.RemoveAll((string k) => Eq(k, key)) == 0)
		{
			list.Add(key);
		}
		Anchor = key;
		Change(h, list);
	}

	/// <summary>Shift+클릭: 마지막으로 누른 것(Anchor)부터 이것까지 한 번에 선택.</summary>
	public static void Range(ISelHost h, string key)
	{
		List<string> list = h.OrderedKeys.ToList();
		int clicked = list.FindIndex((string k) => Eq(k, key));
		int anchor = ((Anchor != null) ? list.FindIndex((string k) => Eq(k, Anchor)) : (-1));
		if (anchor < 0)
		{
			anchor = clicked;
			Anchor = key;
		}
		if (clicked >= 0)
		{
			Change(h, list.Skip(Math.Min(anchor, clicked)).Take(Math.Abs(anchor - clicked) + 1));
		}
	}

	public static List<string> Ordered(ISelHost h)
	{
		return h.OrderedKeys.Where((string k) => Contains(h, k)).ToList();
	}
}
