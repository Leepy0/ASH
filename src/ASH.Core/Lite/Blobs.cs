using System;
using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Lite;

/// <summary>Step G of AGVLITE: dense geometry becomes its outline, by measured geometry only. Occupancy is counted per CellMm cell (connectivity, outline), density per 1 m cell (R5);
/// a blob is replaced when ρ &gt; Dense and its diagonal &lt; MaxBlob. An entity as long as MaxBlob is left out of the occupancy, so a wall through equipment neither joins the
/// equipment into one long blob nor is removed with it (R6). Everything is in the local units of one definition; unitsPerMm converts to mm.</summary>
public sealed class Blobs
{
    public sealed class Blob
    {
        public readonly List<(long X, long Y)> Cells = new();
        public double LengthMm, DiagMm, Density;
        /// <summary>Lines and bytes that went into this blob (set by Replace), and their layers.</summary>
        public int Lines;
        public long Bytes;
        public readonly Dictionary<string, int> Layers = new(StringComparer.Ordinal);
        public bool Selected;
        public LiteBox Box(double cell)
        {
            var b = LiteBox.Empty;
            foreach (var (x, y) in Cells) { b.Add(x * cell, y * cell); b.Add((x + 1) * cell, (y + 1) * cell); }
            return b;
        }
    }

    readonly double _cell, _unit, _tol, _maxDiag, _dense, _maxBlobMm;
    readonly Dictionary<(long, long), double> _len = new();
    readonly Dictionary<(long, long), int> _blobOf = new();
    readonly List<Blob> _blobs = new();
    /// <summary>Chords walked into the occupancy so far (the budget counts these, not entities).</summary>
    public long Chords { get; private set; }

    /// <summary>cell, tol in local units; unitsPerMm = local units per mm (UnitsPerMm / scaleMax); dense = 0 selects nothing.</summary>
    public Blobs(double cell, double unitsPerMm, double tol, double denseMmPerM2, double maxBlobMm)
    {
        (_cell, _unit, _tol, _dense, _maxBlobMm) = (cell, unitsPerMm, tol, denseMmPerM2, maxBlobMm);
        _maxDiag = maxBlobMm * unitsPerMm;
    }

    public IReadOnlyList<Blob> All => _blobs;
    public IEnumerable<Blob> Selected => _blobs.Where(b => b.Selected);

    /// <summary>Left out of the occupancy and never removed (R6).</summary>
    public bool IsLong(LiteEntity e) => LiteBox.Size(e) >= _maxDiag;

    public void Add(LiteEntity e)
    {
        if (IsLong(e)) return;
        Chords += Walk(e, (x, y, l) => _len[(x, y)] = (_len.TryGetValue((x, y), out var v) ? v : 0) + l);
    }

    int Walk(LiteEntity e, Action<long, long, double> cell)
    {
        int n = 0;
        foreach (var (x1, y1, x2, y2) in LiteStat.Segments(new[] { e }, _tol)) { LiteStat.WalkCells(x1, y1, x2, y2, _cell, cell); n++; }
        return n;
    }

    /// <summary>Groups the occupied cells, measures each blob and selects the dense ones.</summary>
    public void Solve()
    {
        foreach (var g in Group(_len))
        {
            var b = new Blob();
            b.Cells.AddRange(g);
            foreach (var c in g) _blobOf[c] = _blobs.Count;
            var m = Measure(g, _len, _cell / _unit, _unit);
            (b.LengthMm, b.DiagMm, b.Density) = (m.LengthMm, m.DiagMm, m.Density);
            b.Selected = _dense > 0 && b.Density > _dense && b.DiagMm < _maxBlobMm;
            _blobs.Add(b);
        }
    }

    /// <summary>The selected blob every cell of every entity lies in, or -1: some entity is long, lies outside, or they span two blobs (an instance partly inside stays, R18).</summary>
    public int BlobOf(IEnumerable<LiteEntity> ents)
    {
        int id = -1, bad = 0;
        foreach (var e in ents)
        {
            if (IsLong(e)) return -1;
            Walk(e, (x, y, _) =>
            {
                if (bad > 0) return;
                if (!_blobOf.TryGetValue((x, y), out int b) || !_blobs[b].Selected || (id >= 0 && b != id)) bad++;
                id = b;
            });
            if (bad > 0) return -1;
        }
        return id;
    }

    /// <summary>The outer ring of a blob's cells on <paramref name="layer"/>.</summary>
    public LitePolyline Outline(Blob b, string layer) => Outline(b.Cells, _cell, _tol, layer);

    /// <summary>Cell → line length inside it, over the entities shorter than maxDiag.</summary>
    public static Dictionary<(long, long), double> Occupy(IEnumerable<LiteEntity> ents, double cell, double tol, double maxDiag)
    {
        var b = new Blobs(cell, 1, tol, 0, maxDiag);
        foreach (var e in ents) b.Add(e);
        return b._len;
    }

    /// <summary>8-connected groups of the cells, each sorted by (Y, X) and the groups by their first cell: no hash order anywhere.</summary>
    public static List<List<(long X, long Y)>> Group(IReadOnlyDictionary<(long, long), double> cells)
    {
        var keys = cells.Keys.OrderBy(c => c.Item2).ThenBy(c => c.Item1).ToList();
        var index = new Dictionary<(long, long), int>(keys.Count);
        for (int i = 0; i < keys.Count; i++) index[keys[i]] = i;
        var parent = Enumerable.Range(0, keys.Count).ToArray();
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        for (int i = 0; i < keys.Count; i++)
        {
            var (x, y) = keys[i];
            foreach (var d in new[] { (1L, 0L), (-1L, 1L), (0L, 1L), (1L, 1L) }) // each pair once: right, and the three above
                if (index.TryGetValue((x + d.Item1, y + d.Item2), out int j)) { int a = Find(i), b = Find(j); if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b); }
        }
        var groups = new List<List<(long, long)>>();
        var at = new Dictionary<int, int>();
        for (int i = 0; i < keys.Count; i++)
        {
            int r = Find(i);
            if (!at.TryGetValue(r, out int g)) { at[r] = g = groups.Count; groups.Add(new List<(long, long)>()); }
            groups[g].Add(keys[i]);
        }
        return groups;
    }

    /// <summary>Total line length, box diagonal of the cells (mm) and density = mm of line per distinct 1 m cell the blob crosses (R5). cellMm is the cell size in mm.</summary>
    public static (double LengthMm, double DiagMm, double Density) Measure(IReadOnlyList<(long X, long Y)> group, IReadOnlyDictionary<(long, long), double> lengths, double cellMm, double unitsPerMm)
    {
        double len = 0;
        long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
        var metres = new HashSet<(long, long)>();
        foreach (var (x, y) in group)
        {
            len += lengths[(x, y)];
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
            metres.Add(((long)Math.Floor(x * cellMm / 1000), (long)Math.Floor(y * cellMm / 1000)));
        }
        double w = (maxX + 1 - minX) * cellMm, h = (maxY + 1 - minY) * cellMm;
        return (len / unitsPerMm, Math.Sqrt(w * w + h * h), metres.Count == 0 ? 0 : len / unitsPerMm / metres.Count);
    }

    /// <summary>The outer ring of a cell set as a closed polyline (holes dropped), thinned with Chain.Simplify at tol. Cell edges run counter-clockwise with the cells on the left;
    /// where two cells touch only at a corner the ring turns right, so both stay inside one ring. The lowest-leftmost cell's bottom edge is on the outer ring, so the trace starts there.</summary>
    public static LitePolyline Outline(IReadOnlyList<(long X, long Y)> cells, double cell, double tol, string layer)
    {
        if (cells.Count == 0) return new LitePolyline(layer, Array.Empty<LiteVertex>(), true);
        var set = new HashSet<(long, long)>(cells);
        var outgoing = new Dictionary<(long, long), List<int>>(); // vertex → directions (0 east, 1 north, 2 west, 3 south) of boundary edges leaving it
        var dx = new long[] { 1, 0, -1, 0 };
        var dy = new long[] { 0, 1, 0, -1 };
        void Edge(long vx, long vy, int dir)
        {
            if (!outgoing.TryGetValue((vx, vy), out var l)) outgoing[(vx, vy)] = l = new List<int>(2);
            l.Add(dir);
        }
        var start = (X: long.MaxValue, Y: long.MaxValue);
        foreach (var (x, y) in cells)
        {
            if (y < start.Y || y == start.Y && x < start.X) start = (x, y);
            if (!set.Contains((x, y - 1))) Edge(x, y, 0);
            if (!set.Contains((x + 1, y))) Edge(x + 1, y, 1);
            if (!set.Contains((x, y + 1))) Edge(x + 1, y + 1, 2);
            if (!set.Contains((x - 1, y))) Edge(x, y + 1, 3);
        }
        var ring = new List<LiteVertex>();
        var (cx, cy, dir) = (start.X, start.Y, 0);
        int guard = 4 * cells.Count + 4; // every boundary edge at most once
        do
        {
            if (guard-- == 0) break;
            ring.Add(new LiteVertex(cx * cell, cy * cell));
            cx += dx[dir]; cy += dy[dir];
            var next = outgoing[(cx, cy)];
            int right = (dir + 3) % 4;
            dir = next.Count == 1 ? next[0] : next.Contains(right) ? right : next[0];
        } while ((cx, cy) != start);
        var p = new LitePolyline(layer, ring, true);
        return Chain.Simplify(p, tol);
    }

    /// <summary>Replaces every dense blob of <paramref name="ents"/> (local units of one definition at its largest instance scale) by its outline, on the layer most of its lines had.
    /// Entities not fully inside a selected blob stay. Stats: Blob (lines removed, their bytes), Blobs, TopBlobs (node tags them, null here).</summary>
    public static List<LiteEntity> Replace(IEnumerable<LiteEntity> ents, LiteThresholds t, double scaleMax, LiteStats stats, LiteNode? node = null)
    {
        var own = new List<LiteEntity>(ents);
        var kept = Replace(own, Array.Empty<Func<IEnumerable<LiteEntity>>>(), out _, t, scaleMax, stats, node, long.MaxValue);
        return kept;
    }

    /// <summary>As above, with child instances streamed in for the occupancy (R18): each child's geometry is enumerated, never stored. A child fully inside one selected blob is dropped
    /// (its index is left out of keptChildren); one partly inside stays. Past <paramref name="budget"/> chords (a runaway fan-out, never a real drawing) the children are given up:
    /// G runs over the own geometry alone, every child stays and keptChildren is null so the caller can report it.</summary>
    public static List<LiteEntity> Replace(IReadOnlyList<LiteEntity> own, IReadOnlyList<Func<IEnumerable<LiteEntity>>> children, out List<int>? keptChildren, LiteThresholds t, double scaleMax, LiteStats stats, LiteNode? node, long budget)
    {
        keptChildren = Enumerable.Range(0, children.Count).ToList();
        if (!(t.DenseMmPerM2 > 0)) return new List<LiteEntity>(own);
        double unit = t.UnitsPerMm / scaleMax, cell = t.CellMm * unit, tol = t.ToleranceMm * unit;
        var blobs = new Blobs(cell, unit, tol, t.DenseMmPerM2, t.MaxBlobMm);
        foreach (var e in own) blobs.Add(e);
        foreach (var c in children)
            foreach (var e in c())
            {
                blobs.Add(e);
                if (blobs.Chords > budget)
                {
                    var alone = Replace(own, Array.Empty<Func<IEnumerable<LiteEntity>>>(), out _, t, scaleMax, stats, node, long.MaxValue);
                    keptChildren = null;
                    return alone;
                }
            }
        blobs.Solve();
        if (!blobs.Selected.Any()) return new List<LiteEntity>(own);

        var o = new List<LiteEntity>(own.Count);
        void Took(Blob b, LiteEntity e) { b.Lines++; b.Bytes += LiteDxfWriter.Bytes(e, e.Layer, 0, 0); b.Layers[e.Layer] = (b.Layers.TryGetValue(e.Layer, out var n) ? n : 0) + 1; }
        foreach (var e in own)
        {
            int id = blobs.BlobOf(new[] { e });
            if (id < 0) o.Add(e);
            else Took(blobs._blobs[id], e);
        }
        keptChildren.Clear();
        for (int i = 0; i < children.Count; i++)
        {
            var took = new List<LiteEntity>();
            int id = blobs.BlobOf(Each(children[i](), took));
            if (id < 0) { keptChildren.Add(i); continue; }
            foreach (var e in took) Took(blobs._blobs[id], e);
            stats.DroppedInstances++;
        }
        foreach (var b in blobs.Selected)
        {
            if (b.Lines == 0) continue; // only long entities ran through it: nothing to replace
            string layer = b.Layers.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
            o.Add(blobs.Outline(b, layer));
            stats.Blobs++;
            stats.Blob.Count += b.Lines;
            stats.Blob.Bytes += b.Bytes;
            var box = b.Box(cell);
            stats.AddTop(new LiteBlobInfo(node, box.MinX, box.MinY, box.MaxX - box.MinX, box.MaxY - box.MinY, b.Lines, b.Density));
        }
        return o;
    }

    /// <summary>Passes the stream through while remembering it, only as far as BlobOf reads; a child kept whole is forgotten again.
    /// shortcut: a dropped child's entities are held for the tally (one instance at a time, never the drawing), stream the tally if an instance alone is too big.</summary>
    static IEnumerable<LiteEntity> Each(IEnumerable<LiteEntity> src, List<LiteEntity> sink)
    {
        foreach (var e in src) { sink.Add(e); yield return e; }
    }
}
