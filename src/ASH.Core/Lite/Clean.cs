using System;
using System.Collections.Generic;

namespace ASH.Core.Lite;

/// <summary>Cleaning steps of AGVLITE on flattened geometry.</summary>
public static class Clean
{
    /// <summary>Step C. Removes lines shorter than eps, circles and arcs with radius under eps, and polylines left with under two vertices once repeated ones are gone.
    /// eps is 0.01 mm in drawing units. removed counts whole entities ("퇴화"); vertices trimmed from a surviving polyline are not counted.</summary>
    public static List<LiteEntity> Degenerate(IEnumerable<LiteEntity> src, double eps, out int removed)
    {
        var o = new List<LiteEntity>();
        removed = 0;
        foreach (var e in src)
        {
            LiteEntity? kept = e switch
            {
                LiteLine l => Dist(l.X1, l.Y1, l.X2, l.Y2) < eps ? null : l,
                LiteCircle c => c.R < eps ? null : c,
                LiteArc a => a.R < eps ? null : a,
                LitePolyline p => Trim(p, eps),
                _ => throw new ArgumentException("unknown lite entity " + e.GetType().Name),
            };
            if (kept == null) removed++;
            else o.Add(kept);
        }
        return o;
    }

    /// <summary>Step D. Keeps the first of each group of entities with one SegKey, whatever their layers. removed counts the rest ("중복").</summary>
    public static List<LiteEntity> Dedup(IEnumerable<LiteEntity> src, double grid, out int removed)
    {
        var o = new List<LiteEntity>();
        var seen = new HashSet<SegKey>();
        removed = 0;
        foreach (var e in src)
        {
            if (seen.Add(SegKey.Of(e, grid))) o.Add(e);
            else removed++;
        }
        return o;
    }

    /// <summary>Step D. Joins lines on one straight line whose spans overlap or lie at most grid apart. The result takes the outer end points of the run and sits where the run's first line was, on that line's layer.
    /// Other lines and other entities come through as they are, in order. removed counts the lines that went ("겹침").
    /// shortcut: the angle and offset bins are rounded, so collinear lines across a bin edge stay separate, and lines up to 0.01 degrees apart can join when they pivot near the origin's foot point, compare ends with the run's axis if that shows.</summary>
    public static List<LiteEntity> MergeCollinear(IEnumerable<LiteEntity> src, double grid, out int removed)
    {
        var all = new List<LiteEntity?>(src);
        var spans = new LineSpan[all.Count];
        int n = 0;
        for (int i = 0; i < all.Count; i++)
            if (all[i] is LiteLine l && LineSpan.Of(l, i, grid) is { } s) spans[n++] = s;
        Array.Sort(spans, 0, n);

        removed = 0;
        for (int i = 0, j; i < n; i = j)
        {
            int first = spans[i].Index, tip = i;
            for (j = i + 1; j < n && spans[j].Angle == spans[i].Angle && spans[j].Offset == spans[i].Offset && spans[j].T0 - spans[tip].T1 <= grid; j++)
            {
                if (spans[j].T1 > spans[tip].T1) tip = j;
                first = Math.Min(first, spans[j].Index);
            }
            if (j - i == 1) continue;

            LiteLine low = (LiteLine)all[spans[i].Index]!, high = (LiteLine)all[spans[tip].Index]!;
            var (x0, y0) = spans[i].Flip ? (low.X2, low.Y2) : (low.X1, low.Y1);
            var (x1, y1) = spans[tip].Flip ? (high.X1, high.Y1) : (high.X2, high.Y2);
            string layer = all[first]!.Layer;
            for (int k = i; k < j; k++) all[spans[k].Index] = null;
            all[first] = new LiteLine(layer, x0, y0, x1, y1);
            removed += j - i - 1;
        }
        all.RemoveAll(e => e == null);
        return all!;
    }

    /// <summary>A line as an interval [T0, T1] on the undirected straight line (Angle, Offset). Flip: its X2 end comes first. Sorted by line, then T0; Index makes the order total, so the result is deterministic.</summary>
    readonly struct LineSpan : IComparable<LineSpan>
    {
        const double AngleStep = 0.01; // degrees

        public readonly long Angle, Offset;
        public readonly double T0, T1;
        public readonly int Index;
        public readonly bool Flip;

        LineSpan(long angle, long offset, double t1, double t2, int index)
        {
            (Angle, Offset, T0, T1, Index, Flip) = (angle, offset, Math.Min(t1, t2), Math.Max(t1, t2), index, t1 > t2);
        }

        /// <summary>Null for a line with no direction.</summary>
        public static LineSpan? Of(LiteLine l, int index, double grid)
        {
            double dx = l.X2 - l.X1, dy = l.Y2 - l.Y1, len = Math.Sqrt(dx * dx + dy * dy);
            if (!(len > 0)) return null;
            double ux = dx / len, uy = dy / len;
            if (uy < 0 || (uy == 0 && ux < 0)) (ux, uy) = (-ux, -uy);
            long angle = (long)Math.Round(Math.Atan2(uy, ux) * 180 / Math.PI / AngleStep);
            if (angle == 18000) (angle, ux, uy) = (0, -ux, -uy); // 179.999 is -0.001 undirected, and the normal has to turn with it

            // positions along the bin's direction, not the line's: a tiny angle difference would shift t by the whole distance to the origin
            double bin = angle * AngleStep * Math.PI / 180, cx = Math.Cos(bin), cy = Math.Sin(bin);
            return new LineSpan(angle, SegKey.Snap(l.Y1 * ux - l.X1 * uy, grid), l.X1 * cx + l.Y1 * cy, l.X2 * cx + l.Y2 * cy, index);
        }

        public int CompareTo(LineSpan o)
        {
            int c = Angle.CompareTo(o.Angle);
            if (c == 0) c = Offset.CompareTo(o.Offset);
            if (c == 0) c = T0.CompareTo(o.T0);
            return c != 0 ? c : Index.CompareTo(o.Index);
        }
    }

    static LitePolyline? Trim(LitePolyline p, double eps)
    {
        var v = new List<LiteVertex>(p.Vertices.Count);
        foreach (var x in p.Vertices)
        {
            int last = v.Count - 1;
            if (last < 0 || !Repeats(v[last], x, eps)) v.Add(x);
            else v[last] = v[last] with { Bulge = x.Bulge }; // the span to x vanishes, the one leaving x stays
        }
        while (p.Closed && v.Count > 1 && Repeats(v[v.Count - 1], v[0], eps)) v.RemoveAt(v.Count - 1);
        return v.Count < 2 ? null : v.Count == p.Vertices.Count ? p : p with { Vertices = v };
    }

    /// <summary>The span from a to b is negligible. A nearly full bulge on a tiny chord is a whole ring: its sagitta |bulge| chord / 2 has to be small too.</summary>
    static bool Repeats(LiteVertex a, LiteVertex b, double eps)
    {
        double d = Dist(a.X, a.Y, b.X, b.Y);
        return d < eps && Math.Abs(a.Bulge) * d / 2 < eps;
    }

    static double Dist(double x1, double y1, double x2, double y2) => Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
}

/// <summary>Grid-snapped identity of an entity, its layer left out. Equality is exact on the snapped values; Hash is the 64-bit form kept per line when writing.
/// Hash comes from the snapped integers alone, never GetHashCode, so it is the same on every run and runtime.
/// shortcut: rounding puts an edge in every grid cell, so ends under a grid apart across an edge (0.04 and 0.06 at grid 0.1) get different keys, probe a second shifted grid if such near-duplicates survive.</summary>
public readonly struct SegKey : IEquatable<SegKey>
{
    const int Line = 1, Circle = 2, Arc = 3, Poly = 4;
    const double BulgeStep = 1e-6;

    /// <summary>Stable 64-bit hash of the key.</summary>
    public long Hash { get; }

    readonly int kind;
    readonly long a, b, c, d, e; // line ends in order, or centre, radius, arc start and end
    readonly long[]? path;       // polyline: x, y, bulge per vertex, then the closed flag

    SegKey(int kind, long a, long b, long c, long d, long e, long[]? path = null)
    {
        (this.kind, this.a, this.b, this.c, this.d, this.e, this.path) = (kind, a, b, c, d, e, path);
        ulong h = Mix(Mix(Mix(Mix(Mix((ulong)kind, a), b), c), d), e);
        if (path != null)
            foreach (long v in path) h = Mix(h, v);
        Hash = (long)h;
    }

    static ulong Mix(ulong h, long v)
    {
        h = (h ^ (ulong)v) * 0x9E3779B97F4A7C15UL;
        return h ^ (h >> 32);
    }

    /// <summary>Math.Round is symmetric about zero, so a bulge snaps to the opposite of its negation, and the cast turns -0 into 0.</summary>
    internal static long Snap(double v, double step) => step > 0 ? (long)Math.Round(v / step) : throw new ArgumentOutOfRangeException(nameof(step));

    /// <summary>The key of a segment and of its reverse.</summary>
    public static SegKey Of(double x1, double y1, double x2, double y2, double grid)
    {
        long ax = Snap(x1, grid), ay = Snap(y1, grid), bx = Snap(x2, grid), by = Snap(y2, grid);
        return ax < bx || (ax == bx && ay <= by) ? new SegKey(Line, ax, ay, bx, by, 0) : new SegKey(Line, bx, by, ax, ay, 0);
    }

    public static SegKey Of(LiteEntity e, double grid) => e switch
    {
        LiteLine l => Of(l.X1, l.Y1, l.X2, l.Y2, grid),
        LiteCircle c => new SegKey(Circle, Snap(c.Cx, grid), Snap(c.Cy, grid), Snap(c.R, grid), 0, 0),
        LiteArc a => OfArc(a, grid),
        LitePolyline p => OfPoly(p, grid),
        _ => throw new ArgumentException("unknown lite entity " + e.GetType().Name),
    };

    static SegKey OfArc(LiteArc a, double grid)
    {
        long r = Snap(a.R, grid);
        return new SegKey(Arc, Snap(a.Cx, grid), Snap(a.Cy, grid), r, Along(a.StartDeg, r), Along(a.EndDeg, r));
    }

    /// <summary>An arc end in grid cells along its circle, so the grid means the same on every radius. Sin and Cos are out: their last bits differ between runtimes.</summary>
    static long Along(double deg, long r)
    {
        long round = Math.Max(1, (long)Math.Round(2 * Math.PI * r));
        return ((long)Math.Round(deg * Math.PI / 180 * r) % round + round) % round;
    }

    /// <summary>The whole vertex run read in the smaller of its two directions. Only the run counts: a closed polyline started at another vertex is another key.</summary>
    static SegKey OfPoly(LitePolyline p, double grid)
    {
        int n = p.Vertices.Count;
        var fore = new long[3 * n + 1];
        var back = new long[3 * n + 1];
        fore[3 * n] = back[3 * n] = p.Closed ? 1 : 0;
        for (int i = 0; i < n; i++)
        {
            var v = p.Vertices[i];
            fore[3 * i] = Snap(v.X, grid);
            fore[3 * i + 1] = Snap(v.Y, grid);
            fore[3 * i + 2] = p.Closed || i < n - 1 ? Snap(v.Bulge, BulgeStep) : 0; // an open end has no span to bulge
        }
        for (int i = 0; i < n; i++)
        {
            int v = n - 1 - i;
            back[3 * i] = fore[3 * v];
            back[3 * i + 1] = fore[3 * v + 1];
            back[3 * i + 2] = -fore[3 * ((v + n - 1) % n) + 2]; // the span leaving v backwards is the one that arrived at it, walked the other way
        }
        return new SegKey(Poly, 0, 0, 0, 0, 0, fore.AsSpan().SequenceCompareTo(back) <= 0 ? fore : back);
    }

    public bool Equals(SegKey o) => Hash == o.Hash && kind == o.kind && a == o.a && b == o.b && c == o.c && d == o.d && e == o.e
        && (path == null ? o.path == null : o.path != null && path.AsSpan().SequenceEqual(o.path));

    public override bool Equals(object? obj) => obj is SegKey o && Equals(o);

    public override int GetHashCode() => (int)Hash;
}
