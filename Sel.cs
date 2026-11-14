using System;
using System.Collections.Generic;
using System.Linq;

namespace DesktopPop;

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
		string[] array = ks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (array.Length == 0)
		{
			host = null;
		}
		if (Host != host || !keys.SequenceEqual(array, StringComparer.OrdinalIgnoreCase))
		{
			ISelHost host2 = Host;
			Host = host;
			keys.Clear();
			keys.AddRange(array);
			membership.Clear();
			membership.UnionWith(array);
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

	public static void Range(ISelHost h, string key)
	{
		List<string> list = h.OrderedKeys.ToList();
		int num = list.FindIndex((string k) => Eq(k, key));
		int num2 = ((Anchor != null) ? list.FindIndex((string k) => Eq(k, Anchor)) : (-1));
		if (num2 < 0)
		{
			num2 = num;
			Anchor = key;
		}
		if (num >= 0)
		{
			Change(h, list.Skip(Math.Min(num2, num)).Take(Math.Abs(num2 - num) + 1));
		}
	}

	public static List<string> Ordered(ISelHost h)
	{
		return h.OrderedKeys.Where((string k) => Contains(h, k)).ToList();
	}
}
