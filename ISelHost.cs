using System.Collections.Generic;
using System.Windows.Controls;

namespace DesktopPop;

internal interface ISelHost
{
	IReadOnlyList<string> OrderedKeys { get; }

	bool BandSelectable { get; }

	Border? TileOf(string key);

	void ApplySelection();
}
