using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Core.Traffic;

public sealed record BlockProposal(List<LayoutBlock> Blocks, List<List<string>> OverlapGroups, List<string> Warnings);

public static class BlockGenerator
{
    /// <summary>One block per junction, only as big as the conflict zone: each arm reaches to where a vehicle stopped there clears every other arm
    /// (BlockReach, when set, fixes that reach instead). AutoMod releases a block on the trailing edge, so the rect needs no vehicle length on top.</summary>
    public static BlockProposal Generate(Layout.Layout l, TrafficParams p)
    {
        double half = p.VehicleWidth / 2;
        var names = l.Blocks.Select(b => b.Name).ToList();
        var blocks = new List<LayoutBlock>();
        foreach (var node in PathGraph.Build(l).Junctions)
        {
            if (l.Blocks.Any(b => b.Rect.Contains(node.Position))) continue;
            var pts = new List<Point2> { node.Position };
            for (int i = 0; i < node.Ends.Count; i++)
            {
                var (path, atStart) = node.Ends[i];
                // the rect is grown by half a width below, which carries the edge out to the clearing point
                double reach = p.BlockReach ?? Math.Max(0, ClearDist(node, i, p, l.Tolerance) - half);
                // a stub shorter than a vehicle before the next node (a corner) has no room to wait in, and the entry CP lands on the block edge:
                // in AutoMod (CrossingScenarioTests, 2026-10-22) the C loop locked up at such a block on its first pass. Take the stub whole.
                if (path.Length - reach < p.VehicleLength + p.SafetyGap) reach = path.Length;
                pts.AddRange(Walk(path, atStart, reach));
            }
            var name = AutoNamer.Next("B", names);
            names.Add(name);
            blocks.Add(new LayoutBlock { Name = name, Cap = 1, Rect = Bbox(pts).Expand(half), Id = Guid.NewGuid().ToString("N") });
        }
        var warns = blocks.Select(b => (b.Name, Cps: StopCpsIn(l, b.Rect))).Where(x => x.Cps.Count > 0)
            .Select(x => $"{x.Name} 안에 정차 CP {string.Join(", ", x.Cps)}: 거기 선 차량이 block을 계속 잡아 교착됩니다. CP를 block 밖으로 옮기세요").ToList();
        // blocks closer than one vehicle plus clearance leave no room to wait between them: a vehicle waiting for the next one still holds the first,
        // and a ring of such blocks (two paths crossing twice, close together) locks up. Those are offered for merging like overlapping ones; the rest stay one per junction.
        return new BlockProposal(blocks, OverlapGroups(blocks, p.VehicleLength + 2 * p.SafetyGap), warns);
    }

    /// <summary>Names of stop CPs (any type but traffic) inside r: a vehicle loading or parked there holds the block the whole time, which locks the junction.</summary>
    public static List<string> StopCpsIn(Layout.Layout l, Rect2 r) => l.Cps.Where(c => c.Type != "traffic" && r.Contains(c.Position)).Select(c => c.Name).ToList();

    // Distance along arm i (walked away from the node) where a vehicle standing there keeps SafetyGap to one on any other arm: both half widths plus
    // its front corner, counted by how far it leans toward the other lane (|cos| of the angle between them; 0 at a right angle). An arm leaving straight
    // opposite is the same lane run through (a crossing's far side, a merge's exit): spacing there is AutoMod's following, not a block's job.
    static double ClearDist(PathNode node, int i, TrafficParams p, double tol)
    {
        double half = p.VehicleWidth / 2, h = Math.Max(p.VehicleWidth / 20, tol);
        static Point2 At(LayoutPath q, bool st, double s) => q.PointAt(st ? s : q.Length - s);
        static Point2 Dir(Point2 a, Point2 b) { double d = a.DistanceTo(b); return d <= 0 ? default : new Point2((b.X - a.X) / d, (b.Y - a.Y) / d); }
        static double Dot(Point2 a, Point2 b) => a.X * b.X + a.Y * b.Y;
        Point2 Leave((LayoutPath Path, bool AtStart) e) => Dir(node.Position, At(e.Path, e.AtStart, Math.Min(h, e.Path.Length)));
        var (path, atStart) = node.Ends[i];
        double len = path.Length;
        var own = Leave(node.Ends[i]);
        var others = node.Ends.Where((e, j) => j != i && Dot(own, Leave(e)) > -0.9).SelectMany(e => e.Path.Segments).ToList();
        for (int k = 0; k * h < len; k++)
        {
            var pt = At(path, atStart, k * h);
            var t = Dir(pt, At(path, atStart, Math.Min((k + 1) * h, len)));
            if (others.All(sg =>
                {
                    double d = sg.DistanceAlong(pt);
                    var lane = Dir(sg.PointAt(d - h), sg.PointAt(d + h)); // PointAt clamps to the segment
                    return pt.DistanceTo(sg.PointAt(d)) >= half + p.SafetyGap + half * Math.Abs(Dot(t, lane)) - 1e-9;
                }))
                return k * h;
        }
        return len + half; // never clears within the arm (a short arm, lanes side by side): all of it
    }

    /// <summary>AGVBLOCKNUM: new names B001, B002, ... for every block, old names ignored, in order of the rect centre's distance from the drawing
    /// origin (0,0); equal distances go lower y first, then lower x. Distance needs no row tolerance, so the order never flips on a small nudge.</summary>
    public static List<(LayoutBlock Block, string Name)> Renumber(IEnumerable<LayoutBlock> blocks)
    {
        static Point2 C(LayoutBlock b) => new((b.Rect.Min.X + b.Rect.Max.X) / 2, (b.Rect.Min.Y + b.Rect.Max.Y) / 2);
        var names = new List<string>();
        return blocks.OrderBy(b => C(b).DistanceTo(default)).ThenBy(b => C(b).Y).ThenBy(b => C(b).X)
            .Select(b => { var n = AutoNamer.Next("B", names); names.Add(n); return (b, n); }).ToList();
    }

    public static LayoutBlock Merge(IEnumerable<LayoutBlock> blocks, string name)
    {
        var list = blocks.ToList();
        var r = new Rect2(
            new Point2(list.Min(b => b.Rect.Min.X), list.Min(b => b.Rect.Min.Y)),
            new Point2(list.Max(b => b.Rect.Max.X), list.Max(b => b.Rect.Max.Y)));
        return new LayoutBlock { Name = name, Cap = 1, Rect = r, Id = Guid.NewGuid().ToString("N") };
    }

    /// <summary>One block per pair of adjacent traffic CPs on the same path (ordered by distance along it).</summary>
    public static List<LayoutBlock> GenerateCorridorBlocks(Layout.Layout l, TrafficParams p)
    {
        double expand = p.VehicleWidth / 2 + p.SafetyGap;
        var names = l.Blocks.Select(b => b.Name).ToList();
        var res = new List<LayoutBlock>();
        var onPath = l.Cps.Where(c => c.Type == "traffic")
            .Select(c => (Cp: c, Pr: LayoutChecker.Project(l, c.Position)))
            .Where(x => x.Pr != null)
            .GroupBy(x => x.Pr!.Value.Path);
        foreach (var grp in onPath)
        {
            var ord = grp.OrderBy(x => x.Pr!.Value.Dist).ToList();
            for (int i = 0; i + 1 < ord.Count; i++)
            {
                double d0 = ord[i].Pr!.Value.Dist, d1 = ord[i + 1].Pr!.Value.Dist;
                var pts = new List<Point2> { ord[i].Cp.Position, ord[i + 1].Cp.Position };
                double off = 0;
                foreach (var s in grp.Key.Segments)
                {
                    off += s.Length;
                    if (off > d0 && off < d1) pts.Add(s.End);
                }
                var name = AutoNamer.Next("B", names);
                names.Add(name);
                res.Add(new LayoutBlock { Name = name, Cap = 1, Rect = Bbox(pts).Expand(expand), Id = Guid.NewGuid().ToString("N") });
            }
        }
        return res;
    }

    // Segment boundaries passed within reach, plus the final point, walking away from the node.
    private static IEnumerable<Point2> Walk(LayoutPath path, bool atStart, double reach)
    {
        double len = path.Length, end = Math.Min(reach, len), off = 0;
        foreach (var s in path.Segments)
        {
            double a = off, b = off + s.Length; // segment spans a..b from path start
            off = b;
            if (atStart && b < end) yield return s.End;
            if (!atStart && len - a < end && a > 0) yield return s.Start;
        }
        yield return path.PointAt(atStart ? end : len - end);
    }

    private static Rect2 Bbox(IEnumerable<Point2> pts)
    {
        var a = pts.ToList();
        return new Rect2(new Point2(a.Min(q => q.X), a.Min(q => q.Y)), new Point2(a.Max(q => q.X), a.Max(q => q.Y)));
    }

    private static List<List<string>> OverlapGroups(List<LayoutBlock> bs, double minGap)
    {
        var parent = Enumerable.Range(0, bs.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        for (int i = 0; i < bs.Count; i++)
            for (int j = i + 1; j < bs.Count; j++)
                if (bs[i].Rect.Expand(minGap / 2).Intersects(bs[j].Rect.Expand(minGap / 2))) parent[Find(i)] = Find(j);
        return Enumerable.Range(0, bs.Count).GroupBy(Find).Where(g => g.Count() >= 2)
            .Select(g => g.Select(i => bs[i].Name).OrderBy(n => n, StringComparer.Ordinal).ToList())
            .OrderBy(g => g[0], StringComparer.Ordinal).ToList();
    }
}
