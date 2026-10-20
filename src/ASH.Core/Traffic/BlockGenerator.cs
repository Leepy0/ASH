using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Core.Traffic;

public sealed record BlockProposal(List<LayoutBlock> Blocks, List<List<string>> OverlapGroups, List<string> Warnings);

public static class BlockGenerator
{
    public static BlockProposal Generate(Layout.Layout l, TrafficParams p)
    {
        double expand = p.VehicleWidth / 2 + p.SafetyGap;
        var names = l.Blocks.Select(b => b.Name).ToList();
        var blocks = new List<LayoutBlock>();
        foreach (var node in PathGraph.Build(l).Junctions)
        {
            if (l.Blocks.Any(b => b.Rect.Contains(node.Position))) continue;
            var pts = new List<Point2> { node.Position };
            foreach (var (path, atStart) in node.Ends) pts.AddRange(Walk(path, atStart, p.EffectiveBlockReach));
            var name = AutoNamer.Next("B", names);
            names.Add(name);
            blocks.Add(new LayoutBlock { Name = name, Cap = 1, Rect = Bbox(pts).Expand(expand), Id = Guid.NewGuid().ToString("N") });
        }
        return new BlockProposal(blocks, OverlapGroups(blocks), new List<string>());
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

    private static List<List<string>> OverlapGroups(List<LayoutBlock> bs)
    {
        var parent = Enumerable.Range(0, bs.Count).ToArray();
        int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
        for (int i = 0; i < bs.Count; i++)
            for (int j = i + 1; j < bs.Count; j++)
                if (bs[i].Rect.Intersects(bs[j].Rect)) parent[Find(i)] = Find(j);
        return Enumerable.Range(0, bs.Count).GroupBy(Find).Where(g => g.Count() >= 2)
            .Select(g => g.Select(i => bs[i].Name).OrderBy(n => n, StringComparer.Ordinal).ToList())
            .OrderBy(g => g[0], StringComparer.Ordinal).ToList();
    }
}
