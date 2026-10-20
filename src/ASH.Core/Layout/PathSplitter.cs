using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;

namespace ASH.Core.Layout;

public sealed record SplitResult(List<LayoutPath> Paths, int IntersectionCount, Dictionary<string, List<string>> Renamed);

public static class PathSplitter
{
    public static SplitResult SplitAll(IReadOnlyList<LayoutPath> paths, double tol)
    {
        var cuts = new Dictionary<int, List<double>>();
        var points = Collect(paths, tol, cuts);
        var result = new List<LayoutPath>();
        var renamed = new Dictionary<string, List<string>>();
        for (int i = 0; i < paths.Count; i++)
        {
            var pieces = cuts.TryGetValue(i, out var c) ? SplitAt(paths[i], c, tol) : new List<LayoutPath> { paths[i] };
            if (pieces.Count > 1) renamed[paths[i].Name] = pieces.Select(p => p.Name).ToList();
            result.AddRange(pieces);
        }
        return new SplitResult(result, points.Count, renamed);
    }

    public static IEnumerable<Point2> FindUnsplitIntersections(IReadOnlyList<LayoutPath> paths, double tol) =>
        Collect(paths, tol, new Dictionary<int, List<double>>());

    /// <summary>Cuts p at the given distances from its start. Cuts within tol of either end are ignored; cuts near a segment boundary snap to it.</summary>
    public static List<LayoutPath> SplitAt(LayoutPath p, IEnumerable<double> distances, double tol)
    {
        double total = p.Length;
        var offs = new List<double> { 0 };
        foreach (var s in p.Segments) offs.Add(offs[offs.Count - 1] + s.Length);
        var cs = new List<double>();
        foreach (var d0 in distances.OrderBy(x => x))
        {
            if (d0 <= tol || d0 >= total - tol) continue;
            double d = d0;
            foreach (var o in offs) if (Math.Abs(o - d0) <= tol) { d = o; break; }
            if (cs.Count == 0 || d - cs[cs.Count - 1] > tol) cs.Add(d);
        }
        if (cs.Count == 0) return new List<LayoutPath> { p };

        var bounds = new List<double> { 0 };
        bounds.AddRange(cs);
        bounds.Add(total);
        var res = new List<LayoutPath>();
        for (int k = 0; k < bounds.Count - 1; k++)
        {
            double a = bounds[k], b = bounds[k + 1];
            var piece = new LayoutPath(p.Name + "_" + (k + 1)) { Id = p.Id + "#" + (k + 1), IsSpur = p.IsSpur };
            for (int i = 0; i < p.Segments.Count; i++)
            {
                double lo = Math.Max(a, offs[i]), hi = Math.Min(b, offs[i + 1]);
                if (hi - lo > 1e-9) piece.Segments.Add(Sub(p.Segments[i], lo - offs[i], hi - offs[i]));
            }
            res.Add(piece);
        }
        return res;
    }

    private static Segment Sub(Segment s, double d0, double d1)
    {
        if (d0 <= 0 && d1 >= s.Length) return s;
        if (s is ArcSeg arc) return new ArcSeg(arc.Center, arc.PointAt(d0), arc.SweepDeg * (d1 - d0) / arc.Length);
        return new LineSeg(s.PointAt(d0), s.PointAt(d1));
    }

    // Finds distinct intersection points across paths and fills cuts (path index -> distances from path start).
    private static List<Point2> Collect(IReadOnlyList<LayoutPath> paths, double tol, Dictionary<int, List<double>> cuts)
    {
        var segs = new List<(int Path, double Off, Segment Seg, double X0, double X1, double Y0, double Y1)>();
        for (int i = 0; i < paths.Count; i++)
        {
            double off = 0;
            foreach (var s in paths[i].Segments)
            {
                var (x0, y0, x1, y1) = Bounds(s);
                segs.Add((i, off, s, x0 - tol, x1 + tol, y0 - tol, y1 + tol));
                off += s.Length;
            }
        }
        var order = Enumerable.Range(0, segs.Count).OrderBy(i => segs[i].X0).ToList();
        var points = new List<Point2>();
        for (int oi = 0; oi < order.Count; oi++)
        {
            var a = segs[order[oi]];
            for (int oj = oi + 1; oj < order.Count; oj++)
            {
                var b = segs[order[oj]];
                if (b.X0 > a.X1) break;
                if (a.Path == b.Path || b.Y0 > a.Y1 || a.Y0 > b.Y1) continue;
                foreach (var pt in Intersections.SegSeg(a.Seg, b.Seg, tol))
                {
                    if (NearPathEnd(paths[a.Path], pt, tol) && NearPathEnd(paths[b.Path], pt, tol)) continue; // plain end-to-end touch
                    if (!points.Any(q => q.DistanceTo(pt) <= tol)) points.Add(pt);
                    AddCut(cuts, paths, a.Path, a.Off + a.Seg.DistanceAlong(pt), tol);
                    AddCut(cuts, paths, b.Path, b.Off + b.Seg.DistanceAlong(pt), tol);
                }
            }
        }
        return points;
    }

    private static bool NearPathEnd(LayoutPath p, Point2 pt, double tol) => p.Start.DistanceTo(pt) <= tol || p.End.DistanceTo(pt) <= tol;

    private static void AddCut(Dictionary<int, List<double>> cuts, IReadOnlyList<LayoutPath> paths, int idx, double d, double tol)
    {
        if (d <= tol || d >= paths[idx].Length - tol) return;
        if (!cuts.TryGetValue(idx, out var l)) cuts[idx] = l = new List<double>();
        if (!l.Any(x => Math.Abs(x - d) <= tol)) l.Add(d);
    }

    private static (double, double, double, double) Bounds(Segment s)
    {
        double x0 = Math.Min(s.Start.X, s.End.X), x1 = Math.Max(s.Start.X, s.End.X);
        double y0 = Math.Min(s.Start.Y, s.End.Y), y1 = Math.Max(s.Start.Y, s.End.Y);
        if (s is ArcSeg a)
        {
            double r = a.Radius, m = Math.Abs(a.SweepDeg);
            foreach (var q in new[] { new Point2(a.Center.X + r, a.Center.Y), new Point2(a.Center.X, a.Center.Y + r), new Point2(a.Center.X - r, a.Center.Y), new Point2(a.Center.X, a.Center.Y - r) })
                if (Math.Abs(a.AngleFromStart(q)) <= m)
                {
                    x0 = Math.Min(x0, q.X); x1 = Math.Max(x1, q.X); y0 = Math.Min(y0, q.Y); y1 = Math.Max(y1, q.Y);
                }
        }
        return (x0, y0, x1, y1);
    }
}
