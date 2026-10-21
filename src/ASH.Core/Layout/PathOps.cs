using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;

namespace ASH.Core.Layout;

/// <summary>Pure path edits behind the AGVREVERSE/OFFSET/ARRAY/JOIN/FILLET commands.</summary>
public static class PathOps
{
    public static LayoutPath Reverse(LayoutPath p) =>
        Copy(p, p.Name, Enumerable.Reverse(p.Segments).Select(s => s.Reversed()), keepId: true);

    /// <summary>Parallel copy at distance d (positive = left of travel). Arcs change radius; line corners are re-met.</summary>
    public static LayoutPath Offset(LayoutPath p, double d, bool reverseDirection)
    {
        var segs = p.Segments.Select(s => OffsetSeg(s, d)).ToList();
        for (int i = 0; i + 1 < segs.Count; i++)
            if (segs[i] is LineSeg a && segs[i + 1] is LineSeg b && LineMeet(a, b) is Point2 m)
            {
                segs[i] = new LineSeg(a.Start, m);
                segs[i + 1] = new LineSeg(m, b.End);
            }
        var r = Copy(p, p.Name, segs, keepId: false);
        return reverseDirection ? Reverse(r) : r;
    }

    /// <summary>count translated copies, copy k (1-based) moved by (dx·k, dy·k), named name + suffix counting up from nameSuffixStart.</summary>
    public static List<LayoutPath> Array(LayoutPath p, double dx, double dy, int count, string nameSuffixStart = "_1")
    {
        int digits = nameSuffixStart.Length;
        while (digits > 0 && char.IsDigit(nameSuffixStart[digits - 1])) digits--;
        string sep = nameSuffixStart.Substring(0, digits);
        int first = digits < nameSuffixStart.Length ? int.Parse(nameSuffixStart.Substring(digits), CultureInfo.InvariantCulture) : 1;
        var res = new List<LayoutPath>();
        for (int k = 1; k <= count; k++)
        {
            var v = new Point2(dx * k, dy * k);
            res.Add(Copy(p, p.Name + sep + (first + k - 1).ToString(CultureInfo.InvariantCulture), p.Segments.Select(s => Translate(s, v)), keepId: false));
        }
        return res;
    }

    public static Segment Translate(Segment s, Point2 v)
    {
        Point2 T(Point2 q) => new Point2(q.X + v.X, q.Y + v.Y);
        return s is ArcSeg a ? new ArcSeg(T(a.Center), T(a.Start), a.SweepDeg) : new LineSeg(T(s.Start), T(s.End));
    }

    /// <summary>a then b, only when a.End ≈ b.Start; takes a's name and type.</summary>
    public static LayoutPath? Join(LayoutPath a, LayoutPath b, double tol)
    {
        if (a.Segments.Count == 0 || b.Segments.Count == 0 || a.End.DistanceTo(b.Start) > tol) return null;
        return Copy(a, a.Name, a.Segments.Concat(b.Segments), keepId: false);
    }

    /// <summary>Two single-line paths meeting at a.End ≈ b.Start: trims both by r·tan(θ/2) and returns the arc path "&lt;a&gt;_f" between them.
    /// Null when the lines are collinear or too short for the radius.</summary>
    public static (LayoutPath A, LayoutPath B, LayoutPath Fillet)? Fillet(LayoutPath a, LayoutPath b, double radius, double tol)
    {
        if (radius <= 0 || a.Segments.Count != 1 || b.Segments.Count != 1 || !(a.Segments[0] is LineSeg la) || !(b.Segments[0] is LineSeg lb)) return null;
        if (la.End.DistanceTo(lb.Start) > tol || la.Length <= tol || lb.Length <= tol) return null;
        var u1 = Unit(la);
        var u2 = Unit(lb);
        double cross = u1.X * u2.Y - u1.Y * u2.X, dot = u1.X * u2.X + u1.Y * u2.Y;
        if (Math.Abs(cross) < 1e-9) return null;
        double theta = Math.Atan2(cross, dot); // signed turn, + = left
        double t = radius * Math.Tan(Math.Abs(theta) / 2);
        if (t >= la.Length - tol || t >= lb.Length - tol) return null;
        var p = la.End;
        var aEnd = new Point2(p.X - u1.X * t, p.Y - u1.Y * t);
        var bStart = new Point2(lb.Start.X + u2.X * t, lb.Start.Y + u2.Y * t);
        double side = Math.Sign(theta) * radius;
        var center = new Point2(aEnd.X - u1.Y * side, aEnd.Y + u1.X * side);
        var arc = new ArcSeg(center, aEnd, theta * 180 / Math.PI);
        return (Copy(a, a.Name, new[] { new LineSeg(la.Start, aEnd) }, keepId: true),
                Copy(b, b.Name, new[] { new LineSeg(bStart, lb.End) }, keepId: true),
                Copy(a, a.Name + "_f", new Segment[] { arc }, keepId: false));
    }

    private static Segment OffsetSeg(Segment s, double d)
    {
        if (s is ArcSeg a)
        {
            double r = a.Radius, nr = a.SweepDeg >= 0 ? r - d : r + d; // left of a CCW arc is toward the centre
            if (nr <= 0) throw new ArgumentException($"오프셋 {d}이 원호 반경 {r}보다 커서 원호가 사라집니다.");
            double k = nr / r;
            return new ArcSeg(a.Center, new Point2(a.Center.X + (a.Start.X - a.Center.X) * k, a.Center.Y + (a.Start.Y - a.Center.Y) * k), a.SweepDeg);
        }
        var u = Unit(s);
        var n = new Point2(-u.Y * d, u.X * d);
        return new LineSeg(new Point2(s.Start.X + n.X, s.Start.Y + n.Y), new Point2(s.End.X + n.X, s.End.Y + n.Y));
    }

    // Intersection of the infinite lines through a and b; null when parallel.
    private static Point2? LineMeet(LineSeg a, LineSeg b)
    {
        var u = Unit(a);
        var w = Unit(b);
        double den = u.X * w.Y - u.Y * w.X;
        if (Math.Abs(den) < 1e-9) return null;
        double t = ((b.Start.X - a.Start.X) * w.Y - (b.Start.Y - a.Start.Y) * w.X) / den;
        return new Point2(a.Start.X + u.X * t, a.Start.Y + u.Y * t);
    }

    private static Point2 Unit(Segment s)
    {
        double dx = s.End.X - s.Start.X, dy = s.End.Y - s.Start.Y, len = Math.Sqrt(dx * dx + dy * dy);
        return len > 0 ? new Point2(dx / len, dy / len) : new Point2(1, 0);
    }

    private static LayoutPath Copy(LayoutPath src, string name, IEnumerable<Segment> segs, bool keepId)
    {
        var p = new LayoutPath(name) { IsSpur = src.IsSpur, IsTwoWay = src.IsTwoWay, IsCrab = src.IsCrab, Segments = segs.ToList() };
        if (keepId) p.Id = src.Id;
        return p;
    }
}
