using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace DesktopPop;

/// <summary>기본 가방 그림 "폴더" (코드로 그린 벡터 그림).</summary>
internal static class DefaultIcons
{
	private static Brush Shine => S("#40FFFFFF");

	private static Color C(string hex)
	{
		return (Color)ColorConverter.ConvertFromString(hex);
	}

	private static Brush V(string top, string bottom)
	{
		return new LinearGradientBrush(C(top), C(bottom), new Point(0.0, 0.0), new Point(0.0, 1.0));
	}

	private static Brush S(string hex)
	{
		return new SolidColorBrush(C(hex));
	}

	private static Pen P(string hex, double w = 1.8)
	{
		return new Pen(S(hex), w)
		{
			LineJoin = PenLineJoin.Round,
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
	}

	private static DrawingGroup Icon(params Drawing[] parts)
	{
		DrawingGroup drawingGroup = new DrawingGroup();
		drawingGroup.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0.0, 0.0, 100.0, 100.0))));
		foreach (Drawing value in parts)
		{
			drawingGroup.Children.Add(value);
		}
		return drawingGroup;
	}

	private static GeometryDrawing D(string path, Brush? fill, Pen? pen = null)
	{
		return new GeometryDrawing(fill, pen, Geometry.Parse(path));
	}

	private static GeometryDrawing R(double x, double y, double w, double h, double r, Brush? fill, Pen? pen = null)
	{
		return new GeometryDrawing(fill, pen, new RectangleGeometry(new Rect(x, y, w, h), r, r));
	}

	private static GeometryDrawing E(double cx, double cy, double rx, double ry, Brush? fill, Pen? pen = null)
	{
		return new GeometryDrawing(fill, pen, new EllipseGeometry(new Point(cx, cy), rx, ry));
	}

	public static IEnumerable<(string name, Drawing drawing)> All()
	{
		yield return (name: "폴더", drawing: Icon(D("M10,26 Q10,19 17,19 L40,19 L48,27 L83,27 Q90,27 90,34 L90,80 Q90,87 83,87 L17,87 Q10,87 10,80 Z", V("#F2B53A", "#C98A12"), P("#8A5A08")), D("M10,42 Q10,36 16,36 L84,36 Q90,36 90,42 L90,80 Q90,87 83,87 L17,87 Q10,87 10,80 Z", V("#FFD56A", "#F0A928"), P("#8A5A08")), R(16.0, 40.0, 68.0, 5.0, 2.5, Shine)));
	}
}
