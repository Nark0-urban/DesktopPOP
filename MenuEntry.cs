using System;
using System.Collections.Generic;

namespace DesktopPop;

/// <summary>우클릭 메뉴에 붙이는 DesktopPop 전용 항목 (글자, 할 일, 하위 메뉴).</summary>
internal sealed class MenuEntry
{
	public string Text = "";

	public Action? Run;

	public List<MenuEntry>? Children;

	public bool IsSeparator;

	public static MenuEntry Separator => new MenuEntry
	{
		IsSeparator = true
	};

	public static MenuEntry Item(string text, Action run)
	{
		return new MenuEntry
		{
			Text = text,
			Run = run
		};
	}

	public static MenuEntry Sub(string text, List<MenuEntry> children)
	{
		return new MenuEntry
		{
			Text = text,
			Children = children
		};
	}
}
