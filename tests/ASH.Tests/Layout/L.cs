using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Tests.Layout;

public static class L
{
    public static LayoutPath Line(string name, double x1, double y1, double x2, double y2) =>
        new LayoutPath(name) { Segments = { new LineSeg(new Point2(x1, y1), new Point2(x2, y2)) } };

    public static ASH.Core.Layout.Layout Of(params LayoutPath[] paths) =>
        new ASH.Core.Layout.Layout { Paths = paths.ToList() };

    public static LayoutPath Spur(string name, double x1, double y1, double x2, double y2)
    {
        var p = Line(name, x1, y1, x2, y2);
        p.IsSpur = true;
        return p;
    }

    public static LayoutCp Cp(string name, double x, double y, string type = "traffic", string group = "") =>
        new LayoutCp { Name = name, Type = type, Group = group, Position = new Point2(x, y) };

    public static LayoutBlock Block(string name, double minx, double miny, double maxx, double maxy, int cap = 1) =>
        new LayoutBlock { Name = name, Cap = cap, Rect = new Rect2(new Point2(minx, miny), new Point2(maxx, maxy)) };
}
