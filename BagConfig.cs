using System;
using System.Collections.Generic;

namespace DesktopPop;

/// <summary>가방 하나의 설정: 이름, 그림, 위치·크기, 넣어 둔 아이콘 경로들.</summary>
internal sealed class BagConfig
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = "가방";

	public string ImagePath { get; set; } = "";

	public double X { get; set; }

	public double Y { get; set; }

	public double Size { get; set; } = 88.0;

	/// <summary>가방 이름 글자 크기 (이름 바꾸기 창에서 고름).</summary>
	public double NameFontSize { get; set; } = 12.5;

	public List<string> Items { get; set; } = new List<string>();
}
