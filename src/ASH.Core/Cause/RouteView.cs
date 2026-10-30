using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;

namespace ASH.Core.Cause;

public enum RouteLegKind { Supply, Return, Standby }

/// <summary>A CP of the route. Label = visit numbers along the supply/return chain ("1", "3,5"), empty for standby and finish CPs. Position null = no such CP in the drawing.</summary>
public sealed record RouteStop(string Name, string Role, string Label, Point2? Position);

/// <summary>One leg of the route with the directed shortest way over the paths. Distance and Points are set when it can be driven; otherwise Problem says why ("L003→U001 도달 불가").</summary>
public sealed record RouteLeg(string From, string To, RouteLegKind Kind, double? Distance, IReadOnlyList<Point2> Points, string? Problem);

public sealed record MaterialRoute(string Material, string Group, IReadOnlyList<RouteStop> Stops, IReadOnlyList<RouteLeg> Legs)
{
    /// <summary>One trip's distance from each load CP (the load CPs take turns, so their mean is the typical trip): that CP's own first leg + the shared supply
    /// legs [+ the return legs]. Distance null when one of its legs cannot be driven.</summary>
    public IReadOnlyList<(string From, double? Distance)> Trips(bool withReturn)
    {
        var loads = Stops.Where(s => s.Role == RouteView.LoadRole).Select(s => s.Name).ToList();
        var legs = Legs.Where(l => l.Kind == RouteLegKind.Supply || (withReturn && l.Kind == RouteLegKind.Return)).ToList();
        double? Sum(IEnumerable<RouteLeg> ls) => ls.Aggregate((double?)0, (a, l) => a + l.Distance);
        var shared = Sum(legs.Where(l => !loads.Contains(l.From)));
        return loads.Select(f => (f, shared + Sum(legs.Where(l => l.From == f)))).ToList();
    }
}

/// <summary>Route view: what one material drives (same legs as the detour check): supply from → [buffer] → supply to → [return from → return to], and standby → first stop / last stop → standby.</summary>
public static class RouteView
{
    const double ArcStepDeg = 10;
    public const string LoadRole = "공급 출발";

    /// <summary>Null when the material or its route row is missing.</summary>
    public static MaterialRoute? Build(Layout.Layout layout, ProjectInput input, string material)
    {
        var m = input.Materials.FirstOrDefault(x => x.Material == material);
        var r = input.Routes.FirstOrDefault(x => x.Material == material);
        if (m == null || r == null) return null;

        var chain = new List<(string Name, string Role)> { (r.SupplyFromCP, LoadRole) };
        if (!string.IsNullOrEmpty(r.BufferCP)) chain.Add((r.BufferCP!, "버퍼"));
        chain.Add((r.SupplyToCP, "공급 도착"));
        int supplyStops = chain.Count;
        if (m.EmptyReturn)
        {
            if (!string.IsNullOrEmpty(r.ReturnFromCP)) chain.Add((r.ReturnFromCP!, "회송 출발"));
            if (!string.IsNullOrEmpty(r.ReturnToCP)) chain.Add((r.ReturnToCP!, "회송 도착"));
        }
        var group = input.Groups.FirstOrDefault(x => x.Group == m.Group);

        // several load CPs take turns: each one starts its own first leg and is reached from standby
        var froms = r.FromCps;
        var legs = new List<(string From, string To, RouteLegKind Kind)>();
        for (int i = 1; i < chain.Count; i++)
            foreach (var from in i == 1 ? froms : new[] { chain[i - 1].Name })
                legs.Add((from, chain[i].Name, i < supplyStops ? RouteLegKind.Supply : RouteLegKind.Return));
        foreach (var s in group?.StandbyCPs ?? new List<string>())
        {
            foreach (var from in froms) legs.Add((s, from, RouteLegKind.Standby));
            legs.Add((chain[chain.Count - 1].Name, s, RouteLegKind.Standby));
        }

        // CPs: the chain numbered in visit order (a CP visited twice carries both numbers), then standby and finish CPs without a number
        var order = new List<string>();
        var role = new Dictionary<string, string>();
        var labels = new Dictionary<string, List<int>>();
        for (int i = 0; i < chain.Count; i++)
            foreach (var (name, rl) in i == 0 ? froms.Select(f => (f, chain[0].Role)) : new[] { chain[i] })
            {
                if (!role.ContainsKey(name)) { role[name] = rl; order.Add(name); labels[name] = new List<int>(); }
                labels[name].Add(i + 1);
            }
        foreach (var (name, rl) in (group?.StandbyCPs ?? new List<string>()).Select(s => (s, "대기"))
                     .Concat(string.IsNullOrEmpty(group?.FinishCP) ? Enumerable.Empty<(string, string)>() : new[] { (group!.FinishCP!, "종료") }))
            if (!role.ContainsKey(name)) { role[name] = rl; order.Add(name); labels[name] = new List<int>(); }
        var pos = layout.Cps.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Position);
        var stops = order.Select(n => new RouteStop(n, role[n], string.Join(",", labels[n]), pos.TryGetValue(n, out var p) ? p : null)).ToList();

        var trace = DetourCauses.Tracer(layout);
        var res = new List<RouteLeg>();
        foreach (var (a, b, kind) in legs.Where(l => l.From != l.To).Distinct())
        {
            var missing = new[] { a, b }.Where(n => !pos.ContainsKey(n)).ToList();
            if (missing.Count > 0) { res.Add(new RouteLeg(a, b, kind, null, Array.Empty<Point2>(), $"{a}→{b} CP 없음 ({string.Join(", ", missing)})")); continue; }
            var t = trace(a, b);
            if (t == null) res.Add(new RouteLeg(a, b, kind, null, Array.Empty<Point2>(), $"{a}→{b} 경로 없음"));
            else if (double.IsPositiveInfinity(t.Value.Dist)) res.Add(new RouteLeg(a, b, kind, null, Array.Empty<Point2>(), $"{a}→{b} 도달 불가"));
            else res.Add(new RouteLeg(a, b, kind, t.Value.Dist, Join(t.Value.Pieces), null));
        }
        return new MaterialRoute(material, m.Group, stops, res);
    }

    // the pieces meet at path ends that PathGraph merged within tolerance, so they chain into one polyline
    static List<Point2> Join(List<(LayoutPath Path, double From, double To)> pieces)
    {
        var pts = new List<Point2>();
        foreach (var (path, from, to) in pieces)
            foreach (var p in Sample(path, from, to))
                if (pts.Count == 0 || pts[pts.Count - 1].DistanceTo(p) > 1e-9) pts.Add(p);
        return pts;
    }

    /// <summary>Points along the path from distance a to b (either order), arcs cut into ~10 degree steps.</summary>
    internal static List<Point2> Sample(LayoutPath path, double a, double b)
    {
        double lo = Math.Min(a, b), hi = Math.Max(a, b), off = 0;
        var pts = new List<Point2>();
        foreach (var s in path.Segments)
        {
            double from = Math.Max(lo, off) - off, to = Math.Min(hi, off + s.Length) - off;
            off += s.Length;
            if (to < from || (to == from && pts.Count > 0)) continue;
            int n = s is ArcSeg arc && s.Length > 0 ? Math.Max(1, (int)Math.Ceiling(Math.Abs(arc.SweepDeg) * (to - from) / s.Length / ArcStepDeg)) : 1;
            for (int i = 0; i <= n; i++)
            {
                var p = s.PointAt(from + (to - from) * i / n);
                if (pts.Count == 0 || pts[pts.Count - 1].DistanceTo(p) > 1e-9) pts.Add(p);
            }
        }
        if (pts.Count == 0) pts.Add(path.PointAt(lo));
        if (a > b) pts.Reverse();
        return pts;
    }
}
