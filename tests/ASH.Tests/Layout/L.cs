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
}
