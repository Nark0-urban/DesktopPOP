using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace DesktopPop;

internal static class BagLayout
{
	public static Point ClampCenter(Point center, double bagSize, Rect workArea)
	{
		double num = bagSize + 24.0;
		double num2 = Math.Max(num, 120.0) / 2.0;
		double num3 = num / 2.0;
		return new Point(Fit(center.X, workArea.Left + num2, workArea.Right - num2), Fit(center.Y, workArea.Top + num3, workArea.Bottom - num3 - 24.0));
		static double Fit(double value, double minimum, double maximum)
		{
			if (!(minimum <= maximum))
			{
				return (minimum + maximum) / 2.0;
			}
			return Math.Clamp(value, minimum, maximum);
		}
	}

	public static List<Point> ComputeRing(int count, double bagSize, Point center, Rect workArea, double tileWidth, double tileHeight)
	{
		if (count <= 0)
		{
			return new List<Point>();
		}
		List<Point> list = Spread(Rings(count, bagSize, tileWidth, tileHeight, Math.PI * 2.0, -Math.PI / 2.0), bagSize, tileWidth, tileHeight);
		double halfWidth = Math.Max(1.0, tileWidth - 6.0) / 2.0;
		double halfHeight = Math.Max(1.0, tileHeight - 6.0) / 2.0;
		bool flag = list.Any((Point p) => center.X + p.X - halfWidth < workArea.Left || center.X + p.X + halfWidth > workArea.Right);
		bool flag2 = list.Any((Point p) => center.Y + p.Y - halfHeight < workArea.Top || center.Y + p.Y + halfHeight > workArea.Bottom);
		if (!flag && !flag2)
		{
			return list;
		}
		double x = (flag ? ((center.X < workArea.Left + workArea.Width / 2.0) ? 1 : (-1)) : 0);
		double y = (flag2 ? ((center.Y < workArea.Top + workArea.Height / 2.0) ? 1 : (-1)) : 0);
		List<Point> list2 = Rings(count, bagSize, tileWidth, tileHeight, (flag & flag2) ? (Math.PI / 2.0) : Math.PI, Math.Atan2(y, x));
		List<Point> list3 = Spread(list2, bagSize, tileWidth, tileHeight);
		Rect viewport = new Rect(workArea.X - center.X, workArea.Y - center.Y, workArea.Width, workArea.Height);
		if (Fits(list3, viewport, halfWidth, halfHeight))
		{
			return list3;
		}
		return FitRegularFan(list2, list3, bagSize, tileWidth, tileHeight, viewport, halfWidth, halfHeight);
	}

	private static bool Fits(List<Point> points, Rect viewport, double halfWidth, double halfHeight)
	{
		return points.All((Point p) => viewport.Contains(new Rect(p.X - halfWidth, p.Y - halfHeight, 2.0 * halfWidth, 2.0 * halfHeight)));
	}

	private static List<Point> FitRegularFan(List<Point> regular, List<Point> original, double bagSize, double tileWidth, double tileHeight, Rect viewport, double halfWidth, double halfHeight)
	{
		if (TryTranslation(original, bagSize, tileWidth, tileHeight, viewport, halfWidth, halfHeight, out var direct))
		{
			return original.Select((Point p) => p + direct).ToList();
		}
		List<Point> list = null;
		double num = double.PositiveInfinity;
		double num2 = original.Max((Point p) => ((Vector)p).Length);
		for (int num3 = 1; num3 <= 18; num3++)
		{
			int[] array = new int[2] { -1, 1 };
			foreach (int num5 in array)
			{
				double num6 = (double)(num5 * num3) * Math.PI / 36.0;
				double cos = Math.Cos(num6);
				double sin = Math.Sin(num6);
				List<Point> list2 = Spread(regular.Select((Point p) => new Point(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos)).ToList(), bagSize, tileWidth, tileHeight);
				if (!TryTranslation(list2, bagSize, tileWidth, tileHeight, viewport, halfWidth, halfHeight, out var shift))
				{
					continue;
				}
				double num7 = shift.LengthSquared + num6 * num6 * num2 * num2 * 0.2;
				if (!(num7 >= num))
				{
					num = num7;
					list = list2.Select((Point p) => p + shift).ToList();
				}
			}
		}
		return list ?? original;
	}

	private static bool TryTranslation(List<Point> points, double bagSize, double tileWidth, double tileHeight, Rect viewport, double halfWidth, double halfHeight, out Vector shift)
	{
		shift = default;
		double num = viewport.Left - points.Min((Point p) => p.X) + halfWidth + 1E-06;
		double num2 = viewport.Right - points.Max((Point p) => p.X) - halfWidth - 1E-06;
		double num3 = viewport.Top - points.Min((Point p) => p.Y) + halfHeight + 1E-06;
		double num4 = viewport.Bottom - points.Max((Point p) => p.Y) - halfHeight - 1E-06;
		if (num > num2 || num3 > num4)
		{
			return false;
		}
		double num5 = bagSize + 24.0;
		double clearX = Math.Max(num5, 120.0) / 2.0 + tileWidth / 2.0;
		double clearAbove = num5 / 2.0 + tileHeight / 2.0;
		double clearBelow = clearAbove + 24.0;
		Vector vector = new Vector(Math.Clamp(0.0, num, num2), Math.Clamp(0.0, num3, num4));
		if (Clear(vector))
		{
			shift = vector;
			return true;
		}
		List<double> list = new List<double> { vector.X, num, num2 };
		List<double> list2 = new List<double> { vector.Y, num3, num4 };
		foreach (Point point in points)
		{
			list.Add(Math.Clamp(0.0 - clearX - point.X, num, num2));
			list.Add(Math.Clamp(clearX - point.X, num, num2));
			list2.Add(Math.Clamp(0.0 - clearAbove - point.Y, num3, num4));
			list2.Add(Math.Clamp(clearBelow - point.Y, num3, num4));
		}
		double num6 = double.PositiveInfinity;
		foreach (double item in list.Distinct().OrderBy(Math.Abs))
		{
			foreach (double item2 in list2.Distinct().OrderBy(Math.Abs))
			{
				Vector vector2 = new Vector(item, item2);
				if (!(vector2.LengthSquared >= num6) && Clear(vector2))
				{
					shift = vector2;
					num6 = vector2.LengthSquared;
				}
			}
		}
		return double.IsFinite(num6);
		bool Clear(Vector candidate)
		{
			return points.All((Point p) => Math.Abs(p.X + candidate.X) >= clearX - 1E-09 || p.Y + candidate.Y <= 0.0 - clearAbove + 1E-09 || p.Y + candidate.Y >= clearBelow - 1E-09);
		}
	}

	private static List<Point> Spread(List<Point> points, double bagSize, double tileWidth, double tileHeight)
	{
		double scale = 1.0;
		double num = bagSize + 24.0;
		double clearance = Math.Max(num, 120.0) / 2.0 + tileWidth / 2.0;
		double num2 = num / 2.0 + tileHeight / 2.0;
		double num3 = num2 + 24.0;
		for (int i = 0; i < points.Count; i++)
		{
			Point point = points[i];
			scale = Math.Max(scale, Math.Min(Ratio(clearance, point.X), Ratio((point.Y < 0.0) ? num2 : num3, point.Y)));
			for (int j = 0; j < i; j++)
			{
				scale = Math.Max(scale, Math.Min(Ratio(tileWidth, point.X - points[j].X), Ratio(tileHeight, point.Y - points[j].Y)));
			}
		}
		return points.Select((Point point2) => new Point(point2.X * scale, point2.Y * scale)).ToList();
		static double Ratio(double num4, double delta)
		{
			if (!(Math.Abs(delta) < 1E-09))
			{
				return num4 / Math.Abs(delta);
			}
			return double.PositiveInfinity;
		}
	}

	private static List<Point> Rings(int count, double bagSize, double tileWidth, double tileHeight, double span, double direction)
	{
		List<Point> list = new List<Point>();
		bool flag = span >= 6.283184307179586;
		double num = bagSize / 2.0 + 12.0 + tileWidth * 0.62;
		int num2 = 0;
		int num3 = 0;
		while (num2 < count)
		{
			double num4 = num + (double)num3 * tileHeight * 0.98;
			int num5 = Math.Min(Math.Max(1, (int)Math.Floor(num4 * span / (tileWidth * 1.04))), count - num2);
			for (int i = 0; i < num5; i++)
			{
				double num6 = (flag ? (direction + Math.PI * 2.0 * (double)i / (double)num5 + ((num3 % 2 == 1) ? (Math.PI / (double)num5) : 0.0)) : (direction - span / 2.0 + span * ((double)i + 0.5) / (double)num5));
				list.Add(new Point(num4 * Math.Cos(num6), num4 * Math.Sin(num6)));
			}
			num2 += num5;
			num3++;
		}
		return list;
	}
}
