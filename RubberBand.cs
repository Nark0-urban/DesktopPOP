using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DesktopPop;

internal sealed class RubberBand
{
	public Action? OnClick;

	public bool IsActive => DesktopBand.IsActive;

	public RubberBand(UIElement surface, Canvas canvas, ISelHost host)
	{
		surface.MouseLeftButtonDown += (object s, MouseButtonEventArgs e) =>
		{
			if (!e.Handled && Native.GetCursorPos(out var p))
			{
				bool addToSelection = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
				DesktopBand.Begin(p, addToSelection, OnClick);
				e.Handled = true;
			}
		};
	}
}
