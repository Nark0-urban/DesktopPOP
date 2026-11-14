using System;
using System.Collections.Generic;

namespace DesktopPop;

internal sealed class BagConfig
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = "가방";

	public string ImagePath { get; set; } = "";

	public double X { get; set; }

	public double Y { get; set; }

	public double Size { get; set; } = 88.0;

	public List<string> Items { get; set; } = new List<string>();
}
