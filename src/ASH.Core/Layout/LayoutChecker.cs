using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;

namespace ASH.Core.Layout;

public static class LayoutChecker
{
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

        foreach (var c in l.Cps)
        {
            string id = string.IsNullOrWhiteSpace(c.Name) ? c.Id : c.Name;
            if (string.IsNullOrWhiteSpace(c.Name))
                res.Add(new Finding(FindingCode.UnnamedCp, "CP has no name", c.Position, id));
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
                double d = s.DistanceAlong(p); // clamped for lines; arcs snap to the nearer end outside the sweep
                double perp = p.DistanceTo(s.PointAt(d));
                if (best == null || perp < best.Value.Item3) best = (path, off + d, perp);
                off += s.Length;
            }
        }
        return best;
    }

    private static bool Touches(Rect2 r, Segment s, double tol)
    {
        if (r.Contains(s.Start)) return true;
        Point2 a = r.Min, b = new(r.Max.X, r.Min.Y), c = r.Max, d = new(r.Min.X, r.Max.Y);
        foreach (var e in new[] { new LineSeg(a, b), new LineSeg(b, c), new LineSeg(c, d), new LineSeg(d, a) })
            if (Intersections.SegSeg(s, e, tol).Count > 0) return true;
        return false;
    }
}
