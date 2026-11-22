using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DesktopPop;

/// <summary>기본 가방 그림 중 "스팀펑크" 6종 (코드로 그린 벡터 그림).</summary>
internal static class SteampunkIcons
{
	private const string Ink = "#35261D";

	private const string Gold = "#F3D393";

	private const string Copper = "#CD8761";

	private const string Draft = "#89633E";

	private static Color C(string value)
	{
		return (Color)ColorConverter.ConvertFromString(value);
	}

	private static Brush S(string value)
	{
		return new SolidColorBrush(C(value));
	}

	private static Brush G(string first, string last)
	{
		return new LinearGradientBrush(C(first), C(last), new Point(0.0, 0.0), new Point(1.0, 1.0));
	}

	private static Pen P(string value = "#35261D", double width = 1.3)
	{
		return new Pen(S(value), width)
		{
			LineJoin = PenLineJoin.Round,
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
	}

	private static Brush Metal(bool copper = false)
	{
		string[] stops = ((!copper) ? new string[5] { "#FAE3AE", "#CBA768", "#88602F", "#E6C27B", "#977044" } : new string[5] { "#F2C3A0", "#BC7951", "#7E442B", "#D8966C", "#79452F" });
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.0),
			EndPoint = new Point(1.0, 1.0)
		};
		for (int i = 0; i < stops.Length; i++)
		{
			linearGradientBrush.GradientStops.Add(new GradientStop(C(stops[i]), (double)i / 4.0));
		}
		return linearGradientBrush;
	}

	private static GeometryDrawing D(string path, Brush? fill = null, Pen? pen = null)
	{
		return new GeometryDrawing(fill, pen, Geometry.Parse(path));
	}

	private static GeometryDrawing E(double x, double y, double radius, Brush? fill, Pen? pen = null)
	{
		return new GeometryDrawing(fill, pen, new EllipseGeometry(new Point(x, y), radius, radius));
	}

	private static GeometryDrawing R(double x, double y, double width, double height, double radius, Brush? fill, Pen? pen = null)
	{
		return new GeometryDrawing(fill, pen, new RectangleGeometry(new Rect(x, y, width, height), radius, radius));
	}

	private static DrawingGroup Icon(params Drawing[] drawings)
	{
		DrawingGroup drawingGroup = new DrawingGroup();
		drawingGroup.Children.Add(R(0.0, 0.0, 100.0, 100.0, 0.0, Brushes.Transparent));
		foreach (Drawing value in drawings)
		{
			drawingGroup.Children.Add(value);
		}
		return drawingGroup;
	}

	private static Drawing Rivet(double x, double y)
	{
		return Icon(E(x, y, 1.5, Metal(), P("#35261D", 0.6)), D($"M{x - 0.6},{y} L{x + 0.6},{y}", null, P("#74543B", 0.6)));
	}

	private static Drawing Gear(double x, double y, double radius)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			for (int i = 0; i < 48; i++)
			{
				// 톱니: 48 개 점 중 4 개마다 2 개는 안쪽(골), 2 개는 바깥(이)
				double angle = Math.PI * 2.0 * (double)i / 48.0;
				int phase = i % 4;
				bool valley = phase == 0 || phase == 3;
				double r = (valley ? (radius * 0.82) : radius);
				Point point = new Point(x + Math.Cos(angle) * r, y + Math.Sin(angle) * r);
				if (i == 0)
				{
					streamGeometryContext.BeginFigure(point, isFilled: true, isClosed: true);
				}
				else
				{
					streamGeometryContext.LineTo(point, isStroked: true, isSmoothJoin: false);
				}
			}
		}
		return Icon(new GeometryDrawing(Metal(copper: true), P("#35261D", 1.0), streamGeometry), E(x, y, radius * 0.55, G("#534234", "#241D19"), P("#F3D393", 0.7)), E(x, y, radius * 0.2, Metal(), P("#35261D", 0.7)));
	}

	private static Drawing Ticks(double x, double y, double radius, int count, double begin, double end)
	{
		DrawingGroup drawingGroup = new DrawingGroup();
		for (int i = 0; i <= count; i++)
		{
			double angle = (begin + (end - begin) * (double)i / (double)count) * Math.PI / 180.0;
			double inner = radius - ((i % 3 == 0) ? 3.0 : 1.7); // 3 개마다 긴 눈금
			drawingGroup.Children.Add(new GeometryDrawing(null, P("#89633E", 0.6), new LineGeometry(new Point(x + inner * Math.Cos(angle), y + inner * Math.Sin(angle)), new Point(x + radius * Math.Cos(angle), y + radius * Math.Sin(angle)))));
		}
		return drawingGroup;
	}

	public static IEnumerable<(string name, Drawing drawing)> All()
	{
		yield return (name: "07 스팀펑크 · 작업대", drawing: Icon(D("M23,62 L31,64 L26,86 L18,86 Z M69,58 L77,56 L82,85 L74,85 Z", Metal(copper: true), P()), D("M29,77 L75,76 L76,81 L27,82 Z", G("#7C573B", "#422C22"), P()), D("M12,43 L68,26 L90,41 L34,61 Z", G("#BC8C59", "#69452C"), P("#35261D", 1.8)), D("M12,43 L34,61 L34,67 L12,49 Z M34,61 L90,41 L90,47 L34,67 Z", Metal(copper: true), P()), D("M20,41 L65,28 L80,39 L35,54 Z", G("#F4E2B7", "#C7AC79"), P("#86603B", 0.8)), D("M30,42 L61,33 M34,45 L65,36 M38,48 L69,39 M39,36 L51,47 M49,33 L61,44 M59,30 L71,41", null, P("#796B5842", 0.55)), D("M36,44 L43,34 L55,39 L49,49 Z M43,34 L43,44 L49,49 M43,44 L55,39", null, P("#8B6642", 0.8)), D("M58,17 L48,44 L52,45 L62,23 Z", Metal(), P()), D("M61,21 L68,41 L72,40 L64,17 Z", Metal(), P()), D("M51,44 L48,49 L48,43 M70,40 L72,45", null, P("#35261D", 1.0)), E(61.0, 18.0, 4.1, Metal(copper: true), P()), E(61.0, 18.0, 1.3, S("#35261D")), D("M56,29 Q62,33 67,28", null, P("#F3D393")), D("M18,53 L31,64 M21,54 L21,58 M25,57 L25,61 M29,60 L29,64", null, P("#F3D393", 0.75)), Rivet(16.0, 47.0), Rivet(36.0, 63.0), Rivet(85.0, 46.0), D("M79,23 L86,28 L89,23 L82,18 Z", Metal(copper: true), P()), D("M81,21 L87,25", null, P("#F3D393", 0.7))));
		yield return (name: "08 스팀펑크 · 건축", drawing: Icon(E(50.0, 50.0, 37.0, null, P("#6A9B7B51", 0.75)), D("M12,75 L88,75 M50,10 L50,87 M17,68 L50,16 L83,68", null, new Pen(S("#687F6848"), 0.7)
		{
			DashStyle = DashStyles.Dash
		}), D("M15,79 L50,89 L87,77 L53,67 Z", Metal(copper: true), P("#35261D", 1.4)), D("M15,79 L50,89 L50,94 L15,84 Z M50,89 L87,77 L87,82 L50,94 Z", G("#866449", "#48362A"), P()), D("M26,72 L26,36 L32,29 L70,29 L76,36 L76,73 L64,77 L64,50 Q51,32 38,50 L38,76 Z", Metal(), P("#35261D", 1.7)), D("M33,72 L33,48 Q51,21 69,48 L69,72", null, P("#F8DFA3", 1.1)), D("M38,76 L38,50 Q51,32 64,50 L64,77 L59,75 L59,52 Q51,40 43,52 L43,77 Z", Metal(copper: true), P()), D("M24,31 L51,13 L78,31 Z", Metal(copper: true), P("#35261D", 1.5)), D("M31,28 L51,17 L71,28 Z", G("#52483B", "#302A23"), P("#F3D393", 0.75)), D("M37,27 L42,22 L47,27 L52,18 L57,27 L62,23 L67,27", null, P("#F3D393", 1.0)), D("M27,42 L38,44 M26,54 L38,54 M26,65 L38,65 M64,44 L75,42 M64,55 L76,55 M64,66 L76,66 M44,34 L46,43 M56,34 L55,43", null, P("#946C3B", 0.75)), R(23.0, 73.0, 19.0, 6.0, 1.0, Metal(), P()), R(61.0, 73.0, 19.0, 6.0, 1.0, Metal(), P()), Rivet(29.0, 35.0), Rivet(73.0, 35.0), Rivet(31.0, 76.0), Rivet(71.0, 76.0), Ticks(50.0, 50.0, 37.0, 18, 184.0, 356.0)));
		yield return (name: "09 스팀펑크 · 설계", drawing: Icon(D("M19,67 A35,35 0 0 1 79,25", null, P("#98775445", 1.1)), Ticks(49.0, 50.0, 35.0, 23, 150.0, 315.0), D("M30,33 L52,20 L74,34 L69,61 L47,74 L25,60 Z", G("#DAA76D", "#765239"), P("#35261D", 1.5)), D("M30,33 L51,40 L52,20 Z", G("#F5DFAD", "#B99862"), P("#F3D393", 0.8)), D("M51,40 L74,34 L52,20 Z", G("#BD8157", "#754A32"), P("#F3D393", 0.8)), D("M30,33 L25,60 L44,58 L51,40 Z", G("#C69C68", "#61432F"), P("#F3D393", 0.8)), D("M51,40 L44,58 L69,61 L74,34 Z", G("#BD8E5C", "#48392B"), P("#F3D393", 0.8)), D("M25,60 L47,74 L44,58 Z M44,58 L47,74 L69,61 Z", G("#8C5D3D", "#34271F"), P("#F3D393", 0.8)), D("M52,20 L44,58 M30,33 L69,61 M74,34 L25,60", null, new Pen(S("#A9F5D99D"), 0.65)
		{
			DashStyle = DashStyles.Dash
		}), D("M14,75 L79,75 L79,81 L14,81 Z", Metal(), P()), D("M15,48 L21,48 L21,75 L31,75 L31,82 L15,82 Z M67,64 L73,64 L73,82 L59,82 L59,75 L67,75 Z", Metal(copper: true), P()), D("M26,76 L26,79 M31,76 L31,78 M36,76 L36,79 M41,76 L41,78 M46,76 L46,79 M51,76 L51,78 M56,76 L56,79", null, P("#35261D", 0.6)), R(64.0, 71.0, 12.0, 13.0, 1.5, Metal(), P()), Rivet(70.0, 77.0), E(30.0, 33.0, 1.5, S("#FFF0CA")), E(52.0, 20.0, 1.5, S("#FFF0CA")), E(69.0, 61.0, 1.5, S("#FFF0CA"))));
		yield return (name: "10 스팀펑크 · 프로그램", drawing: Icon(D("M24,26 L30,19 L78,19 L84,26 L84,68 L76,75 L24,75 Z", Metal(copper: true), P("#35261D", 1.5)), R(20.0, 25.0, 57.0, 48.0, 4.0, Metal(), P("#35261D", 1.6)), R(26.0, 31.0, 45.0, 34.0, 2.0, G("#3F4A3B", "#19231E"), P("#795E3D", 1.1)), R(32.0, 37.0, 13.0, 11.0, 1.3, G("#E9C27D", "#9E6E38"), P("#D5BD8B", 0.7)), R(51.0, 37.0, 13.0, 11.0, 1.3, G("#D8996A", "#8A5637"), P("#E7B58A", 0.7)), R(32.0, 53.0, 13.0, 6.0, 1.0, G("#B5C18E", "#667450"), P("#CAD3A8", 0.6)), D("M51,54 L64,54 M51,58 L59,58", null, P("#DBCA96", 1.0)), D("M35,76 L64,76 L68,82 L30,82 Z", Metal(copper: true), P()), D("M17,82 L76,82 L84,91 L10,91 Z", Metal(), P("#35261D", 1.4)), D("M23,85 L72,85 M18,88 L77,88 M26,83 L24,90 M35,83 L34,90 M44,83 L44,90 M53,83 L54,90 M62,83 L64,90", null, P("#7F5D39", 0.9)), D("M79,34 L90,34 L90,56 L81,56", null, P("#35261D", 4.0)), D("M79,34 L90,34 L90,56 L81,56", null, P("#CD8761", 2.2)), Gear(17.0, 63.0, 10.0), Rivet(24.0, 29.0), Rivet(73.0, 29.0), Rivet(73.0, 69.0), E(57.0, 69.0, 1.5, S("#FFC96B")), E(63.0, 69.0, 1.5, S("#B1BA7B"))));
		yield return (name: "11 스팀펑크 · 바로가기", drawing: Icon(E(49.0, 52.0, 30.0, null, P("#BB886443", 2.4)), E(49.0, 52.0, 25.0, null, P("#687A5B3C", 0.65)), D("M17,63 C31,78 72,63 82,34 C59,26 26,41 17,63 Z", null, P("#BFD09E62", 1.2)), D("M28,25 C12,51 44,85 70,79 C78,53 56,24 28,25 Z", null, P("#997B684D", 0.8)), Ticks(49.0, 52.0, 30.0, 21, 20.0, 345.0), D("M28,66 L58,36 L45,35 L46,25 L80,24 L79,58 L69,59 L69,46 L39,77 Z", Metal(), P("#35261D", 2.0)), D("M35,66 L66,35 L52,34 M66,35 L72,35 L72,51", null, P("#FFE8B0", 1.4)), D("M29,66 L39,77 L35,80 L25,70 Z", Metal(copper: true), P()), Rivet(74.0, 30.0), Rivet(34.0, 70.0), E(20.0, 79.0, 6.0, null, P("#35261D", 4.0)), E(20.0, 79.0, 6.0, null, P("#CD8761", 2.5)), E(29.0, 83.0, 5.0, null, P("#35261D", 4.0)), E(29.0, 83.0, 5.0, null, P("#F3D393", 2.2)), E(21.0, 40.0, 2.1, Metal(copper: true), P()), E(75.0, 67.0, 2.1, Metal(), P())));
		yield return (name: "12 스팀펑크 · 금고", drawing: Icon(D("M23,26 L31,18 L76,18 L84,26 L84,79 L77,86 L23,86 Z", Metal(copper: true), P("#35261D", 1.7)), D("M20,28 L51,10 L81,28 Z", Metal(), P("#35261D", 1.5)), D("M29,26 L51,15 L72,26 Z", G("#5C4935", "#30271F"), P("#A78650", 0.8)), D("M35,25 L51,17 L65,25 M51,18 L51,25", null, P("#F3D393", 0.85)), R(21.0, 28.0, 58.0, 57.0, 3.0, G("#806047", "#392D23"), P("#F3D393", 1.4)), D("M30,78 L30,49 Q50,20 70,49 L70,78 Z", Metal(), P("#35261D", 1.4)), D("M36,74 L36,50 Q50,31 64,50 L64,74 Z", G("#3B4139", "#1D2521"), P("#917147", 1.1)), D("M33,47 L40,51 M41,38 L44,47 M53,35 L52,45 M63,39 L59,48 M68,47 L62,51", null, P("#846039", 0.9)), E(50.0, 59.0, 10.0, Metal(copper: true), P("#35261D", 1.2)), E(50.0, 59.0, 7.0, G("#334038", "#1E2722"), P("#F3D393", 0.8)), D("M50,52 L50,66 M43,59 L57,59 M45,54 L55,64 M45,64 L55,54", null, P("#F3D393", 1.5)), E(50.0, 59.0, 2.7, Metal(), P("#35261D", 0.7)), R(24.0, 44.0, 7.0, 11.0, 1.0, Metal(copper: true), P()), R(24.0, 66.0, 7.0, 11.0, 1.0, Metal(copper: true), P()), R(21.0, 85.0, 14.0, 5.0, 1.0, Metal(), P()), R(65.0, 85.0, 14.0, 5.0, 1.0, Metal(), P()), Rivet(25.0, 32.0), Rivet(75.0, 32.0), Rivet(25.0, 81.0), Rivet(75.0, 81.0), D("M42,79 L58,79", null, P("#F3D393", 0.8))));
	}
}
