using System.Collections.Generic;

namespace DesktopPop;

internal sealed class PanelConfig
{
	public string MonitorId { get; set; } = "";

	public Side Side { get; set; }

	public List<string> Items { get; set; } = new List<string>();
}
