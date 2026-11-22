using System.Collections.Generic;
using System.Windows.Controls;

namespace DesktopPop;

/// <summary>아이콘을 선택할 수 있는 곳(가장자리 판, 가방)이 공통으로 가진 것.</summary>
internal interface ISelHost
{
	IReadOnlyList<string> OrderedKeys { get; }

	bool BandSelectable { get; }

	Border? TileOf(string key);

	void ApplySelection();
}
