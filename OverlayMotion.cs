using System;
using System.Windows;

namespace DesktopPop;

/// <summary>날아가는 연출·끌림 그림의 위치 계산.</summary>
internal static class OverlayMotion
{
	public static Point FlightControl(Point from, Point to, double desktopTop)
	{
		// 날아가는 곡선이 위로 볼록하게: 두 점 가운데에서 거리에 비례해 위로 (화면 위로는 안 나가게)
		double lift = Math.Max(60.0, Math.Abs(to.X - from.X) * 0.25);
		return new Point((from.X + to.X) / 2.0, Math.Max(desktopTop + 20.0, Math.Min(from.Y, to.Y) - lift));
	}

	public static Rect FlightBounds(Point from, Point control, Point to, double width, double height)
	{
		// 곡선 전체 + (가장 커질 때 1.35배인) 아이콘 + 여유 40 이 들어가는 사각형
		double padX = width * 1.35 / 2.0 + 40.0;
		double padY = height * 1.35 / 2.0 + 40.0;
		double left = Math.Floor(Math.Min(from.X, Math.Min(control.X, to.X)) - padX);
		double top = Math.Floor(Math.Min(from.Y, Math.Min(control.Y, to.Y)) - padY);
		double right = Math.Ceiling(Math.Max(from.X, Math.Max(control.X, to.X)) + padX);
		double bottom = Math.Ceiling(Math.Max(from.Y, Math.Max(control.Y, to.Y)) + padY);
		return new Rect(left, top, right - left, bottom - top);
	}

	public static (int x, int y) GhostPosition(int cursorX, int cursorY, double widthDip, double scale)
	{
		return (x: (int)Math.Round((double)cursorX - widthDip * scale / 2.0), y: (int)Math.Round((double)cursorY - 34.0 * scale));
	}
}
