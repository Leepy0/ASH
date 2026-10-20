using System;
using System.Collections.Generic;

namespace ASH.Core.Geometry;

/// <summary>
/// Plain segment geometry: every common point is returned, including ones at segment endpoints.
/// End-to-end touch exclusion is path-level and lives in PathSplitter.
/// </summary>
public static class Intersections
{
    public static List<Point2> SegSeg(Segment a, Segment b, double tol)
    {
        if (a is LineSeg la)
        {
            if (b is LineSeg lb) return LineLine(la, lb, tol);
            if (b is ArcSeg ab) return LineArc(la, ab, tol);
        }
        else if (a is ArcSeg aa)
        {
            if (b is LineSeg lb) return LineArc(lb, aa, tol);
            if (b is ArcSeg ab) return ArcArc(aa, ab, tol);
        }
        return new List<Point2>();
    }

    public static List<Point2> LineLine(LineSeg a, LineSeg b, double tol)
    {
        var res = new List<Point2>();
        double rx = a.End.X - a.Start.X, ry = a.End.Y - a.Start.Y;
        double sx = b.End.X - b.Start.X, sy = b.End.Y - b.Start.Y;
        double lr = Math.Sqrt(rx * rx + ry * ry), ls = Math.Sqrt(sx * sx + sy * sy);
        if (lr <= 0 || ls <= 0) return res;
        double den = rx * sy - ry * sx;
        if (Math.Abs(den) < 1e-12 * lr * ls) return res; // parallel / collinear: not a crossing
        double qx = b.Start.X - a.Start.X, qy = b.Start.Y - a.Start.Y;
        double t = (qx * sy - qy * sx) / den, u = (qx * ry - qy * rx) / den;
        double et = tol / lr, eu = tol / ls;
        if (t < -et || t > 1 + et || u < -eu || u > 1 + eu) return res;
        Add(res, a, b, new Point2(a.Start.X + t * rx, a.Start.Y + t * ry), tol);
        return res;
    }

    public static List<Point2> LineArc(LineSeg a, ArcSeg b, double tol)
    {
        var res = new List<Point2>();
        double dx = a.End.X - a.Start.X, dy = a.End.Y - a.Start.Y;
        double fx = a.Start.X - b.Center.X, fy = a.Start.Y - b.Center.Y;
        double A = dx * dx + dy * dy;
        if (A <= 0) return res;
        double R = b.Radius, B = 2 * (fx * dx + fy * dy), C = fx * fx + fy * fy - R * R;
        double disc = B * B - 4 * A * C;
        if (disc < 0) return res;
        double sq = Math.Sqrt(disc), len = Math.Sqrt(A), et = tol / len;
        var ts = sq < 1e-12 ? new[] { -B / (2 * A) } : new[] { (-B - sq) / (2 * A), (-B + sq) / (2 * A) };
        foreach (var t in ts)
        {
            if (t < -et || t > 1 + et) continue;
            var p = new Point2(a.Start.X + t * dx, a.Start.Y + t * dy);
            if (OnArc(b, p, tol)) Add(res, a, b, p, tol);
        }
        return res;
    }

    public static List<Point2> ArcArc(ArcSeg a, ArcSeg b, double tol)
    {
        var res = new List<Point2>();
        double dx = b.Center.X - a.Center.X, dy = b.Center.Y - a.Center.Y;
        double d = Math.Sqrt(dx * dx + dy * dy), r1 = a.Radius, r2 = b.Radius;
        if (d < 1e-12 || d > r1 + r2 + tol || d < Math.Abs(r1 - r2) - tol) return res;
        double x = (d * d + r1 * r1 - r2 * r2) / (2 * d);
        double h = Math.Sqrt(Math.Max(0, r1 * r1 - x * x));
        double mx = a.Center.X + x * dx / d, my = a.Center.Y + x * dy / d;
        var cands = h < tol * 1e-3
            ? new[] { new Point2(mx, my) }
            : new[] { new Point2(mx - h * dy / d, my + h * dx / d), new Point2(mx + h * dy / d, my - h * dx / d) };
        foreach (var p in cands)
            if (OnArc(a, p, tol) && OnArc(b, p, tol)) Add(res, a, b, p, tol);
        return res;
    }

    // p is assumed to lie on the arc's circle; checks the angular range with a tol-sized slack at both ends.
    private static bool OnArc(ArcSeg arc, Point2 p, double tol)
    {
        double ta = tol / arc.Radius * 180.0 / Math.PI;
        double m = Math.Abs(arc.AngleFromStart(p));
        return m <= Math.Abs(arc.SweepDeg) + ta || m >= 360.0 - ta;
    }

    private static void Add(List<Point2> res, Segment a, Segment b, Point2 p, double tol)
    {
        res.Add(p);
    }

}
