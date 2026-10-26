using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Core.Traffic;

public sealed record CpProposal(List<LayoutCp> Cps, List<string> Warnings);

public static class TrafficCpGenerator
{
    /// <summary>Path distances where the path goes from outside to inside the rect. insideOnly = never leaves the rect.</summary>
    internal static List<double> Entries(LayoutPath path, Rect2 rect, double tol, out bool insideOnly)
    {
        var cuts = Cuts(path, rect, tol);
        var res = new List<double>();
        bool? prevInside = null, firstInside = null; insideOnly = true;
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            if (cuts[i + 1] - cuts[i] < 1e-9) continue;
            bool inside = rect.Contains(path.PointAt((cuts[i] + cuts[i + 1]) / 2));
            if (!inside) insideOnly = false;
            if (inside && prevInside == false) res.Add(cuts[i]);
            firstInside ??= inside;
            prevInside = inside;
        }
        // A path starting on the boundary and running inward enters at 0; one ending on the boundary from outside enters at its end.
        if (firstInside == true && OnBoundary(rect, path.Start, tol)) res.Insert(0, 0);
        if (prevInside == false && OnBoundary(rect, path.End, tol)) res.Add(path.Length);
        return res;
    }

    /// <summary>Path distance ranges (from, to) inside the rect, in drawing order.</summary>
    internal static List<(double From, double To)> Spans(LayoutPath path, Rect2 rect, double tol)
    {
        var cuts = Cuts(path, rect, tol);
        var res = new List<(double From, double To)>();
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            if (cuts[i + 1] - cuts[i] < 1e-9 || !rect.Contains(path.PointAt((cuts[i] + cuts[i + 1]) / 2))) continue;
            if (res.Count > 0 && cuts[i] - res[res.Count - 1].To < 1e-9) res[res.Count - 1] = (res[res.Count - 1].From, cuts[i + 1]);
            else res.Add((cuts[i], cuts[i + 1]));
        }
        return res;
    }

    /// <summary>Sorted path distances where the path starts, ends or crosses the rect's edges.</summary>
    static List<double> Cuts(LayoutPath path, Rect2 rect, double tol)
    {
        var edges = new[]
        {
            new LineSeg(rect.Min, new Point2(rect.Max.X, rect.Min.Y)), new LineSeg(new Point2(rect.Max.X, rect.Min.Y), rect.Max),
            new LineSeg(rect.Max, new Point2(rect.Min.X, rect.Max.Y)), new LineSeg(new Point2(rect.Min.X, rect.Max.Y), rect.Min),
        };
        var cuts = new List<double> { 0 };
        double off = 0;
        foreach (var s in path.Segments)
        {
            foreach (var e in edges)
                foreach (var pt in Intersections.SegSeg(s, e, tol)) cuts.Add(off + s.DistanceAlong(pt));
            off += s.Length;
            cuts.Add(off);
        }
        cuts.Sort();
        return cuts;
    }

    /// <summary>A two-way path is entered from both ends, so its traffic CPs are placed for travel in each direction.</summary>
    static IEnumerable<LayoutPath> Directions(LayoutPath p)
    {
        yield return p;
        if (!p.IsTwoWay) yield break;
        var back = new LayoutPath(p.Name) { Id = p.Id, IsSpur = p.IsSpur, IsTwoWay = true, IsCrab = p.IsCrab };
        for (int i = p.Segments.Count - 1; i >= 0; i--) back.Segments.Add(p.Segments[i].Reversed());
        yield return back;
    }

    static bool OnBoundary(Rect2 r, Point2 p, double tol) => r.Expand(tol).Contains(p) && !r.Expand(-tol).Contains(p);

    // Segment bbox disjoint from rect (inflated by tol).
    private static bool Outside(LineSeg s, Rect2 r, double tol) =>
        Math.Max(s.Start.X, s.End.X) < r.Min.X - tol || Math.Min(s.Start.X, s.End.X) > r.Max.X + tol ||
        Math.Max(s.Start.Y, s.End.Y) < r.Min.Y - tol || Math.Min(s.Start.Y, s.End.Y) > r.Max.Y + tol;

    public static CpProposal Generate(Layout.Layout l, TrafficParams p)
    {
        var cps = new List<LayoutCp>();
        var warns = new List<string>();
        var names = l.Cps.Select(c => c.Name).ToList();
        double dup = l.Tolerance * 10;

        void Add(Point2 pos)
        {
            if (l.Cps.Concat(cps).Any(c => c.Position.DistanceTo(pos) <= dup)) return;
            var n = AutoNamer.Next("T", names);
            names.Add(n);
            cps.Add(new LayoutCp { Name = n, Type = "traffic", Cap = 1, Position = pos });
        }

        if (l.Blocks.Count == 0) warns.Add("block이 없습니다. AGVBLOCKAUTO를 먼저 실행하세요.");
        foreach (var b in l.Blocks)
            foreach (var path in l.Paths.SelectMany(Directions))
            {
                if (path.Segments.All(sg => sg is LineSeg ls && Outside(ls, b.Rect, l.Tolerance))) continue; // no entry, no warning
                var entries = Entries(path, b.Rect, l.Tolerance, out var insideOnly);
                if (insideOnly && entries.Count == 0) { warns.Add($"{path.Name}: 경로가 {b.Name} 안에서만 존재하여 CP를 배치하지 않았습니다"); continue; }
                foreach (var d in entries)
                {
                    double at = d - p.EffectiveClaimDist;
                    if (at < 0) { at = l.Tolerance; warns.Add($"{path.Name}: ClaimDist 부족, {(d - at).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)} m에 배치"); }
                    Add(path.PointAt(at));
                }
            }

        if (p.SpacingCps && p.CpSpacing <= 0) warns.Add("CpSpacing는 0보다 커야 합니다");
        foreach (var path in l.Paths)
        {
            if (p.SpacingCps && p.CpSpacing > 0)
                for (double d = p.CpSpacing; d < path.Length - 1e-9; d += p.CpSpacing) Add(path.PointAt(d));
            if (p.CurveCps)
                foreach (var a in path.Segments.OfType<ArcSeg>()) { Add(a.Start); Add(a.End); }
        }
        return new CpProposal(cps, warns);
    }
}
