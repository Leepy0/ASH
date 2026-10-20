using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;

namespace ASH.Core.Layout;

public static class LayoutChecker
{
    static readonly HashSet<string> Reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "name", "type", "at", "spur", "crab", "piece", "item", "cap", "align", "begx", "begy", "endx", "endy", "cenx", "ceny", "upz", "angle" };

    /// <summary>Usable as a single .asy token: non-empty, no whitespace, not a reserved keyword (case-insensitive).</summary>
    public static bool IsValidName(string n) => n.Length > 0 && !n.Any(char.IsWhiteSpace) && !Reserved.Contains(n);

    public static List<Finding> Check(Layout l, CheckOptions o)
    {
        var res = new List<Finding>();
        var g = PathGraph.Build(l);

        foreach (var (path, atStart) in g.DanglingEnds)
            if (!path.IsSpur)
                res.Add(new Finding(FindingCode.DanglingEnd, $"Path {path.Name} has a free {(atStart ? "start" : "end")}",
                    atStart ? path.Start : path.End, path.Name));

        foreach (var n in g.Nodes)
            if (n.Degree >= 2 && (n.InCount == 0 || n.OutCount == 0))
                res.Add(new Finding(FindingCode.DeadEndNode,
                    n.InCount == 0 ? "Node has only outgoing paths" : "Node has only incoming paths", n.Position, null));

        var pathNames = l.Paths.GroupBy(p => p.Name).Where(x => x.Count() > 1).Select(x => x.Key);
        var dupPaths = new HashSet<string>(pathNames);
        foreach (var p in l.Paths)
        {
            var at = p.Segments.Count > 0 ? p.Start : default;
            if (dupPaths.Contains(p.Name)) res.Add(new Finding(FindingCode.DuplicatePathName, $"Duplicate path name {p.Name}", at, p.Name));
            if (!IsValidName(p.Name)) res.Add(new Finding(FindingCode.InvalidName, $"Invalid path name '{p.Name}'", at, p.Name));
        }
        foreach (var b in l.Blocks)
            if (!IsValidName(b.Name)) res.Add(new Finding(FindingCode.InvalidName, $"Invalid block name '{b.Name}'", b.Rect.Min, b.Name));

        foreach (var c in l.Cps)
        {
            string id = string.IsNullOrWhiteSpace(c.Name) ? c.Id : c.Name;
            if (string.IsNullOrWhiteSpace(c.Name))
                res.Add(new Finding(FindingCode.UnnamedCp, "CP has no name", c.Position, id));
            else if (!IsValidName(c.Name))
                res.Add(new Finding(FindingCode.InvalidName, $"Invalid CP name '{c.Name}'", c.Position, id));
            else if (l.Cps.Count(x => x.Name == c.Name) > 1)
                res.Add(new Finding(FindingCode.DuplicateCpName, $"Duplicate CP name {c.Name}", c.Position, id));

            var pr = Project(l, c.Position);
            if (pr == null || pr.Value.Offset > o.CpMaxOffset)
                res.Add(new Finding(FindingCode.CpOffPath, $"CP {id} is off the path network", c.Position, id));
        }

        foreach (var b in l.Blocks)
            if (!l.Paths.Any(p => p.Segments.Any(s => Touches(b.Rect, s, l.Tolerance))))
                res.Add(new Finding(FindingCode.BlockTouchesNoPath, $"Block {b.Name} touches no path", b.Rect.Min, b.Name));

        foreach (var pt in PathSplitter.FindUnsplitIntersections(l.Paths, l.Tolerance))
            res.Add(new Finding(FindingCode.UnsplitIntersection, "Paths cross without a split", pt, null));

        if (o.RouteCpNames != null)
        {
            var have = new HashSet<string>(l.Cps.Select(c => c.Name));
            foreach (var n in o.RouteCpNames.Distinct().Where(n => !have.Contains(n)))
                res.Add(new Finding(FindingCode.RouteCpMissing, $"Route CP {n} not found in layout", default, n));
        }
        return res;
    }

    /// <summary>Nearest point on any path. Brute force over all segments.</summary>
    public static (LayoutPath Path, double Dist, double Offset)? Project(Layout l, Point2 p)
    {
        (LayoutPath, double, double)? best = null;
        foreach (var path in l.Paths)
        {
            double off = 0;
            foreach (var s in path.Segments)
            {
                if (best != null && s is LineSeg ls && BoxDist(ls, p) >= best.Value.Item3) { off += s.Length; continue; } // can't beat best
                double d = s.DistanceAlong(p); // clamped for lines; arcs snap to the nearer end outside the sweep
                double perp = p.DistanceTo(s.PointAt(d));
                if (best == null || perp < best.Value.Item3) best = (path, off + d, perp);
                off += s.Length;
            }
        }
        return best;
    }

    private static double BoxDist(LineSeg s, Point2 p)
    {
        double dx = Math.Max(Math.Max(Math.Min(s.Start.X, s.End.X) - p.X, 0), p.X - Math.Max(s.Start.X, s.End.X));
        double dy = Math.Max(Math.Max(Math.Min(s.Start.Y, s.End.Y) - p.Y, 0), p.Y - Math.Max(s.Start.Y, s.End.Y));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool Touches(Rect2 r, Segment s, double tol)
    {
        if (s is LineSeg ls &&
            (Math.Max(ls.Start.X, ls.End.X) < r.Min.X - tol || Math.Min(ls.Start.X, ls.End.X) > r.Max.X + tol ||
             Math.Max(ls.Start.Y, ls.End.Y) < r.Min.Y - tol || Math.Min(ls.Start.Y, ls.End.Y) > r.Max.Y + tol)) return false; // bbox disjoint
        if (r.Contains(s.Start)) return true;
        Point2 a = r.Min, b = new(r.Max.X, r.Min.Y), c = r.Max, d = new(r.Min.X, r.Max.Y);
        foreach (var e in new[] { new LineSeg(a, b), new LineSeg(b, c), new LineSeg(c, d), new LineSeg(d, a) })
            if (Intersections.SegSeg(s, e, tol).Count > 0) return true;
        return false;
    }
}
