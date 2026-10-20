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
        var res = new List<double>();
        bool? prevInside = null; insideOnly = true;
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            if (cuts[i + 1] - cuts[i] < 1e-9) continue;
            bool inside = rect.Contains(path.PointAt((cuts[i] + cuts[i + 1]) / 2));
            if (!inside) insideOnly = false;
            if (inside && prevInside == false) res.Add(cuts[i]);
            prevInside = inside;
        }
        return res;
    }

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
            foreach (var path in l.Paths)
            {
                var entries = Entries(path, b.Rect, l.Tolerance, out var insideOnly);
                if (insideOnly && entries.Count == 0) { warns.Add($"{path.Name}: 경로가 {b.Name} 안에서만 존재하여 CP를 배치하지 않았습니다"); continue; }
                foreach (var d in entries)
                {
                    double at = d - p.EffectiveClaimDist;
                    if (at < 0) { at = l.Tolerance; warns.Add($"{path.Name}: ClaimDist 부족, {at:F3} m에 배치"); }
                    Add(path.PointAt(at));
                }
            }

        foreach (var path in l.Paths)
        {
            if (p.SpacingCps)
                for (double d = p.CpSpacing; d < path.Length - 1e-9; d += p.CpSpacing) Add(path.PointAt(d));
            if (p.CurveCps)
                foreach (var a in path.Segments.OfType<ArcSeg>()) { Add(a.Start); Add(a.End); }
        }
        return new CpProposal(cps, warns);
    }
}
