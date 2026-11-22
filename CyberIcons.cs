using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DesktopPop;

/// <summary>기본 가방 그림 중 "사이버" 6종 (코드로 그린 벡터 그림).</summary>
internal static class CyberIcons
{
	private const string Cyan = "#55F4FF";

	private const string Violet = "#B991FF";

	private const string Ice = "#D7FBFF";

	private static Color C(string value)
	{
		return (Color)ColorConverter.ConvertFromString(value);
	}

	private static Brush S(string value)
	{
		return new SolidColorBrush(C(value));
	}

	private static Brush G(string top, string bottom)
	{
		return new LinearGradientBrush(C(top), C(bottom), new Point(0.0, 0.0), new Point(1.0, 1.0));
	}

	private static Pen P(string value, double width = 1.5)
	{
		return new Pen(S(value), width)
		{
			LineJoin = PenLineJoin.Round,
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
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

	private static DrawingGroup N(string path, string color, double width = 1.8)
	{
		Geometry geometry = Geometry.Parse(path);
		DrawingGroup drawingGroup = new DrawingGroup();
		Color color2 = C(color);
		color2.A = 25;
		Color color3 = C(color);
		color3.A = 65;
		drawingGroup.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(color2), width + 5.0), geometry));
		drawingGroup.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(color3), width + 2.0), geometry));
		drawingGroup.Children.Add(new GeometryDrawing(null, P(color, width), geometry));
		return drawingGroup;
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

	private static DrawingGroup Badge()
	{
		return Icon(D("M30,9 L70,9 L91,30 L91,70 L70,91 L30,91 L9,70 L9,30 Z", G("#F018273D", "#EE111022"), P("#50627C9C", 1.0)), D("M33,14 L67,14 L86,33 L86,67 L67,86 L33,86 L14,67 L14,33 Z", null, P("#283F6680", 0.7)), N("M12,35 L12,29 L28,13 L37,13 M63,87 L72,87 L88,71 L88,64", "#55F4FF", 1.4), N("M69,12 L73,12 L88,27 M12,72 L27,87 L33,87", "#B991FF", 1.2), D("M35,92 L40,92 M44,92 L48,92 M52,92 L65,92", null, P("#887D99CC", 0.8)));
	}

	public static IEnumerable<(string name, Drawing drawing)> All()
	{
		yield return (name: "01 사이버 · 포털 바로가기", drawing: Icon(Badge(), E(50.0, 49.0, 31.0, G("#D51E3260", "#EE0D1729"), P("#6055F4FF", 1.0)), N("M28,27 A31,31 0 0 1 75,31 M80,43 A31,31 0 0 1 66,77 M45,80 A31,31 0 0 1 20,49", "#55F4FF", 2.1), N("M22,39 A29,29 0 0 1 29,28 M69,25 A29,29 0 0 1 79,37 M61,78 A29,29 0 0 1 51,80", "#B991FF", 2.5), E(50.0, 49.0, 24.0, null, P("#90B991FF", 1.0)), E(50.0, 49.0, 19.0, G("#9E473F83", "#E015233B"), P("#4055F4FF", 1.0)), N("M35,63 L63,35 M48,35 L63,35 L63,50", "#D7FBFF", 3.2), D("M34,69 L40,69 M66,29 L69,29 M27,48 L27,53", null, P("#55F4FF")), E(19.0, 49.0, 2.2, S("#D7FBFF")), E(76.0, 31.0, 1.7, S("#B991FF"))));
		yield return (name: "02 사이버 · CAD 그리드", drawing: Icon(Badge(), D("M15,63 L48,79 L85,57 L52,41 Z", G("#C31F3152", "#D517213C"), P("#9555F4FF", 1.3)), D("M23,67 L59,45 M31,71 L67,49 M39,75 L75,53 M24,58 L57,74 M33,53 L66,69 M42,47 L76,63", null, P("#724EB6CB", 0.7)), D("M36,35 L54,25 L73,36 L55,47 Z", G("#A870DDF5", "#B38975E6"), P("#55F4FF")), D("M36,35 L55,47 L55,68 L36,57 Z", G("#BD365A93", "#CC283158"), P("#55F4FF", 1.4)), D("M55,47 L73,36 L73,56 L55,68 Z", G("#B88963CA", "#DF362858"), P("#B991FF", 1.4)), N("M36,35 L55,47 L73,36 M55,47 L55,68", "#D7FBFF", 1.4), D("M54,25 L54,46 L36,57 M54,46 L73,56", null, new Pen(S("#7569D6EE"), 0.9)
		{
			DashStyle = DashStyles.Dash
		}), N("M24,29 L24,57 M21,29 L27,29 M21,57 L27,57 M36,19 L73,19 M36,16 L36,22 M73,16 L73,22", "#55F4FF", 0.9), E(36.0, 35.0, 1.9, S("#D7FBFF")), E(73.0, 36.0, 1.9, S("#D7FBFF")), E(55.0, 68.0, 1.9, S("#B991FF")), D("M72,74 L80,69 L82,72 L74,77 Z", S("#E3C8FF"), P("#B991FF", 0.8))));
		yield return (name: "03 사이버 · Rhino 폴리곤", drawing: Icon(Badge(), D("M18,53 L25,37 L47,29 L66,34 L75,43 L83,41 L90,51 L84,64 L72,68 L66,79 L58,79 L58,67 L40,68 L35,80 L26,80 L25,66 Z", G("#A443D6EA", "#D93A397E"), P("#55F4FF", 1.3)), D("M18,53 L40,43 L25,37 Z", S("#686BF7FF")), D("M25,37 L47,29 L40,43 Z", S("#A35DDEE8")), D("M47,29 L66,34 L57,47 L40,43 Z", S("#7748ADCD")), D("M18,53 L40,43 L34,62 L25,66 Z", S("#835D8AC9")), D("M40,43 L57,47 L50,64 L34,62 Z", S("#8A606BD2")), D("M57,47 L66,34 L75,43 L72,68 L50,64 Z", S("#827F61D6")), D("M75,43 L83,41 L90,51 L77,55 Z", S("#B39BE8F4")), D("M77,55 L90,51 L84,64 L72,68 Z", S("#996D91CB")), N("M25,37 L40,43 L47,29 M40,43 L34,62 L50,64 L57,47 L66,34 M57,47 L75,43 M50,64 L58,67 M75,43 L77,55 L72,68", "#AA98E5FF", 0.7), D("M83,46 L92,28 L90,51 Z", G("#D7FBFF", "#62DFF1"), P("#D7FBFF", 0.9)), D("M72,42 L70,31 L79,39 Z", G("#B991FF", "#6163C3"), P("#B991FF", 1.0)), N("M19,51 L13,45 L12,39", "#B991FF", 1.5), E(81.0, 50.0, 1.25, S("#F9F8FF")), N("M23,85 L37,85 M58,85 L70,85", "#6955F4FF", 1.0), E(40.0, 43.0, 1.3, S("#D7FBFF")), E(57.0, 47.0, 1.3, S("#B991FF")), E(34.0, 62.0, 1.3, S("#55F4FF"))));
		yield return (name: "04 사이버 · 앱 코어", drawing: Icon(Badge(), N("M35,20 L35,29 M45,17 L45,28 M55,17 L55,28 M65,20 L65,29 M35,71 L35,81 M45,72 L45,84 M55,72 L55,84 M65,71 L65,81 M20,35 L29,35 M17,45 L28,45 M17,55 L28,55 M20,65 L29,65 M71,35 L81,35 M72,45 L84,45 M72,55 L84,55 M71,65 L81,65", "#9B8B9EF6", 1.2), R(27.0, 27.0, 46.0, 46.0, 7.0, G("#EF243F58", "#F02B1E4D"), P("#55F4FF")), R(33.0, 33.0, 34.0, 34.0, 4.0, G("#BF24526F", "#D15B3B87"), P("#95B991FF", 0.9)), D("M38,43 L50,36 L62,43 L50,50 Z", G("#9CFAFF", "#A8ACFF"), P("#D7FBFF", 1.0)), D("M38,43 L50,50 L50,64 L38,57 Z", G("#32B7C5", "#274F87"), P("#55F4FF", 1.0)), D("M50,50 L62,43 L62,57 L50,64 Z", G("#BA80FC", "#6940B5"), P("#B991FF", 1.0)), N("M38,43 L50,50 L62,43 M50,50 L50,64", "#D7FBFF", 1.0), E(31.0, 31.0, 1.2, S("#D7FBFF")), E(69.0, 69.0, 1.2, S("#B991FF")), D("M36,77 L42,77 M58,23 L64,23", null, P("#D7FBFF", 1.0))));
		yield return (name: "05 사이버 · 링크 허브", drawing: Icon(Badge(), N("M32,30 L43,42 M68,30 L57,42 M31,70 L43,58 M68,70 L57,58 M50,63 L50,80", "#55F4FF", 1.4), N("M20,42 L20,56 L31,67 M80,42 L80,56 L69,67", "#B991FF", 1.0), R(16.0, 18.0, 22.0, 22.0, 6.0, G("#D329526F", "#F01E2945"), P("#55F4FF", 1.3)), R(62.0, 18.0, 22.0, 22.0, 6.0, G("#DF574584", "#F028264D"), P("#B991FF", 1.3)), R(16.0, 61.0, 22.0, 22.0, 6.0, G("#D329526F", "#F01E2945"), P("#55F4FF", 1.3)), R(62.0, 61.0, 22.0, 22.0, 6.0, G("#DF574584", "#F028264D"), P("#B991FF", 1.3)), D("M50,35 L63,43 L63,57 L50,65 L37,57 L37,43 Z", G("#E6336D83", "#F044386E"), P("#D7FBFF")), N("M45,50 L48,47 Q51,44 54,47 Q57,50 54,53 L51,56 M49,44 L46,47 Q43,50 46,53 Q49,56 52,53 L55,50", "#D7FBFF", 1.4), D("M22,29 L27,24 L32,29 L27,34 Z", S("#6655F4FF"), P("#55F4FF", 0.9)), E(73.0, 29.0, 4.0, S("#65B991FF"), P("#B991FF", 1.0)), R(23.0, 68.0, 8.0, 8.0, 1.0, S("#6655F4FF"), P("#55F4FF", 0.9)), D("M69,76 L69,68 L77,72 Z", S("#A7D5B4FF"), P("#B991FF", 0.9)), E(50.0, 82.0, 2.0, S("#D7FBFF"))));
		yield return (name: "06 사이버 · 프로젝트 금고", drawing: Icon(Badge(), D("M24,29 L33,20 L75,20 L83,28 L83,72 L75,81 L33,81 L24,72 Z", G("#D43F4471", "#EF1A203A"), P("#B991FF", 1.2)), D("M24,29 L33,20 L33,70 L24,78 Z", G("#A2416B82", "#D829294B"), P("#7071D9E8", 1.0)), R(23.0, 28.0, 52.0, 52.0, 5.0, G("#F1254259", "#F516253B"), P("#55F4FF", 1.7)), R(28.0, 33.0, 42.0, 42.0, 3.0, null, P("#6355F4FF", 1.0)), E(49.0, 54.0, 15.0, G("#B1294865", "#D83F3464"), P("#B991FF", 1.3)), E(49.0, 54.0, 10.0, S("#B5142638"), P("#9855F4FF", 1.0)), N("M49,44 L49,64 M39,54 L59,54 M42,47 L56,61 M42,61 L56,47", "#55F4FF", 1.4), E(49.0, 54.0, 4.0, G("#D7FBFF", "#8CAFFF"), P("#D7FBFF", 0.8)), R(20.0, 37.0, 6.0, 10.0, 1.5, G("#72E7E8", "#365E8A"), P("#55F4FF", 0.7)), R(20.0, 62.0, 6.0, 10.0, 1.5, G("#72E7E8", "#365E8A"), P("#55F4FF", 0.7)), N("M38,24 L52,24 M57,24 L64,24", "#B991FF", 1.0), E(65.0, 38.0, 1.8, S("#A5FFDB")), D("M30,84 L42,84 M57,84 L69,84", null, P("#8055F4FF", 1.3))));
	}
}
