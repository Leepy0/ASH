using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Core.Cause;

/// <summary>Routes far longer than their undirected distance (broken links, wrong directions).</summary>
public static class DetourCauses
{
    const double MinRatio = 1.8, MinExtra = 30, SevereRatio = 3; // directed / undirected, metres more than undirected
    const double GapReach = 0.5;                                  // path ends this close but not joined count as a broken joint

    public static IEnumerable<CauseItem> Find(CauseContext c) =>
        ByMaterial(c).GroupBy(x => x.Item.Where).Select(g => g.First().Item);

    /// <summary>Every flagged leg with the material whose route uses it (a leg shared by materials appears once per material).</summary>
    internal static IReadOnlyList<(string Material, CauseItem Item)> ByMaterial(CauseContext c)
    {
        var res = new List<(string, CauseItem)>();
        if (c.Layout.Paths.Count == 0) return res;
        var net = new Net(c.Layout);
        foreach (var m in c.Input.Materials)
        {
            var r = c.Input.Routes.FirstOrDefault(x => x.Material == m.Material);
            if (r == null) continue;
            // the legs the model drives: standby -> pickup -> [buffer] -> unload -> [empty return pickup -> return drop] -> standby
            var stops = new List<string> { r.SupplyFromCP };
            if (!string.IsNullOrEmpty(r.BufferCP)) stops.Add(r.BufferCP!);
            stops.Add(r.SupplyToCP);
            if (m.EmptyReturn)
            {
                if (!string.IsNullOrEmpty(r.ReturnFromCP)) stops.Add(r.ReturnFromCP!);
                if (!string.IsNullOrEmpty(r.ReturnToCP)) stops.Add(r.ReturnToCP!);
            }
            var legs = new List<(string, string)>();
            for (int i = 1; i < stops.Count; i++) legs.Add((stops[i - 1], stops[i]));
            foreach (var s in c.Input.Groups.FirstOrDefault(x => x.Group == m.Group)?.StandbyCPs ?? new List<string>())
            {
                legs.Add((s, stops[0]));
                legs.Add((stops[stops.Count - 1], s));
            }
            foreach (var (a, b) in legs.Distinct())
                if (net.Check(a, b) is { } leg && Item(m.Material, a, b, leg) is { } item) res.Add((m.Material, item));
        }
        return res;
    }

    static CauseItem? Item(string material, string a, string b, Leg leg)
    {
        double und = leg.Undirected;
        bool lost = double.IsPositiveInfinity(leg.Directed);
        if (!lost && !(leg.Directed >= MinRatio * und && leg.Directed - und >= MinExtra)) return null;
        string Fmt(double d) => d.ToString("0", CultureInfo.InvariantCulture);
        var first = leg.Suspects.FirstOrDefault();
        return new CauseItem(CauseKinds.Detour, lost || leg.Directed >= SevereRatio * und ? 2 : 1,
            lost ? $"{material} 도달 불가" : $"{material} 우회 {Fmt(leg.Directed)} m", $"{a}→{b}",
            string.Join(" · ", new[] { lost ? "실제 도달 불가" : $"실제 {Fmt(leg.Directed)} m", $"방향 무시 {Fmt(und)} m" }.Concat(leg.Suspects.Select(s => s.Text))),
            string.Join(", ", leg.Suspects.Select(s => s.Fix)), null, first?.X, first?.Y);
    }

    sealed record Suspect(string Text, string Fix, double X, double Y);
    sealed record Leg(double Directed, double Undirected, List<Suspect> Suspects);
    sealed record Cp(LayoutPath Path, double Dist);

    // path graph of one layout: a node per path end (PathGraph merges ends within tolerance); directed edges follow the drawing order,
    // two-way paths both ways; the undirected search also uses every path backwards and bridges near-miss ends (Path null)
    sealed class Net
    {
        readonly Layout.Layout l;
        readonly List<PathNode> nodes;
        readonly Dictionary<(LayoutPath, bool), int> nodeOf = new();
        readonly List<(int To, LayoutPath? Path, bool Fwd, double Len, Point2 Mid)>[] dir, und;
        readonly Dictionary<string, Cp?> cps = new();
        readonly Dictionary<(string, bool), (double[] D, (int From, int Edge)[] P)> runs = new();

        public Net(Layout.Layout layout)
        {
            l = layout;
            nodes = PathGraph.Build(l).Nodes.ToList();
            for (int i = 0; i < nodes.Count; i++) foreach (var e in nodes[i].Ends) nodeOf[(e.Path, e.AtStart)] = i;
            dir = new List<(int, LayoutPath?, bool, double, Point2)>[nodes.Count];
            und = new List<(int, LayoutPath?, bool, double, Point2)>[nodes.Count];
            for (int i = 0; i < nodes.Count; i++) { dir[i] = new(); und[i] = new(); }
            foreach (var p in l.Paths.Where(p => p.Segments.Count > 0))
            {
                int a = nodeOf[(p, true)], b = nodeOf[(p, false)];
                double len = p.Length; var mid = p.PointAt(len / 2);
                dir[a].Add((b, p, true, len, mid)); und[a].Add((b, p, true, len, mid));
                if (p.IsTwoWay) dir[b].Add((a, p, false, len, mid));
                und[b].Add((a, p, false, len, mid));
            }
            // nodes are ordered by X, so the scan stops at the first one too far east
            for (int i = 0; i < nodes.Count; i++)
                for (int j = i + 1; j < nodes.Count && nodes[j].Position.X - nodes[i].Position.X <= GapReach; j++)
                {
                    double d = nodes[i].Position.DistanceTo(nodes[j].Position);
                    if (d > GapReach) continue;
                    var mid = new Point2((nodes[i].Position.X + nodes[j].Position.X) / 2, (nodes[i].Position.Y + nodes[j].Position.Y) / 2);
                    und[i].Add((j, null, true, d, mid)); und[j].Add((i, null, true, d, mid));
                }
        }

        Cp? CpOf(string name)
        {
            if (!cps.TryGetValue(name, out var cp))
            {
                var pos = l.Cps.FirstOrDefault(x => x.Name == name)?.Position;
                cps[name] = cp = pos != null && LayoutChecker.Project(l, pos.Value) is { } pr ? new Cp(pr.Path, pr.Dist) : null;
            }
            return cp;
        }

        // shortest distance from the CP to every node; the CP leaves its path forward, backward only when that is allowed
        (double[] D, (int From, int Edge)[] P) Run(string name, Cp cp, bool directed)
        {
            if (runs.TryGetValue((name, directed), out var done)) return done;
            var d = Enumerable.Repeat(double.PositiveInfinity, nodes.Count).ToArray();
            var prev = Enumerable.Repeat((-1, -1), nodes.Count).ToArray();
            var q = new SortedSet<(double, int)>();
            void Seed(int n, double v, int edge) { if (v < d[n]) { d[n] = v; prev[n] = (-1, edge); q.Add((v, n)); } }
            Seed(nodeOf[(cp.Path, false)], cp.Path.Length - cp.Dist, -1);
            if (!directed || cp.Path.IsTwoWay) Seed(nodeOf[(cp.Path, true)], cp.Dist, -2); // -2: left backward along the CP's own path
            while (q.Count > 0)
            {
                var (dv, u) = q.Min; q.Remove(q.Min);
                if (dv > d[u]) continue;
                var edges = directed ? dir[u] : und[u];
                for (int k = 0; k < edges.Count; k++)
                {
                    double nd = dv + edges[k].Len;
                    if (nd < d[edges[k].To]) { d[edges[k].To] = nd; prev[edges[k].To] = (u, k); q.Add((nd, edges[k].To)); }
                }
            }
            return runs[(name, directed)] = (d, prev);
        }

        // distance to the CP b: arrive forward over its path's start node, backward over the end node, or straight along a shared path (via = -1)
        double To(string from, Cp a, Cp b, bool directed, out int via, out bool back)
        {
            var d = Run(from, a, directed).D;
            double fwd = d[nodeOf[(b.Path, true)]] + b.Dist;
            double bwd = directed && !b.Path.IsTwoWay ? double.PositiveInfinity : d[nodeOf[(b.Path, false)]] + b.Path.Length - b.Dist;
            double best = Math.Min(fwd, bwd);
            via = 0; back = bwd < fwd;
            if (a.Path == b.Path && (b.Dist >= a.Dist || !directed || a.Path.IsTwoWay))
            {
                double s = Math.Abs(b.Dist - a.Dist);
                if (s <= best) { via = -1; best = s; }
            }
            return best;
        }

        public Leg? Check(string from, string to)
        {
            if (from == to || CpOf(from) is not { } a || CpOf(to) is not { } b) return null;
            double dd = To(from, a, b, true, out _, out _), du = To(from, a, b, false, out int via, out bool back);
            if (double.IsPositiveInfinity(du) || du <= 0) return null;
            var sus = new List<Suspect>();
            if (via == -1)
            {
                if (b.Dist < a.Dist && !a.Path.IsTwoWay) sus.Add(Against(a.Path)); // straight back along its own one-way path
            }
            else
            {
                // walk the undirected route back from the target: paths run against their direction, bridged gaps
                var prev = Run(from, a, false).P;
                int n = nodeOf[(b.Path, !back)];
                if (back && !b.Path.IsTwoWay) sus.Add(Against(b.Path));
                for (int guard = nodes.Count; guard-- > 0;)
                {
                    var (u, k) = prev[n];
                    if (u < 0) { if (k == -2 && !a.Path.IsTwoWay) sus.Add(Against(a.Path)); break; }
                    var e = und[u][k];
                    if (e.Path == null) sus.Add(Gap(u, n, e.Mid));
                    else if (!e.Fwd && !e.Path.IsTwoWay) sus.Add(Against(e.Path));
                    n = u;
                }
                sus.Reverse();
            }
            return new Leg(dd, du, sus);
        }

        static Suspect Against(LayoutPath p)
        {
            var m = p.PointAt(p.Length / 2);
            return new Suspect($"{p.Name} 방향 반대 의심", $"{p.Name} 방향 확인", m.X, m.Y);
        }

        Suspect Gap(int u, int v, Point2 mid)
        {
            string name = $"{nodes[u].Ends[0].Path.Name}·{nodes[v].Ends[0].Path.Name}";
            return new Suspect($"{name} 끊김 의심", $"{name} 연결 확인", mid.X, mid.Y);
        }
    }
}
