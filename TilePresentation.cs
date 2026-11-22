using System.Collections.Generic;
using System.Windows.Media;

namespace DesktopPop;

/// <summary>아이콘 칸이 보여 주는 내용 요약 (바뀌었는지 비교해서 바뀐 것만 다시 그리려고).</summary>
internal readonly record struct TilePresentation(string Key, string Label, string ToolTip, ImageSource? Icon)
{
	public static TilePresentation Capture(DesktopItem item)
	{
		return new TilePresentation(item.Key, item.DisplayLabel, item.ToolTipText, item.Icon);
	}

	public static bool Matches(IReadOnlyList<TilePresentation> previous, IReadOnlyList<DesktopItem> current)
	{
		if (previous.Count != current.Count)
		{
			return false;
		}
		for (int i = 0; i < previous.Count; i++)
		{
			if (previous[i] != Capture(current[i]))
			{
				return false;
			}
		}
		return true;
	}
}
