using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using CoreLayout = ASH.Core.Layout.Layout;

namespace ASH.Core.Traffic;

/// <summary>One CP's name change (Cp is the layout CP, its Id the drawing handle).</summary>
public sealed record CpRename(LayoutCp Cp, string OldName, string NewName)
{
    public bool Changed => OldName != NewName;
}

/// <summary>AGVCPRENUM / AGVCPEDIT: CPs in travel order along the directed paths, and the names they get when numbered in that order.</summary>
public static class CpRenumber
{
    /// <summary>
    /// The CPs in travel order. Paths are visited depth first from the start path (if a point is given), then from the paths that begin at a source node
    /// (nothing flows in), then from any path not reached yet in layout order (loops). A CP's key is its path's visit rank, then the distance along the path
    /// in the direction it was entered (a two-way path entered at its end counts backwards). Ties keep the input order; a CP off every path goes last.
    /// </summary>
    public static List<LayoutCp> TravelOrder(CoreLayout layout, IEnumerable<LayoutCp> cps, Point2? start = null)
    {
        var list = cps.ToList();
        var rank = new Dictionary<LayoutPath, (int Rank, bool Forward)>();
        if (layout.Paths.Count > 0) VisitOrder(layout, start, rank);
        var keyed = list.Select((cp, i) =>
        {
            var hit = rank.Count == 0 ? null : LayoutChecker.Project(layout, cp.Position);
            if (hit == null) return (cp, i, r: int.MaxValue, d: 0.0);
            var (r, fwd) = rank[hit.Value.Path];
            return (cp, i, r, d: fwd ? hit.Value.Dist : hit.Value.Path.Length - hit.Value.Dist);
        });
        return keyed.OrderBy(k => k.r).ThenBy(k => k.d).ThenBy(k => k.i).Select(k => k.cp).ToList();
    }

    private static void VisitOrder(CoreLayout layout, Point2? start, Dictionary<LayoutPath, (int, bool)> rank)
    {
        var graph = PathGraph.Build(layout);
        var nodeAt = new Dictionary<(LayoutPath, bool), PathNode>();
        foreach (var n in graph.Nodes) foreach (var (p, atStart) in n.Ends) nodeAt[(p, atStart)] = n;

        void Walk(LayoutPath first, bool forward)
        {
            var stack = new Stack<(LayoutPath Path, bool Forward)>();
            stack.Push((first, forward));
            while (stack.Count > 0)
            {
                var (path, fwd) = stack.Pop();
                if (rank.ContainsKey(path)) continue;
                rank[path] = (rank.Count, fwd);
                if (!nodeAt.TryGetValue((path, !fwd), out var exit)) continue; // leaving at the end when entered at the start
                var next = exit.Ends.Where(e => e.Path != path && (e.AtStart || e.Path.IsTwoWay)).ToList();
                for (int i = next.Count - 1; i >= 0; i--) stack.Push((next[i].Path, next[i].AtStart)); // first end pops first
            }
        }

        if (start != null)
        {
            var hit = LayoutChecker.Project(layout, start.Value);
            if (hit != null) Walk(hit.Value.Path, true);
        }
        foreach (var n in graph.Nodes.Where(n => n.InCount == 0))
            foreach (var (p, atStart) in n.Ends.Where(e => e.AtStart || e.Path.IsTwoWay)) Walk(p, atStart);
        foreach (var p in layout.Paths.Where(p => p.Segments.Count > 0)) Walk(p, true);
    }

    /// <summary>
    /// New names for the CPs of the given types (null or empty = all), numbered per type in travel order with the type's prefix (L001, L002 ...).
    /// A name another CP keeps is skipped, so no two CPs end up with the same name. Returns every CP of those types, changed or not, in travel order.
    /// </summary>
    public static List<CpRename> Plan(CoreLayout layout, ICollection<string>? types, Point2? start = null)
    {
        bool all = types == null || types.Count == 0;
        bool Selected(LayoutCp c) => all || types!.Contains(c.Type, StringComparer.OrdinalIgnoreCase);
        var reserved = new HashSet<string>(layout.Cps.Where(c => !Selected(c)).Select(c => c.Name));
        var res = new List<CpRename>();
        var typeOrder = CpTypes.All.Concat(layout.Cps.Select(c => c.Type).Where(t => Array.IndexOf(CpTypes.All, t) < 0).Distinct());
        foreach (var type in typeOrder)
        {
            var group = layout.Cps.Where(c => Selected(c) && string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase)).ToList();
            if (group.Count == 0) continue;
            var prefix = AutoNamer.PrefixFor(type);
            int n = 0;
            foreach (var cp in TravelOrder(layout, group, start))
            {
                string name;
                do name = prefix + (++n).ToString("D3"); while (reserved.Contains(name));
                reserved.Add(name);
                res.Add(new CpRename(cp, cp.Name, name));
            }
        }
        return res;
    }

    /// <summary>Old and new name (and type, group) per changed CP as tab-separated lines with a header, ready to paste into Excel.</summary>
    public static string Tsv(IEnumerable<CpRename> renames)
    {
        var sb = new StringBuilder("이전 이름\t새 이름\t종류\t그룹\n");
        foreach (var r in renames.Where(r => r.Changed)) sb.Append(r.OldName).Append('\t').Append(r.NewName).Append('\t').Append(r.Cp.Type).Append('\t').Append(r.Cp.Group).Append('\n');
        return sb.ToString();
    }

    /// <summary>The paths joined to <paramref name="from"/> through shared end points, directly or through others (direction ignored): where a CP may slide to.</summary>
    public static List<LayoutPath> ConnectedPaths(CoreLayout layout, LayoutPath from)
    {
        var graph = PathGraph.Build(layout);
        var byPath = new Dictionary<LayoutPath, List<PathNode>>();
        foreach (var n in graph.Nodes) foreach (var (p, _) in n.Ends) { if (!byPath.TryGetValue(p, out var l)) byPath[p] = l = new List<PathNode>(); l.Add(n); }
        var seen = new HashSet<LayoutPath> { from };
        var todo = new Queue<LayoutPath>();
        todo.Enqueue(from);
        while (todo.Count > 0)
            if (byPath.TryGetValue(todo.Dequeue(), out var nodes))
                foreach (var n in nodes) foreach (var (p, _) in n.Ends) if (seen.Add(p)) todo.Enqueue(p);
        return layout.Paths.Where(seen.Contains).ToList();
    }
}
