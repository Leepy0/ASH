using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Traffic;

namespace ASH.Core.Runner;

/// <summary>Groups whose routes share a path or a traffic block must be judged together (spec 6.7).</summary>
public sealed class GroupClusters
{
    public IReadOnlyList<IReadOnlyList<string>> Clusters { get; init; } = new List<IReadOnlyList<string>>();
    public IReadOnlyDictionary<string, HashSet<string>> UsedPaths { get; private set; } = new Dictionary<string, HashSet<string>>();
    public IReadOnlyDictionary<string, HashSet<string>> UsedBlocks { get; private set; } = new Dictionary<string, HashSet<string>>();
    public List<string> Warnings { get; } = new();

    /// <summary>A cluster per group, no sharing assumed (for callers without a layout).</summary>
    public static GroupClusters Singletons(ProjectInput input) => new GroupClusters
    {
        Clusters = input.Groups.Select(g => (IReadOnlyList<string>)new List<string> { g.Group }).ToList()
    };

    public static GroupClusters Build(Layout.Layout layout, ProjectInput input, TrafficTable traffic)
    {
        var graph = PathGraph.Build(layout);
        var nodeOf = new Dictionary<(LayoutPath, bool), int>();
        for (int i = 0; i < graph.Nodes.Count; i++)
            foreach (var e in graph.Nodes[i].Ends) nodeOf[(e.Path, e.AtStart)] = i;
        var cpPath = new Dictionary<string, LayoutPath>();
        foreach (var c in layout.Cps)
            if (LayoutChecker.Project(layout, c.Position) is { } pr) cpPath[c.Name] = pr.Path;

        var warnings = new List<string>();
        var paths = new Dictionary<string, HashSet<string>>();
        var blocks = new Dictionary<string, HashSet<string>>();
        foreach (var g in input.Groups)
        {
            var used = new HashSet<string>();
            var legs = new List<(string, string)>();
            foreach (var m in input.Materials.Where(x => x.Group == g.Group))
                foreach (var rt in input.Routes.Where(x => x.Material == m.Material))
                {
                    var cps = new[] { rt.SupplyFromCP, rt.BufferCP, rt.SupplyToCP, rt.ReturnFromCP, rt.ReturnToCP }
                        .Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
                    for (int i = 0; i + 1 < cps.Count; i++) legs.Add((cps[i], cps[i + 1]));
                    foreach (var sb in g.StandbyCPs) legs.Add((sb, rt.SupplyFromCP));
                }
            foreach (var (a, b) in legs.Distinct())
            {
                if (!cpPath.TryGetValue(a, out var pa) || !cpPath.TryGetValue(b, out var pb))
                { warnings.Add($"{g.Group}: CP {(cpPath.ContainsKey(a) ? b : a)} not on layout"); continue; }
                var leg = Route(graph, nodeOf, pa, pb);
                if (leg == null) { warnings.Add($"{g.Group}: no route {a} -> {b}"); continue; }
                foreach (var p in leg) used.Add(p.Id);
            }
            paths[g.Group] = used;
            var names = new HashSet<string>(layout.Paths.Where(p => used.Contains(p.Id)).Select(p => p.Name));
            blocks[g.Group] = new HashSet<string>(traffic.Entries.Where(e => names.Contains(e.PathName)).Select(e => e.BlockName));
        }

        var gs = input.Groups.Select(g => g.Group).ToList();
        var parent = gs.ToDictionary(x => x, x => x);
        string Find(string x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        for (int i = 0; i < gs.Count; i++)
            for (int j = i + 1; j < gs.Count; j++)
                if (paths[gs[i]].Overlaps(paths[gs[j]]) || blocks[gs[i]].Overlaps(blocks[gs[j]]))
                    parent[Find(gs[i])] = Find(gs[j]);
        var r = new GroupClusters { Clusters = gs.GroupBy(Find).Select(x => (IReadOnlyList<string>)x.ToList()).ToList() };
        r.UsedPaths = paths; r.UsedBlocks = blocks; r.Warnings.AddRange(warnings);
        return r;
    }

    // shortcut: O(V^2) Dijkstra, fine for hundreds of path ends. Same-path legs assume the target is ahead (no loop needed).
    static List<LayoutPath>? Route(PathGraph graph, Dictionary<(LayoutPath, bool), int> nodeOf, LayoutPath pa, LayoutPath pb)
    {
        var res = new List<LayoutPath> { pa };
        if (pa == pb) return res;
        res.Add(pb);
        int src = nodeOf[(pa, false)], dst = nodeOf[(pb, true)], n = graph.Nodes.Count;
        var dist = Enumerable.Repeat(double.PositiveInfinity, n).ToArray();
        var prev = new (int Node, LayoutPath? Path)[n];
        var done = new bool[n];
        dist[src] = 0;
        while (true)
        {
            int u = -1;
            for (int i = 0; i < n; i++) if (!done[i] && !double.IsInfinity(dist[i]) && (u < 0 || dist[i] < dist[u])) u = i;
            if (u < 0 || u == dst) break;
            done[u] = true;
            foreach (var (p, _) in graph.Nodes[u].Ends.Where(e => e.AtStart))
            {
                int v = nodeOf[(p, false)];
                if (dist[u] + p.Length < dist[v]) { dist[v] = dist[u] + p.Length; prev[v] = (u, p); }
            }
        }
        if (double.IsInfinity(dist[dst])) return null;
        for (int v = dst; v != src; v = prev[v].Node) res.Add(prev[v].Path!);
        return res;
    }
}
