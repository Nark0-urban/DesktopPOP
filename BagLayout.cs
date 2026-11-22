using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace DesktopPop;

/// <summary>
/// 가방을 클릭했을 때 아이콘들이 가방 둘레로 펼쳐지는 자리 계산.
/// 기본은 가방을 빙 둘러싼 원(여러 겹), 화면 가장자리에 걸리면 화면 안쪽으로 부채꼴로 펼치고,
/// 그래도 안 들어가면 부채꼴을 조금씩 돌려 보며 가장 덜 움직이는 자리를 찾음. 아이콘끼리, 가방·가방 이름과 겹치지 않게 간격을 넓힘.
/// 돌려주는 점들은 가방 중심 기준 상대 위치.
/// </summary>
internal static class BagLayout
{
	/// <summary>가방 그림 아래 이름 글자가 차지하는 기본 높이 (글자 크기를 키우면 BagWindow 가 더 큰 값을 넘김).</summary>
	private const double LabelHeight = 24.0;

	/// <summary>가방(그림 + 이름 둘레 여유) 자체가 화면 밖으로 나가지 않게 중심을 화면 안으로 끌어 옴.</summary>
	public static Point ClampCenter(Point center, double bagSize, Rect workArea, double labelWidth = 120.0, double labelHeight = LabelHeight)
	{
		double box = bagSize + 24.0;
		double halfWidth = Math.Max(box, labelWidth) / 2.0; // 이름 글자 폭만큼은 비워 둠
		double halfHeight = box / 2.0;
		return new Point(Fit(center.X, workArea.Left + halfWidth, workArea.Right - halfWidth), Fit(center.Y, workArea.Top + halfHeight, workArea.Bottom - halfHeight - labelHeight));
		static double Fit(double value, double minimum, double maximum)
		{
			if (!(minimum <= maximum))
			{
				return (minimum + maximum) / 2.0;
			}
			return Math.Clamp(value, minimum, maximum);
		}
	}

	public static List<Point> ComputeRing(int count, double bagSize, Point center, Rect workArea, double tileWidth, double tileHeight, double labelWidth = 120.0, double labelHeight = LabelHeight)
	{
		if (count <= 0)
		{
			return new List<Point>();
		}
		// 1) 가방을 빙 둘러싼 원 (12시 방향부터)
		List<Point> ring = Spread(Rings(count, bagSize, tileWidth, tileHeight, labelWidth, labelHeight, Math.PI * 2.0, -Math.PI / 2.0), bagSize, tileWidth, tileHeight, labelWidth, labelHeight);
		double halfWidth = Math.Max(1.0, tileWidth - 6.0) / 2.0;
		double halfHeight = Math.Max(1.0, tileHeight - 6.0) / 2.0;
		bool outHorizontally = ring.Any((Point p) => center.X + p.X - halfWidth < workArea.Left || center.X + p.X + halfWidth > workArea.Right);
		bool outVertically = ring.Any((Point p) => center.Y + p.Y - halfHeight < workArea.Top || center.Y + p.Y + halfHeight > workArea.Bottom);
		if (!outHorizontally && !outVertically)
		{
			return ring;
		}
		// 2) 화면 안쪽을 향한 부채꼴 (모서리면 90도, 한쪽 가장자리면 180도)
		double towardX = (outHorizontally ? ((center.X < workArea.Left + workArea.Width / 2.0) ? 1 : (-1)) : 0);
		double towardY = (outVertically ? ((center.Y < workArea.Top + workArea.Height / 2.0) ? 1 : (-1)) : 0);
		List<Point> fan = Rings(count, bagSize, tileWidth, tileHeight, labelWidth, labelHeight, (outHorizontally & outVertically) ? (Math.PI / 2.0) : Math.PI, Math.Atan2(towardY, towardX));
		List<Point> spreadFan = Spread(fan, bagSize, tileWidth, tileHeight, labelWidth, labelHeight);
		Rect viewport = new Rect(workArea.X - center.X, workArea.Y - center.Y, workArea.Width, workArea.Height); // 가방 중심 기준 화면
		if (Fits(spreadFan, viewport, halfWidth, halfHeight))
		{
			return spreadFan;
		}
		// 3) 부채꼴을 돌리거나 옮겨서 맞추기
		return FitRegularFan(fan, spreadFan, bagSize, tileWidth, tileHeight, labelWidth, labelHeight, viewport, halfWidth, halfHeight);
	}

	private static bool Fits(List<Point> points, Rect viewport, double halfWidth, double halfHeight)
	{
		return points.All((Point p) => viewport.Contains(new Rect(p.X - halfWidth, p.Y - halfHeight, 2.0 * halfWidth, 2.0 * halfHeight)));
	}

	/// <summary>
	/// 부채꼴을 통째로 옮겨서 맞춰 보고, 안 되면 5도씩(±90도까지) 돌려 가며 옮겨 봄.
	/// 덜 옮기고 덜 돌린 것을 고름. 그래도 안 되면 원래 부채꼴.
	/// </summary>
	private static List<Point> FitRegularFan(List<Point> regular, List<Point> original, double bagSize, double tileWidth, double tileHeight, double labelWidth, double labelHeight, Rect viewport, double halfWidth, double halfHeight)
	{
		if (TryTranslation(original, bagSize, tileWidth, tileHeight, labelWidth, labelHeight, viewport, halfWidth, halfHeight, out var direct))
		{
			return original.Select((Point p) => p + direct).ToList();
		}
		List<Point> best = null;
		double bestCost = double.PositiveInfinity;
		double radius = original.Max((Point p) => ((Vector)p).Length);
		for (int step = 1; step <= 18; step++)
		{
			int[] signs = new int[2] { -1, 1 };
			foreach (int sign in signs)
			{
				double angle = (double)(sign * step) * Math.PI / 36.0;
				double cos = Math.Cos(angle);
				double sin = Math.Sin(angle);
				List<Point> rotated = Spread(regular.Select((Point p) => new Point(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos)).ToList(), bagSize, tileWidth, tileHeight, labelWidth, labelHeight);
				if (!TryTranslation(rotated, bagSize, tileWidth, tileHeight, labelWidth, labelHeight, viewport, halfWidth, halfHeight, out var shift))
				{
					continue;
				}
				// 옮긴 거리² + 돌린 정도(호의 길이²)에 가중치
				double cost = shift.LengthSquared + angle * angle * radius * radius * 0.2;
				if (!(cost >= bestCost))
				{
					bestCost = cost;
					best = rotated.Select((Point p) => p + shift).ToList();
				}
			}
		}
		return best ?? original;
	}

	/// <summary>
	/// 점들을 통째로 옮겨 화면(viewport) 안에 넣되, 가방·이름과 겹치지 않는 가장 짧은 이동을 찾음.
	/// </summary>
	private static bool TryTranslation(List<Point> points, double bagSize, double tileWidth, double tileHeight, double labelWidth, double labelHeight, Rect viewport, double halfWidth, double halfHeight, out Vector shift)
	{
		shift = default;
		// 화면 안에 들어가게 하는 이동 범위
		double minX = viewport.Left - points.Min((Point p) => p.X) + halfWidth + 1E-06;
		double maxX = viewport.Right - points.Max((Point p) => p.X) - halfWidth - 1E-06;
		double minY = viewport.Top - points.Min((Point p) => p.Y) + halfHeight + 1E-06;
		double maxY = viewport.Bottom - points.Max((Point p) => p.Y) - halfHeight - 1E-06;
		if (minX > maxX || minY > maxY)
		{
			return false;
		}
		// 가방 그림 + 이름 둘레의 비워 둘 영역
		double box = bagSize + 24.0;
		double clearX = Math.Max(box, labelWidth) / 2.0 + tileWidth / 2.0;
		double clearAbove = box / 2.0 + tileHeight / 2.0;
		double clearBelow = clearAbove + labelHeight;
		Vector nearest = new Vector(Math.Clamp(0.0, minX, maxX), Math.Clamp(0.0, minY, maxY));
		if (Clear(nearest))
		{
			shift = nearest;
			return true;
		}
		// 후보 이동량: 경계값들과 각 아이콘이 가방 영역을 막 벗어나는 값들의 조합 중 가장 짧은 것
		List<double> candidatesX = new List<double> { nearest.X, minX, maxX };
		List<double> candidatesY = new List<double> { nearest.Y, minY, maxY };
		foreach (Point point in points)
		{
			candidatesX.Add(Math.Clamp(0.0 - clearX - point.X, minX, maxX));
			candidatesX.Add(Math.Clamp(clearX - point.X, minX, maxX));
			candidatesY.Add(Math.Clamp(0.0 - clearAbove - point.Y, minY, maxY));
			candidatesY.Add(Math.Clamp(clearBelow - point.Y, minY, maxY));
		}
		double bestLengthSq = double.PositiveInfinity;
		foreach (double dx in candidatesX.Distinct().OrderBy(Math.Abs))
		{
			foreach (double dy in candidatesY.Distinct().OrderBy(Math.Abs))
			{
				Vector candidate = new Vector(dx, dy);
				if (!(candidate.LengthSquared >= bestLengthSq) && Clear(candidate))
				{
					shift = candidate;
					bestLengthSq = candidate.LengthSquared;
				}
			}
		}
		return double.IsFinite(bestLengthSq);
		bool Clear(Vector candidate)
		{
			return points.All((Point p) => Math.Abs(p.X + candidate.X) >= clearX - 1E-09 || p.Y + candidate.Y <= 0.0 - clearAbove + 1E-09 || p.Y + candidate.Y >= clearBelow - 1E-09);
		}
	}

	/// <summary>
	/// 모양은 그대로 두고 전체를 같은 비율로 넓혀서, 아이콘끼리 그리고 가방·이름과 겹치지 않게 함.
	/// </summary>
	private static List<Point> Spread(List<Point> points, double bagSize, double tileWidth, double tileHeight, double labelWidth, double labelHeight)
	{
		double scale = 1.0;
		double box = bagSize + 24.0;
		double clearance = Math.Max(box, labelWidth) / 2.0 + tileWidth / 2.0;
		double clearAbove = box / 2.0 + tileHeight / 2.0;
		double clearBelow = clearAbove + labelHeight;
		for (int i = 0; i < points.Count; i++)
		{
			Point point = points[i];
			scale = Math.Max(scale, Math.Min(Ratio(clearance, point.X), Ratio((point.Y < 0.0) ? clearAbove : clearBelow, point.Y)));
			for (int j = 0; j < i; j++)
			{
				scale = Math.Max(scale, Math.Min(Ratio(tileWidth, point.X - points[j].X), Ratio(tileHeight, point.Y - points[j].Y)));
			}
		}
		return points.Select((Point p) => new Point(p.X * scale, p.Y * scale)).ToList();
		// 거리 delta 를 needed 이상으로 벌리려면 몇 배로 넓혀야 하나
		static double Ratio(double needed, double delta)
		{
			if (!(Math.Abs(delta) < 1E-09))
			{
				return needed / Math.Abs(delta);
			}
			return double.PositiveInfinity;
		}
	}

	/// <summary>
	/// 가방 둘레에 겹겹이 놓을 자리. span = 펼칠 각도(2π 면 한 바퀴), direction = 부채꼴 가운데 방향.
	/// 안쪽 겹부터 들어갈 만큼 채우고, 남으면 바깥 겹으로.
	/// </summary>
	private static List<Point> Rings(int count, double bagSize, double tileWidth, double tileHeight, double labelWidth, double labelHeight, double span, double direction)
	{
		List<Point> points = new List<Point>();
		bool fullCircle = span >= 6.283184307179586;
		double firstRadius = bagSize / 2.0 + 12.0 + tileWidth * 0.62;
		int placed = 0;
		int ring = 0;
		while (placed < count)
		{
			double radius = firstRadius + (double)ring * tileHeight * 0.98;
			int onThisRing = Math.Min(Math.Max(1, (int)Math.Floor(radius * span / (tileWidth * 1.04))), count - placed);
			for (int i = 0; i < onThisRing; i++)
			{
				// 한 바퀴일 때는 홀수 겹을 반 칸 엇갈리게
				double angle = (fullCircle ? (direction + Math.PI * 2.0 * (double)i / (double)onThisRing + ((ring % 2 == 1) ? (Math.PI / (double)onThisRing) : 0.0)) : (direction - span / 2.0 + span * ((double)i + 0.5) / (double)onThisRing));
				points.Add(new Point(radius * Math.Cos(angle), radius * Math.Sin(angle)));
			}
			placed += onThisRing;
			ring++;
		}
		return points;
	}
}
