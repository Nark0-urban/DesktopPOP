using System.Collections.Generic;

namespace DesktopPop;

/// <summary>가장자리 판 하나의 설정 (어느 모니터 어느 쪽, 아이콘 순서).</summary>
internal sealed class PanelConfig
{
	public string MonitorId { get; set; } = "";

	public Side Side { get; set; }

	public List<string> Items { get; set; } = new List<string>();
}
