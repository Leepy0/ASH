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
    /// <summary>Indices into Own per symbolic layer, in original order.</summary>
    internal Dictionary<string, List<int>>? ByLayer;
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
    /// <summary>The file of this run: what writing needs, made once.</summary>
    internal LiteSheet? Sheet;
    internal LiteRun(LiteThresholds t) => Thresholds = t;
}

/// <summary>One row of the report's blob table: X, Y = centre in original drawing coordinates at the first placement of its node (NaN when the node is no longer placed, its instances
/// went into a parent's blob), DiagMm placed there; Instances = placements of the node; Lines = lines replaced in one instance.</summary>
public sealed record LiteBlobRow(double X, double Y, double DiagMm, double Density, int Lines, int Instances);

/// <summary>What Write and the size measure share, so they cannot differ. Places has one entry per placed instance, never the expanded entities. Layers: original layer to indices into
/// Places, in definition then original order. Box covers everything placed, before the global dedup: a duplicate lies within a grid step of what stays, and this way the shift, the header
/// and so every byte but the duplicates' own are the same in the measure and the file.</summary>
internal sealed class LiteSheet
{
    public List<(LiteNode Node, Affine2 Xf, string Inherit)> Places = null!;
    public readonly SortedDictionary<string, List<int>> Layers = new(StringComparer.Ordinal);
    public LiteBox Box = LiteBox.Empty;
    public double Sx, Sy;
    public int Decimals;
    public Dictionary<string, string> Names = null!;
    public Dictionary<string, short> Colors = null!;
    /// <summary>The measured file, once measured: total, and per original layer.</summary>
    public long Size = -1;
    public Dictionary<string, (int Count, long Bytes)> Tally = null!;

    public void Begin(TextWriter w) => LiteDxfWriter.Begin(w, Box, Layers.Keys, Names, Colors, Sx, Sy, Decimals);
    public void Entity(TextWriter w, LiteEntity e) => LiteDxfWriter.Entity(w, e, LiteDxfWriter.Name(Names, e.Layer), Sx, Sy, Decimals);
    public long Bytes(LiteEntity e) => LiteDxfWriter.Bytes(e, LiteDxfWriter.Name(Names, e.Layer), Sx, Sy, Decimals);
}

/// <summary>The plan of AGVLITE over block definitions: every (definition, orientation) is cleaned once, leaves first; a parent counts its children's placed geometry in a stream and
/// keeps them as instances; writing expands the instances through their matrices (layer, then definition in first-seen order, then original order) and drops what is already written at
/// that place. Sizes are measured by streaming the same entities through the same writer without that dedup (R20), so an estimate is the file plus its duplicates, never less.
/// One object per drawing: a level or bare thresholds select the run, the settings hold what a person fixed. Nothing here reads a name.</summary>
public sealed class LiteBuild
{
    /// <summary>A stack guard only: cycles are cut by path (R9).</summary>
    public const int MaxDepth = 256;
    /// <summary>Chords one definition's blob step may stream from its children before it judges its own geometry alone (a runaway fan-out guard: the root streams the whole drawing by
    /// design, R18, and 2 million entities is an ordinary 300 MB drawing), and the most the density histogram walks. Settable for tests.</summary>
    public long Budget { get; set; } = 200_000_000;

    readonly LiteBlocks _src;
    readonly HashSet<string> _exclude;
    readonly SortedDictionary<int, long> _sizes = new();
    LiteRun? _last;

    public LiteBuild(LiteBlocks src, IEnumerable<string>? exclude = null) : this(src, new LiteSettings { ExcludeLayers = (exclude ?? Array.Empty<string>()).ToList() }) { }

    public LiteBuild(LiteBlocks src, LiteSettings settings)
    {
        _src = src;
        Settings = settings;
        _exclude = new HashSet<string>(settings.ExcludeLayers, StringComparer.Ordinal);
    }

    public LiteBlocks Source => _src;
    public LiteSettings Settings { get; }
    /// <summary>Levels measured so far (Total, ChooseLevel) and their sizes in bytes.</summary>
    public IReadOnlyDictionary<int, long> LevelSizes => _sizes;

    public LiteThresholds Thresholds(int level) => LiteLevels.Get(level, _src.InsUnits, Settings.NoBlobs);

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

    /// <summary>The first level from 0 up whose measured size fits, or the last one. The run of the level returned stays cached for Write.</summary>
    public int ChooseLevel(long targetBytes)
    {
        for (int level = 0; ; level++)
            if (Total(level) <= targetBytes || level == LiteLevels.Max) return level;
    }

    public long Total(int level) => _sizes[level] = Total(Thresholds(level));

    /// <summary>The size of the file with the duplicates still in: an upper bound of what Write writes, and exact when there are none.</summary>
    public long Total(LiteThresholds t) => Measured(Run(t)).Size;

    /// <summary>Per original layer, everything placed, duplicates included: count and bytes as written.</summary>
    public Dictionary<string, (int Count, long Bytes)> Estimate(LiteThresholds t) => Measured(Run(t)).Tally;

    /// <summary>The layers that weigh most.</summary>
    public List<(string Layer, long Bytes)> Biggest(LiteThresholds t, int n = 3) =>
        Estimate(t).Where(kv => kv.Value.Bytes > 0).OrderByDescending(kv => kv.Value.Bytes).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(n).Select(kv => (kv.Key, kv.Value.Bytes)).ToList();

    /// <summary>The blobs of a run that replaced most lines, in drawing terms.</summary>
    public List<LiteBlobRow> TopBlobs(LiteThresholds t)
    {
        var run = Run(t);
        var first = new Dictionary<LiteNode, (Affine2 Xf, int Count)>();
        foreach (var (n, xf, _) in Sheet(run).Places) first[n] = first.TryGetValue(n, out var f) ? (f.Xf, f.Count + 1) : (xf, 1);
        return run.Stats.TopBlobs.Select(b =>
        {
            if (b.Node == null || !first.TryGetValue(b.Node, out var f)) return new LiteBlobRow(double.NaN, double.NaN, double.NaN, b.Density, b.Lines, 0);
            var (cx, cy) = f.Xf.Ap(b.X + b.W / 2, b.Y + b.H / 2);
            var (x0, y0) = f.Xf.Ap(b.X, b.Y);
            var (x1, y1) = f.Xf.Ap(b.X + b.W, b.Y + b.H);
            return new LiteBlobRow(cx, cy, Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) / t.UnitsPerMm, b.Density, b.Lines, f.Count);
        }).ToList();
    }

    /// <summary>Line length per 1 m cell of the raw drawing (R5's grid), hidden layers left out (R8): before any cleaning, streamed, never stored. Runs apart from every level.
    /// Past Budget chords and instances the walk stops and the result says Cut.</summary>
    public LiteStat.Hist Histogram()
    {
        double perMm = Thresholds(0).UnitsPerMm, tol = LiteLevels.Table[0].ToleranceMm * perMm;
        long chords = 0;
        var h = LiteStat.Histogram(Raw(_src.Root, Affine3.Identity, "0", new bool[_src.Defs.Count], 0, tol).TakeWhile(_ => chords++ < Budget)
            .Select(s => (s.Item1 / perMm, s.Item2 / perMm, s.Item3 / perMm, s.Item4 / perMm)), 1000);
        return h with { Cut = chords > Budget };
    }

    /// <summary>The chords of a definition's raw geometry under xf and of everything inserted in it, by the visibility rules of Place. A cycle is cut by the path (R9).</summary>
    IEnumerable<(double, double, double, double)> Raw(int def, Affine3 xf, string inherit, bool[] onPath, int depth, double tol)
    {
        onPath[def] = true;
        yield return default; // an empty chord per visit: a fan-out of empty blocks has to meet the budget too
        foreach (var g in _src.Defs[def].Geo)
        {
            var layer = g.Layer == "0" ? inherit : g.Layer;
            if (Excluded(layer)) continue;
            foreach (var s in LiteStat.Segments(new[] { Flatten.To2D(g, xf, tol, layer) }, tol)) yield return s;
        }
        foreach (var u in _src.Defs[def].Uses)
        {
            var layer = u.Layer == "0" ? inherit : u.Layer;
            if (u.Def < 0 || u.Def >= onPath.Length || onPath[u.Def] || depth >= MaxDepth || Frozen(layer)) continue;
            foreach (var s in Raw(u.Def, u.Xf.Then(xf), layer, onPath, depth + 1, tol)) yield return s;
        }
        onPath[def] = false;
    }

    public LiteResult Write(TextWriter w, int level) => Write(w, Thresholds(level)) with { Level = level };

    /// <summary>Writes the drawing as R12 DXF at the unit's precision, expanding the instances through their matrices: layer (ordinal), then definition (first-seen index), then original
    /// order. An entity whose placed coordinates match one already written, on any layer, is dropped. Moved so the lower left sits just above (0, 0) (LitePlan.Shift).
    /// Holds a 64-bit key per entity, never the entities.</summary>
    public LiteResult Write(TextWriter w, LiteThresholds t)
    {
        var run = Run(t);
        var sheet = Sheet(run);
        double grid = LiteThresholds.GridMm * t.UnitsPerMm;
        var seen = new HashSet<long>();
        var written = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int count = 0, deduped = 0;
        long dedupedBytes = 0;

        sheet.Begin(w);
        foreach (var e in Entities(run, sheet))
        {
            if (!seen.Add(SegKey.Of(e, grid).Hash))
            {
                deduped++;
                dedupedBytes += sheet.Bytes(e);
                continue;
            }
            sheet.Entity(w, e);
            written[e.Kind] = (written.TryGetValue(e.Kind, out var c) ? c : 0) + 1;
            count++;
        }
        LiteDxfWriter.End(w);
        var box = sheet.Box;
        var ext = box.IsEmpty ? box : new LiteBox { MinX = box.MinX - sheet.Sx, MinY = box.MinY - sheet.Sy, MaxX = box.MaxX - sheet.Sx, MaxY = box.MaxY - sheet.Sy };
        return new LiteResult(sheet.Sx, sheet.Sy, ext, written, count, deduped, dedupedBytes);
    }

    /// <summary>The whole file through a counting writer, duplicates included (R20).</summary>
    LiteSheet Measured(LiteRun run)
    {
        var sheet = Sheet(run);
        if (sheet.Size >= 0) return sheet;
        var cw = new LiteDxfWriter.CountingWriter();
        var tally = new Dictionary<string, (int Count, long Bytes)>(StringComparer.Ordinal);
        sheet.Begin(cw);
        foreach (var e in Entities(run, sheet))
        {
            long before = cw.Count;
            sheet.Entity(cw, e);
            tally.TryGetValue(e.Layer, out var x);
            tally[e.Layer] = (x.Count + 1, x.Bytes + cw.Count - before);
        }
        LiteDxfWriter.End(cw);
        sheet.Tally = tally;
        sheet.Size = cw.Count;
        return sheet;
    }

    LiteSheet Sheet(LiteRun run)
    {
        if (run.Sheet != null) return run.Sheet;
        var t = run.Thresholds;
        var sheet = new LiteSheet { Places = Placements(run.Root, Affine2.Identity, "0", 0).ToList(), Decimals = LiteDxfWriter.Precision(t.InsUnits) };
        foreach (int pi in Enumerable.Range(0, sheet.Places.Count).OrderBy(i => sheet.Places[i].Node.Def)) // stable: original order inside a definition
        {
            var (n, _, inherit) = sheet.Places[pi];
            n.ByLayer ??= n.Own.Select((e, i) => (e.Layer, i)).GroupBy(x => x.Layer, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Select(x => x.i).ToList(), StringComparer.Ordinal);
            foreach (var key in n.ByLayer.Keys)
            {
                var layer = key == "0" ? inherit : key;
                if (key == "0" && Excluded(layer)) continue;
                if (!sheet.Layers.TryGetValue(layer, out var l)) sheet.Layers[layer] = l = new List<int>();
                if (l.Count == 0 || l[l.Count - 1] != pi) l.Add(pi); // "0" and the layer it inherits are one visit
            }
        }
        foreach (var e in Entities(run, sheet)) e.Extend(ref sheet.Box);
        if (!sheet.Box.IsEmpty) (sheet.Sx, sheet.Sy) = (LitePlan.Shift(sheet.Box.MinX, t.InsUnits), LitePlan.Shift(sheet.Box.MinY, t.InsUnits));
        sheet.Names = LiteLayers.R12Names(_src.Layers.Keys.Concat(sheet.Layers.Keys));
        sheet.Colors = _src.Layers.ToDictionary(kv => kv.Key, kv => kv.Value.Color, StringComparer.Ordinal);
        return run.Sheet = sheet;
    }

    /// <summary>Everything placed in writing order, one at a time, each carrying its original layer: writing and measuring walk this same stream.</summary>
    IEnumerable<LiteEntity> Entities(LiteRun run, LiteSheet sheet)
    {
        double tol = run.Thresholds.ToleranceMm * run.Thresholds.UnitsPerMm;
        foreach (var kv in sheet.Layers)
        {
            string layer = kv.Key;
            foreach (int pi in kv.Value)
            {
                var (n, xf, inherit) = sheet.Places[pi];
                IEnumerable<int> at = inherit == layer && n.ByLayer!.ContainsKey("0")
                    ? Enumerable.Range(0, n.Own.Count).Where(i => n.Own[i].Layer == "0" || n.Own[i].Layer == layer) // both kinds, original order
                    : n.ByLayer![layer];
                foreach (int i in at) yield return CurveApprox.Transform(n.Own[i], xf, tol, layer);
            }
        }
    }
}
