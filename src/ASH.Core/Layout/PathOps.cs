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

    /// <summary>+1 when the point is left of the direction of travel at the nearest point of the path, -1 when right, 0 when on the path (within tol).</summary>
    public static int SideOf(LayoutPath p, Point2 pt, double tol)
    {
        double best = double.MaxValue, bd = 0;
        Segment? bs = null;
        foreach (var s in p.Segments)
        {
            double d = s.DistanceAlong(pt), dist = s.PointAt(d).DistanceTo(pt);
            if (dist < best) { best = dist; bs = s; bd = d; }
        }
        if (bs == null || best <= tol) return 0;
        double e = Math.Max(bs.Length * 1e-3, 1e-9);
        var a = bs.PointAt(Math.Max(0, bd - e));
        var b = bs.PointAt(Math.Min(bs.Length, bd + e));
        var q = bs.PointAt(bd);
        double cross = (b.X - a.X) * (pt.Y - q.Y) - (b.Y - a.Y) * (pt.X - q.X);
        return cross >= 0 ? 1 : -1;
    }

    /// <summary>
    /// Rounds the corners where two straight segments of ONE path meet (a polyline drawn with PLINE). With <paramref name="near"/> only the corner closest to it,
    /// otherwise every corner. The path keeps its name and type. Corners that do not fit the radius are skipped and listed.
    /// Null when the path has no corner of two non-parallel lines.
    /// </summary>
    public static (LayoutPath Path, int Rounded, List<string> Skipped)? FilletCorners(LayoutPath p, double radius, double tol, Point2? near = null)
    {
        int n = p.Segments.Count;
        if (radius <= 0 || n < 2) return null;
        var corners = new List<int>();
        for (int i = 0; i + 1 < n; i++)
            if (p.Segments[i] is LineSeg a && p.Segments[i + 1] is LineSeg b && a.Length > tol && b.Length > tol
                && Math.Abs(Unit(a).X * Unit(b).Y - Unit(a).Y * Unit(b).X) >= 1e-9) corners.Add(i);
        if (corners.Count == 0) return null;
        if (near is Point2 q) corners = new List<int> { corners.OrderBy(i => p.Segments[i].End.DistanceTo(q)).First() };

        var starts = p.Segments.Select(s => s.Start).ToArray();
        var ends = p.Segments.Select(s => s.End).ToArray();
        var arcs = new Dictionary<int, ArcSeg>();
        var skipped = new List<string>();
        foreach (int i in corners)
        {
            var la = (LineSeg)p.Segments[i];
            var lb = (LineSeg)p.Segments[i + 1];
            var u1 = Unit(la);
            var u2 = Unit(lb);
            double cross = u1.X * u2.Y - u1.Y * u2.X, dot = u1.X * u2.X + u1.Y * u2.Y;
            double theta = Math.Atan2(cross, dot);
            double t = radius * Math.Tan(Math.Abs(theta) / 2);
            double remainA = starts[i].DistanceTo(ends[i]), remainB = starts[i + 1].DistanceTo(ends[i + 1]);
            var corner = la.End;
            if (t >= remainA - tol || t >= remainB - tol)
            {
                skipped.Add(string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###})", corner.X, corner.Y));
                continue;
            }
            var aEnd = new Point2(corner.X - u1.X * t, corner.Y - u1.Y * t);
            var bStart = new Point2(corner.X + u2.X * t, corner.Y + u2.Y * t);
            double side = Math.Sign(theta) * radius;
            var center = new Point2(aEnd.X - u1.Y * side, aEnd.Y + u1.X * side);
            ends[i] = aEnd;
            starts[i + 1] = bStart;
            arcs[i] = new ArcSeg(center, aEnd, theta * 180 / Math.PI);
        }
        if (arcs.Count == 0) return (p, 0, skipped);
        var segs = new List<Segment>();
        for (int i = 0; i < n; i++)
        {
            segs.Add(p.Segments[i] is LineSeg ? new LineSeg(starts[i], ends[i]) : p.Segments[i]);
            if (arcs.TryGetValue(i, out var arc)) segs.Add(arc);
        }
        return (Copy(p, p.Name, segs, keepId: false), arcs.Count, skipped);
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
