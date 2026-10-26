using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Traffic;

namespace ASH.Core.Cause;

/// <summary>Layout-only: chains of blocks that can lock each other (no waiting room between them).
/// Along every lane (a path in its direction; a two-way path both ways) block B follows block A when a vehicle entering B still holds A: B starts
/// inside A or less than one vehicle plus twice the claim distance after A's end. A cycle of that succession is a ring vehicles can lock up in.</summary>
public static class DeadlockRisk
{
    public static IEnumerable<CauseItem> Find(CauseContext c)
    {
        var l = c.Layout;
        if (l.Blocks.Count < 2) return Array.Empty<CauseItem>();
        var next = Succession(l, c.VehicleLength + 2 * c.ClaimDist);
        var res = new List<CauseItem>();
        foreach (var ring in Rings(next))
        {
            var names = string.Join("·", ring);
            var gaps = ring.SelectMany(a => next[a].Where(kv => ring.Contains(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{a}→{kv.Key} {M(kv.Value)} m"));
            var (x, y) = Centre(l, ring);
            res.Add(new CauseItem(CauseKinds.DeadlockRisk, 1, "교착 위험 " + names, names, string.Join(" · ", gaps), Fix(c, ring), null, x, y, ring.ToList()));
        }
        return res;
    }

    /// <summary>The prescription for blocks that lock (layout rules, first that applies): a stop CP inside or within one vehicle length of one
    /// of them moves out to one vehicle length ("U003 block 밖 1.5 m 이동"), first one where a vehicle of the lock stands holding a block another
    /// waits for (holders: where it stands, that block); blocks with no room between them become one ("B005·B007 병합");
    /// else room to wait before the block waited for ("대기 자리 확보 B005 앞"; waitedFor null = the first block).</summary>
    internal static string Fix(CauseContext c, IReadOnlyList<string> blocks, string? waitedFor = null, IEnumerable<(Point2 At, string Block)>? holders = null)
    {
        foreach (var (at, name) in holders ?? Enumerable.Empty<(Point2, string)>())
        {
            var b = c.Layout.Blocks.FirstOrDefault(x => x.Name == name);
            var cp = c.Layout.Cps.FirstOrDefault(x => x.Type != "traffic" && x.Position.DistanceTo(at) < 0.01);
            if (b == null || cp == null) continue;
            var d = Signed(c.Layout, b.Rect, cp.Position);
            if (d < c.VehicleLength) return $"{cp.Name} block 밖 {M(c.VehicleLength - d)} m 이동";
        }
        var rects = blocks.Select(n => c.Layout.Blocks.FirstOrDefault(b => b.Name == n)).Where(b => b != null).Select(b => b!).ToList();
        if (rects.Count > 0)
        {
            var stop = c.Layout.Cps.Where(cp => cp.Type != "traffic")
                .Select(cp => (Cp: cp, D: rects.Min(b => Signed(c.Layout, b.Rect, cp.Position))))
                .Where(x => x.D < c.VehicleLength).OrderBy(x => x.D).FirstOrDefault();
            if (stop.Cp != null) return $"{stop.Cp.Name} block 밖 {M(c.VehicleLength - stop.D)} m 이동";
            var merge = BlockGenerator.OverlapGroups(rects, c.VehicleLength + 2 * c.ClaimDist).FirstOrDefault();
            if (merge != null) return string.Join("·", merge) + " 병합";
        }
        return $"대기 자리 확보 {waitedFor ?? blocks[0]} 앞";
    }

    /// <summary>Mean of the named blocks' rect centres (metres); nulls when none is in the layout.</summary>
    internal static (double? X, double? Y) Centre(Layout.Layout l, IEnumerable<string> blocks)
    {
        var cs = blocks.Select(n => l.Blocks.FirstOrDefault(b => b.Name == n)).Where(b => b != null)
            .Select(b => new Point2((b!.Rect.Min.X + b.Rect.Max.X) / 2, (b.Rect.Min.Y + b.Rect.Max.Y) / 2)).ToList();
        return cs.Count == 0 ? (null, null) : (cs.Average(p => p.X), cs.Average(p => p.Y));
    }

    static string M(double v) => Math.Round(Math.Max(0, v), 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture);

    // distance from the rect, negative inside: the way out along the CP's path, either direction (shortcut: a path end counts as out,
    // upgrade to a walk across joints if CPs sit right at junctions); off any path, the depth to the nearest edge
    static double Signed(Layout.Layout l, Rect2 r, Point2 p)
    {
        double dx = Math.Max(Math.Max(r.Min.X - p.X, p.X - r.Max.X), 0), dy = Math.Max(Math.Max(r.Min.Y - p.Y, p.Y - r.Max.Y), 0);
        if (dx > 0 || dy > 0) return Math.Sqrt(dx * dx + dy * dy);
        var pr = LayoutChecker.Project(l, p);
        if (pr == null) return -Math.Min(Math.Min(p.X - r.Min.X, r.Max.X - p.X), Math.Min(p.Y - r.Min.Y, r.Max.Y - p.Y));
        var (path, d, _) = pr.Value;
        double Out(int dir)
        {
            double s = 0;
            while (d + dir * s >= 0 && d + dir * s <= path.Length && r.Contains(path.PointAt(d + dir * s))) s += 0.01;
            return Math.Max(0, s - 0.005); // the edge lies within the last step
        }
        return -Math.Min(Out(1), Out(-1));
    }

    sealed record Lane(LayoutPath Path, bool Fwd)
    {
        public double Length => Path.Length;
        public Point2 At(double s) => Path.PointAt(Fwd ? s : Path.Length - s);
    }

    /// <summary>Block -> blocks that follow it with no room between (smallest gap along any lane, metres; 0 = entered while still inside).</summary>
    internal static Dictionary<string, Dictionary<string, double>> Succession(Layout.Layout l, double need)
    {
        var lanes = l.Paths.Where(p => p.Segments.Count > 0 && p.Length > 0)
            .SelectMany(p => p.IsTwoWay ? new[] { new Lane(p, true), new Lane(p, false) } : new[] { new Lane(p, true) }).ToList();
        // block intervals along each lane by sampling (shortcut: 5 cm steps, a block thinner than that can be missed; fine for traffic blocks)
        var spans = new Dictionary<Lane, List<(double In, double Out, string B)>>();
        foreach (var ln in lanes)
        {
            var list = new List<(double In, double Out, string B)>();
            int n = Math.Max(2, (int)Math.Ceiling(ln.Length / 0.05));
            foreach (var b in l.Blocks)
            {
                double? start = null;
                for (int i = 0; i <= n; i++)
                {
                    double s = ln.Length * i / n;
                    bool inside = b.Rect.Contains(ln.At(s));
                    if (inside && start == null) start = s;
                    if (!inside && start != null) { list.Add((start.Value, ln.Length * (i - 1) / n, b.Name)); start = null; }
                }
                if (start != null) list.Add((start.Value, ln.Length, b.Name));
            }
            spans[ln] = list.OrderBy(x => x.In).ToList();
        }
        // lanes leaving a lane's end node (no U-turn onto the other direction of the same path)
        var nodeOf = new Dictionary<(LayoutPath, bool), PathNode>();
        foreach (var nd in PathGraph.Build(l).Nodes) foreach (var e in nd.Ends) nodeOf[e] = nd;
        List<Lane> After(Lane ln) => nodeOf.TryGetValue((ln.Path, !ln.Fwd), out var nd)
            ? nd.Ends.Where(e => e.AtStart || e.Path.IsTwoWay).Select(e => new Lane(e.Path, e.AtStart)).Where(x => x.Path != ln.Path || x.Fwd == ln.Fwd).ToList()
            : new List<Lane>();

        var next = l.Blocks.GroupBy(b => b.Name).ToDictionary(g => g.Key, _ => new Dictionary<string, double>());
        void Edge(string a, string b, double gap)
        {
            if (a == b) return;
            if (!next[a].TryGetValue(b, out var g) || gap < g) next[a][b] = gap;
        }
        // blocks entered within budget after leaving `from` (walked = metres since its end); `from` running on into this lane ends the walk
        void Walk(Lane ln, string from, double walked, double budget, int depth)
        {
            if (depth > 20) return;
            foreach (var sp in spans[ln])
            {
                if (sp.In > budget) break;
                if (sp.B == from) { if (sp.In <= 1e-9) return; continue; }
                Edge(from, sp.B, walked + sp.In);
            }
            if (ln.Length < budget) foreach (var nx in After(ln)) Walk(nx, from, walked + ln.Length, budget - ln.Length, depth + 1);
        }
        foreach (var ln in lanes)
            foreach (var a in spans[ln])
            {
                foreach (var b in spans[ln]) if (b.B != a.B && b.In > a.In && b.In <= a.Out + need) Edge(a.B, b.B, Math.Max(0, b.In - a.Out));
                double left = need - (ln.Length - a.Out);
                if (left > 0) foreach (var nx in After(ln)) Walk(nx, a.B, ln.Length - a.Out, left, 0);
            }
        return next;
    }

    /// <summary>Strongly connected sets of two or more blocks (Tarjan), each sorted by name, in name order.</summary>
    internal static List<List<string>> Rings(Dictionary<string, Dictionary<string, double>> next)
    {
        int index = 0;
        var idx = new Dictionary<string, int>(); var low = new Dictionary<string, int>(); var on = new HashSet<string>(); var stack = new Stack<string>();
        var res = new List<List<string>>();
        void Visit(string v)
        {
            idx[v] = low[v] = index++; stack.Push(v); on.Add(v);
            foreach (var w in next[v].Keys)
            {
                if (!idx.ContainsKey(w)) { Visit(w); low[v] = Math.Min(low[v], low[w]); }
                else if (on.Contains(w)) low[v] = Math.Min(low[v], idx[w]);
            }
            if (low[v] != idx[v]) return;
            var set = new List<string>();
            string x;
            do { x = stack.Pop(); on.Remove(x); set.Add(x); } while (x != v);
            if (set.Count >= 2) res.Add(set.OrderBy(s => s, StringComparer.Ordinal).ToList());
        }
        foreach (var v in next.Keys.OrderBy(s => s, StringComparer.Ordinal)) if (!idx.ContainsKey(v)) Visit(v);
        return res.OrderBy(r => r[0], StringComparer.Ordinal).ToList();
    }
}
