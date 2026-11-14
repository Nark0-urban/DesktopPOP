using System;
using System.Windows;

namespace DesktopPop;

internal static class OverlayMotion
{
	public static Point FlightControl(Point from, Point to, double desktopTop)
	{
		double num = Math.Max(60.0, Math.Abs(to.X - from.X) * 0.25);
		return new Point((from.X + to.X) / 2.0, Math.Max(desktopTop + 20.0, Math.Min(from.Y, to.Y) - num));
	}

	public static Rect FlightBounds(Point from, Point control, Point to, double width, double height)
	{
		double num = width * 1.35 / 2.0 + 40.0;
		double num2 = height * 1.35 / 2.0 + 40.0;
		double num3 = Math.Floor(Math.Min(from.X, Math.Min(control.X, to.X)) - num);
		double num4 = Math.Floor(Math.Min(from.Y, Math.Min(control.Y, to.Y)) - num2);
		double num5 = Math.Ceiling(Math.Max(from.X, Math.Max(control.X, to.X)) + num);
		double num6 = Math.Ceiling(Math.Max(from.Y, Math.Max(control.Y, to.Y)) + num2);
		return new Rect(num3, num4, num5 - num3, num6 - num4);
	}

	public static (int x, int y) GhostPosition(int cursorX, int cursorY, double widthDip, double scale)
	{
		return (x: (int)Math.Round((double)cursorX - widthDip * scale / 2.0), y: (int)Math.Round((double)cursorY - 34.0 * scale));
	}
}
