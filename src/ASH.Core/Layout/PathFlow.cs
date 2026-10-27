using System;
using System.Collections.Generic;
using ASH.Core.Geometry;

namespace ASH.Core.Layout;

/// <summary>One frame of the AGVFLOW preview: small arrows spread along every path, moved forward by phase.</summary>
public static class PathFlow
{
    /// <summary>
    /// Arrows (Left, Tip, Right) about spacing apart, size long, pointing the way of travel (two-way paths: both ways).
    /// phase 0..1 moves each arrow one gap forward; the count and order never change with phase, so a frame can update the previous one in place.
    /// </summary>
    public static List<(Point2 Left, Point2 Tip, Point2 Right)> Frame(IReadOnlyList<LayoutPath> paths, double spacing, double size, double phase)
    {
        var res = new List<(Point2, Point2, Point2)>();
        phase -= Math.Floor(phase);
        foreach (var p in paths)
        {
            if (p.Segments.Count == 0) continue;
            double len = p.Length;
            if (!(len > 0)) continue;
            int n = Math.Max(1, (int)(len / spacing));
            double step = len / n;
            for (int k = 0; k < n; k++)
            {
                double d = (k + phase) * step;
                res.Add(Arrow(p, d, len, size, true));
                if (p.IsTwoWay) res.Add(Arrow(p, len - d, len, size, false));
            }
        }
        return res;
    }

    private static (Point2, Point2, Point2) Arrow(LayoutPath p, double d, double len, double size, bool forward)
    {
        double e = Math.Max(len * 1e-3, 1e-9);
        Point2 a = p.PointAt(Math.Max(0, d - e)), b = p.PointAt(Math.Min(len, d + e)), m = p.PointAt(d);
        double dx = b.X - a.X, dy = b.Y - a.Y, l = Math.Sqrt(dx * dx + dy * dy);
        if (l <= 0) { dx = 1; dy = 0; l = 1; }
        double s = forward ? 1 : -1, ux = s * dx / l, uy = s * dy / l, h = size / 2, w = size / 3;
        var tip = new Point2(m.X + ux * h, m.Y + uy * h);
        Point2 back = new Point2(m.X - ux * h, m.Y - uy * h);
        return (new Point2(back.X - uy * w, back.Y + ux * w), tip, new Point2(back.X + uy * w, back.Y - ux * w));
    }
}
