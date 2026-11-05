using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ASH.Core.Lite;

/// <summary>The cleaned result of one definition in one orientation (R7). Plan: flattened with the local identity, placed by an Affine2. Tilted: flattened with Linear (the
/// accumulated 3x3 up to the nearest plan ancestor, 6 decimals), placed by a translation. Own, Uses and Box are in the node's coordinates; Own holds layer "0" symbolically
/// (it takes the insert's layer at placement, R8). Memory per node is the definition's size: children stay instances (R18).</summary>
public sealed class LiteNode
{
    public int Def { get; internal set; }
    public bool Tilted { get; internal set; }
    public Affine3 Linear { get; internal set; }
    /// <summary>Largest scale any placement of this node reaches (drawing units per node unit): every threshold is judged at it.</summary>
    public double ScaleMax { get; internal set; }
    /// <summary>Own geometry after B–G, outlines included, in original order (outlines last).</summary>
    public List<LiteEntity> Own { get; internal set; } = new();
    /// <summary>Child instances still placed, in original order.</summary>
    public List<LiteUse> Uses { get; internal set; } = new();
    public LiteBox Box { get; internal set; } = LiteBox.Empty;
    /// <summary>Own per symbolic layer: count, and the size parts Task 6 bounds from above (R15).</summary>
    public Dictionary<string, LiteTally> OwnTally { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, List<int>>? ByLayer;
}

/// <summary>Size parts of some entities. Bytes is what LiteDxfWriter writes for them with an empty layer name, in node coordinates, unshifted; LayerSlots is how often a layer name is
/// written (1 per LINE/CIRCLE/ARC, 2 + vertices per POLYLINE); Coords is how many x or y values are written (each at least "0.0", 3 characters; radii, angles and bulges do not move with
/// the shift and are already in Bytes). Upper bound after placement: Bytes + LayerSlots x longest R12 name + Coords x (widest coordinate - 3).</summary>
public readonly record struct LiteTally(int Count, long Bytes, long LayerSlots, long Coords)
{
    public static LiteTally operator +(LiteTally a, LiteTally b) => new(a.Count + b.Count, a.Bytes + b.Bytes, a.LayerSlots + b.LayerSlots, a.Coords + b.Coords);
    public static LiteTally Of(LiteEntity e)
    {
        var (slots, coords) = e switch
        {
            LiteLine => (1, 4),
            LiteCircle or LiteArc => (1, 2),
            LitePolyline p => (2 + p.Vertices.Count, 2 * p.Vertices.Count),
            _ => throw new ArgumentException("unknown lite entity " + e.GetType().Name),
        };
        return new LiteTally(1, LiteDxfWriter.Bytes(e, "", 0, 0), slots, coords);
    }
}

/// <summary>A kept child instance: Placement maps the child's node coordinates into the parent's; Layer is the insert's ("0" inherits).</summary>
public sealed record LiteUse(LiteNode Child, Affine2 Placement, string Layer);

/// <summary>Everything one set of thresholds produced.</summary>
public sealed class LiteRun
{
    public LiteThresholds Thresholds { get; }
    public LiteNode Root { get; internal set; } = null!;
    /// <summary>Every node, parents before children.</summary>
    public List<LiteNode> Nodes { get; } = new();
    public LiteStats Stats { get; } = new();
    /// <summary>Nodes whose blob step hit the budget: their own geometry was judged alone, their children all stayed.</summary>
    public int Truncated { get; internal set; }
    internal LiteRun(LiteThresholds t) => Thresholds = t;
}

/// <summary>The plan of AGVLITE over block definitions: every (definition, orientation) is cleaned once, leaves first; a parent counts its children's placed geometry in a stream and
/// keeps them as instances; writing expands the instances through their matrices (layer, then definition in first-seen order, then original order). Nothing here reads a name.
/// Task 6 adds: global dedup at writing, per-unit precision (R15), level choice, settings and the report; exclude is the user's layer list from those settings.</summary>
public sealed class LiteBuild
{
    /// <summary>A stack guard only: cycles are cut by path (R9).</summary>
    public const int MaxDepth = 256;
    /// <summary>Chords one definition's blob step may stream from its children before it judges its own geometry alone (a runaway fan-out guard: the root streams the whole drawing by
    /// design, R18, and 2 million entities is an ordinary 300 MB drawing). Settable for tests.</summary>
    public long Budget { get; set; } = 200_000_000;

    readonly LiteBlocks _src;
    readonly HashSet<string> _exclude;
    LiteRun? _last;

    public LiteBuild(LiteBlocks src, IEnumerable<string>? exclude = null)
    {
        _src = src;
        _exclude = new HashSet<string>(exclude ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    public LiteBlocks Source => _src;

    /// <summary>Off, frozen, AGV_ or excluded by the user: its geometry is left out before cleaning (R8).</summary>
    public bool Excluded(string layer) => LiteLayers.IsAsh(layer) || _exclude.Contains(layer) || (_src.Layers.TryGetValue(layer, out var s) && (s.Off || s.Frozen));
    bool Frozen(string layer) => _src.Layers.TryGetValue(layer, out var s) && s.Frozen;

    /// <summary>Runs B–G for these thresholds (the last run is kept, so Estimate then Write at the same thresholds cleans once).</summary>
    public LiteRun Run(LiteThresholds t)
    {
        if (_last != null && _last.Thresholds == t) return _last;
        var run = new LiteRun(t);
        var defs = _src.Defs;
        int n = defs.Count;

        // 1. definitions reachable from the root in topological order; an insert back onto the path, or past MaxDepth, is cut
        var order = new List<int>();
        var state = new byte[n]; // 0 new, 1 on the path, 2 done
        var cut = new HashSet<(int, int)>();
        void Dfs(int d, int depth)
        {
            state[d] = 1;
            var uses = defs[d].Uses;
            for (int i = 0; i < uses.Count; i++)
            {
                int c = uses[i].Def;
                // cut once per definition on the first DFS path (the old per-path Walk could cut another edge of the same loop): differs only on malformed drawings
                if (c < 0 || c >= n || state[c] == 1 || depth >= MaxDepth) { cut.Add((d, i)); continue; }
                if (state[c] == 0) Dfs(c, depth + 1);
            }
            state[d] = 2;
            order.Add(d);
        }
        Dfs(_src.Root, 0);
        order.Reverse();

        // 2. one node per (definition, orientation), with the largest scale reaching it: parents are final before their children are visited
        var nodes = new Dictionary<(int, bool, Affine3), LiteNode>();
        var byDef = new List<LiteNode>[n];
        foreach (var d in order) byDef[d] = new List<LiteNode>();
        LiteNode Node(int def, bool tilted, Affine3 linear, double scale)
        {
            if (!nodes.TryGetValue((def, tilted, linear), out var nd))
            {
                nodes[(def, tilted, linear)] = nd = new LiteNode { Def = def, Tilted = tilted, Linear = linear };
                run.Nodes.Add(nd);
                byDef[def].Add(nd);
            }
            nd.ScaleMax = Math.Max(nd.ScaleMax, scale);
            return nd;
        }
        run.Root = Node(_src.Root, false, Affine3.Identity, 1);
        foreach (var d in order)
            foreach (var nd in byDef[d].ToList())
                for (int i = 0; i < defs[d].Uses.Count; i++)
                    if (!cut.Contains((d, i)) && Child(nd, defs[d].Uses[i]) is (var tilted, var linear, _, var scale))
                        Node(defs[d].Uses[i].Def, tilted, linear, scale);

        // 3. leaves first
        for (int k = order.Count - 1; k >= 0; k--)
            foreach (var nd in byDef[order[k]])
                Process(nd, run, i => !cut.Contains((nd.Def, i)), (def, tilted, linear) => nodes[(def, tilted, linear)]);
        return _last = run;
    }

    /// <summary>The child node a use leads to from a node, its placement into the node's coordinates and the scale it reaches there; null for a zero-scale plan placement.</summary>
    static (bool Tilted, Affine3 Linear, Affine2 Placement, double Scale)? Child(LiteNode parent, BlockUse u)
    {
        var acc = u.Xf.Then(parent.Linear);
        if (acc.PlanHasZ) return (true, Round(acc.Linear), new Affine2(1, 0, 0, 1, acc.M03, acc.M13), parent.ScaleMax);
        var plan = acc.Plan;
        double norm = plan.Norm;
        return norm > 0 ? (false, Affine3.Identity, plan, parent.ScaleMax * norm) : null;
    }

    static Affine3 Round(Affine3 a) => new(R(a.M00), R(a.M01), R(a.M02), 0, R(a.M10), R(a.M11), R(a.M12), 0, R(a.M20), R(a.M21), R(a.M22), 0);
    static double R(double v) => Math.Round(v, 6);

    void Process(LiteNode nd, LiteRun run, Func<int, bool> kept, Func<int, bool, Affine3, LiteNode> node)
    {
        var t = run.Thresholds;
        double unit = t.UnitsPerMm / nd.ScaleMax, tol = t.ToleranceMm * unit;
        var def = _src.Defs[nd.Def];

        var flat = new List<LiteEntity>(def.Geo.Count);
        foreach (var g in def.Geo)
            if (g.Layer == "0" || !Excluded(g.Layer)) flat.Add(Flatten.To2D(g, nd.Linear, tol, g.Layer));
        var own = LitePipeline.Run(flat, t, nd.ScaleMax, run.Stats);

        var uses = new List<LiteUse>();
        for (int i = 0; i < def.Uses.Count; i++)
        {
            var u = def.Uses[i];
            if (!kept(i) || (u.Layer != "0" && Frozen(u.Layer)) || Child(nd, u) is not (var tilted, var linear, var placement, _)) continue;
            var child = node(u.Def, tilted, linear);
            if (child.Own.Count == 0 && child.Uses.Count == 0) continue;
            if (Map(child.Box, placement).Diagonal < t.MinSizeMm * unit) { run.Stats.SmallInstances++; continue; } // R2a, per instance
            uses.Add(new LiteUse(child, placement, u.Layer));
        }

        var streams = uses.Select(u => (Func<IEnumerable<LiteEntity>>)(() => Place(u.Child, u.Placement, u.Layer == "0" ? null : u.Layer, tol, 1))).ToList();
        own = Blobs.Replace(own, streams, out var keptChildren, t, nd.ScaleMax, run.Stats, nd, Budget);
        if (keptChildren == null) run.Truncated++;
        nd.Own = own;
        nd.Uses = keptChildren == null ? uses : keptChildren.Select(i => uses[i]).ToList();

        var box = LiteBox.Of(own);
        foreach (var u in nd.Uses)
        {
            var m = Map(u.Child.Box, u.Placement);
            if (!m.IsEmpty) { box.Add(m.MinX, m.MinY); box.Add(m.MaxX, m.MaxY); }
        }
        nd.Box = box;
        foreach (var e in own) nd.OwnTally[e.Layer] = (nd.OwnTally.TryGetValue(e.Layer, out var x) ? x : default) + LiteTally.Of(e);
    }

    static LiteBox Map(LiteBox b, Affine2 xf)
    {
        if (b.IsEmpty) return b;
        var o = LiteBox.Empty;
        foreach (var (x, y) in new[] { (b.MinX, b.MinY), (b.MaxX, b.MinY), (b.MinX, b.MaxY), (b.MaxX, b.MaxY) })
        {
            var p = xf.Ap(x, y);
            o.Add(p.X, p.Y);
        }
        return o;
    }

    /// <summary>The placed geometry of a node and everything under it, one entity at a time (nothing is collected). inherit is the layer "0" resolves to; null while it is still
    /// unknown (inside a definition), in which case nothing on "0" is hidden yet.</summary>
    IEnumerable<LiteEntity> Place(LiteNode n, Affine2 xf, string? inherit, double tol, int depth)
    {
        if (depth > MaxDepth) yield break;
        foreach (var e in n.Own)
        {
            var layer = e.Layer == "0" ? inherit : e.Layer;
            if (e.Layer == "0" && layer != null && Excluded(layer)) continue;
            yield return CurveApprox.Transform(e, xf, tol, layer ?? "0");
        }
        foreach (var u in n.Uses)
        {
            var layer = u.Layer == "0" ? inherit : u.Layer;
            if (u.Layer == "0" && layer != null && Frozen(layer)) continue; // a frozen parent hides all of it
            foreach (var x in Place(u.Child, u.Placement.Then(xf), layer, tol, depth + 1)) yield return x;
        }
    }

    /// <summary>Every placed instance under the root, depth first in original order, with its world transform and the layer its "0" geometry takes.</summary>
    IEnumerable<(LiteNode Node, Affine2 Xf, string Inherit)> Placements(LiteNode n, Affine2 xf, string inherit, int depth)
    {
        if (depth > MaxDepth) yield break;
        yield return (n, xf, inherit);
        foreach (var u in n.Uses)
        {
            var layer = u.Layer == "0" ? inherit : u.Layer;
            if (u.Layer == "0" && Frozen(layer)) continue;
            foreach (var p in Placements(u.Child, u.Placement.Then(xf), layer, depth + 1)) yield return p;
        }
    }

    /// <summary>Per original layer, everything Write would place: definition tallies x instances (see LiteTally for the upper bound Task 6 makes of it, R15).</summary>
    public Dictionary<string, LiteTally> Estimate(LiteThresholds t)
    {
        var run = Run(t);
        var d = new Dictionary<string, LiteTally>(StringComparer.Ordinal);
        foreach (var (n, _, inherit) in Placements(run.Root, Affine2.Identity, "0", 0))
            foreach (var kv in n.OwnTally)
            {
                var layer = kv.Key == "0" ? inherit : kv.Key;
                if (kv.Key == "0" && Excluded(layer)) continue;
                d[layer] = (d.TryGetValue(layer, out var x) ? x : default) + kv.Value;
            }
        return d;
    }

    public long Total(LiteThresholds t)
    {
        var e = Estimate(t);
        return LiteDxfWriter.OverheadBytes(e.Count) + e.Values.Sum(v => v.Bytes);
    }

    /// <summary>The layers that weigh most.</summary>
    public List<(string Layer, long Bytes)> Biggest(LiteThresholds t, int n = 3) =>
        Estimate(t).Where(kv => kv.Value.Bytes > 0).OrderByDescending(kv => kv.Value.Bytes).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(n).Select(kv => (kv.Key, kv.Value.Bytes)).ToList();

    /// <summary>Writes the drawing as R12 DXF, expanding the instances through their matrices: layer (ordinal), then definition (first-seen index), then original order.
    /// Moved so the lower left sits just above (0, 0) (LitePlan.Shift). Holds one entry per placed instance, never the expanded entities.</summary>
    public LiteResult Write(TextWriter w, LiteThresholds t)
    {
        var run = Run(t);
        double tol = t.ToleranceMm * t.UnitsPerMm;
        var places = Placements(run.Root, Affine2.Identity, "0", 0).ToList();
        var byDef = new List<int>[_src.Defs.Count];
        var layers = new SortedSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < places.Count; i++)
        {
            var (n, _, inherit) = places[i];
            (byDef[n.Def] ??= new List<int>()).Add(i);
            foreach (var l in n.OwnTally.Keys)
                if (l != "0") layers.Add(l);
                else if (!Excluded(inherit)) layers.Add(inherit);
        }

        var box = run.Root.Box;
        double sx = box.IsEmpty ? 0 : LitePlan.Shift(box.MinX, _src.InsUnits), sy = box.IsEmpty ? 0 : LitePlan.Shift(box.MinY, _src.InsUnits);
        var names = LiteLayers.R12Names(_src.Layers.Keys.Concat(layers));
        var colors = _src.Layers.ToDictionary(kv => kv.Key, kv => kv.Value.Color, StringComparer.Ordinal);
        var written = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int count = 0;

        LiteDxfWriter.Begin(w, box, layers, names, colors, sx, sy);
        foreach (var layer in layers)
        {
            string name = LiteDxfWriter.Name(names, layer);
            foreach (var group in byDef)
            {
                if (group == null) continue;
                foreach (int pi in group)
                {
                    var (n, xf, inherit) = places[pi];
                    n.ByLayer ??= n.Own.Select((e, i) => (e.Layer, i)).GroupBy(x => x.Layer, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(x => x.i).ToList(), StringComparer.Ordinal);
                    IEnumerable<int> at;
                    if (inherit == layer && n.ByLayer.ContainsKey("0")) at = Enumerable.Range(0, n.Own.Count).Where(i => n.Own[i].Layer == "0" || n.Own[i].Layer == layer); // both kinds, original order
                    else if (layer != "0" && n.ByLayer.TryGetValue(layer, out var l)) at = l;
                    else continue;
                    foreach (int i in at)
                    {
                        var e = CurveApprox.Transform(n.Own[i], xf, tol, layer);
                        LiteDxfWriter.Entity(w, e, name, sx, sy);
                        written[e.Kind] = (written.TryGetValue(e.Kind, out var c) ? c : 0) + 1;
                        count++;
                    }
                }
            }
        }
        LiteDxfWriter.End(w);
        var ext = box.IsEmpty ? box : new LiteBox { MinX = box.MinX - sx, MinY = box.MinY - sy, MaxX = box.MaxX - sx, MaxY = box.MaxY - sy };
        return new LiteResult(sx, sy, ext, written, count);
    }
}
