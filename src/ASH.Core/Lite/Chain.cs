using System.Collections.Generic;

namespace ASH.Core.Lite;

/// <summary>Step E of AGVLITE: pieces joined into polylines, then thinned.</summary>
public static class Chain
{
    /// <summary>Joins lines and open polylines of one layer through end points where exactly two pieces meet, within a grid cell; branches and free ends stop a chain.
    /// A chain sits where its earliest piece was and runs that piece's way. Bulges stay with their spans. A piece that joins nothing, and every other entity, comes through as it is.
    /// A piece with both ends in one cell is left out: it has no way through.
    /// shortcut: rounding puts an edge in every grid cell, so ends under a grid apart across an edge do not link (as in SegKey), probe a shifted grid if such breaks show.</summary>
    public static List<LiteEntity> Join(IEnumerable<LiteEntity> src, double grid)
    {
        var all = new List<LiteEntity>(src);
        int n = all.Count;
        int[] start = new int[n], end = new int[n];                                   // end nodes of a piece, -1 for anything else
        int[] degree = new int[2 * n], touch0 = new int[2 * n], touch1 = new int[2 * n]; // per node: pieces touching it, the first two of them
        var nodes = new Dictionary<(string, long, long), int>();

        int Node(string layer, LiteVertex v)
        {
            var key = (layer, SegKey.Snap(v.X, grid), SegKey.Snap(v.Y, grid));
            if (!nodes.TryGetValue(key, out int id)) nodes[key] = id = nodes.Count;
            return id;
        }
        void Touch(int node, int piece)
        {
            if (degree[node]++ == 0) touch0[node] = piece;
            else touch1[node] = piece;
        }

        for (int i = 0; i < n; i++)
        {
            start[i] = end[i] = -1;
            if (Path(all[i]) is not { } path) continue;
            int a = Node(all[i].Layer, path[0]), b = Node(all[i].Layer, path[path.Count - 1]);
            if (a == b) continue;
            (start[i], end[i]) = (a, b);
            Touch(a, i);
            Touch(b, i);
        }

        var used = new bool[n];
        // Walks from `first` out through `node` while it is a pass-through. True: it came back round to `first`.
        bool Walk(int first, int node, List<(int Piece, bool Flip)> run)
        {
            for (int piece = first; degree[node] == 2;)
            {
                piece = touch0[node] == piece ? touch1[node] : touch0[node];
                if (used[piece]) return true;
                used[piece] = true;
                bool flip = end[piece] == node;
                run.Add((piece, flip));
                node = flip ? start[piece] : end[piece];
            }
            return false;
        }

        var o = new List<LiteEntity>(n);
        var ahead = new List<(int Piece, bool Flip)>();
        var behind = new List<(int Piece, bool Flip)>();
        for (int i = 0; i < n; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            ahead.Clear();
            behind.Clear();
            bool closed = start[i] >= 0 && Walk(i, end[i], ahead);
            if (start[i] >= 0 && !closed) Walk(i, start[i], behind);
            if (ahead.Count + behind.Count == 0) { o.Add(all[i]); continue; }

            var v = new List<LiteVertex>();
            for (int k = behind.Count - 1; k >= 0; k--) Append(v, all[behind[k].Piece], !behind[k].Flip);
            Append(v, all[i], false);
            foreach (var (piece, flip) in ahead) Append(v, all[piece], flip);
            v[v.Count - 1] = v[v.Count - 1] with { Bulge = 0 }; // an open end has no span to bulge
            closed = closed && v.Count > 3;                     // two pieces on the same two points would close on two vertices
            if (closed) v.RemoveAt(v.Count - 1);
            o.Add(new LitePolyline(all[i].Layer, v.ToArray(), closed));
        }
        return o;
    }

    /// <summary>The vertices of a piece Join can chain, else null.</summary>
    static IReadOnlyList<LiteVertex>? Path(LiteEntity e) => e switch
    {
        LiteLine l => new[] { new LiteVertex(l.X1, l.Y1), new LiteVertex(l.X2, l.Y2) },
        LitePolyline { Closed: false, Vertices.Count: >= 2 } p => p.Vertices,
        _ => null,
    };

    /// <summary>Adds the piece's vertices to the run; its first vertex stands in for the run's last one.</summary>
    static void Append(List<LiteVertex> run, LiteEntity piece, bool flip)
    {
        var p = Path(piece)!;
        int n = p.Count;
        for (int k = 0; k < n; k++)
        {
            // walked backwards, the span leaving a vertex is the one that arrived at it
            var x = !flip ? p[k] : new LiteVertex(p[n - 1 - k].X, p[n - 1 - k].Y, k < n - 1 ? -p[n - 2 - k].Bulge : 0);
            if (k == 0 && run.Count > 0) run[run.Count - 1] = run[run.Count - 1] with { Bulge = x.Bulge };
            else run.Add(x);
        }
    }

    /// <summary>Douglas-Peucker over the straight runs: no vertex removed lies farther than tol from the result. Both ends of a bulge span stay.
    /// A closed polyline is split at its start and at the vertex farthest from it. One thinned to two straight-joined points comes back open.</summary>
    public static LitePolyline Simplify(LitePolyline p, double tol)
    {
        var v = p.Vertices;
        int n = v.Count, spans = p.Closed ? n : n - 1;
        if (n < 3) return p;

        var keep = new bool[n];
        keep[0] = keep[p.Closed ? Farthest(v) : n - 1] = true;
        for (int i = 0; i < spans; i++)
            if (v[i].Bulge != 0) keep[i] = keep[(i + 1) % n] = true;
        for (int a = 0, b; a < spans; a = b)
        {
            for (b = a + 1; b < spans && !keep[b]; b++) { }
            Peucker(v, a, b, tol, keep);
        }

        var o = new List<LiteVertex>(n);
        for (int i = 0; i < n; i++)
            if (keep[i]) o.Add(v[i]);
        if (o.Count == n) return p;
        return p with { Vertices = o.ToArray(), Closed = p.Closed && (o.Count >= 3 || o.Exists(x => x.Bulge != 0)) };
    }

    static int Farthest(IReadOnlyList<LiteVertex> v)
    {
        int at = 0;
        double worst = -1;
        for (int i = 1; i < v.Count; i++)
        {
            double dx = v[i].X - v[0].X, dy = v[i].Y - v[0].Y, d = dx * dx + dy * dy;
            if (d > worst) (worst, at) = (d, i);
        }
        return at;
    }

    /// <summary>Marks what to keep between first and last, indices taken modulo the count so a run can end on vertex 0. A stack, not recursion: a long curve would go deep.</summary>
    static void Peucker(IReadOnlyList<LiteVertex> v, int first, int last, double tol, bool[] keep)
    {
        var todo = new Stack<(int A, int B)>();
        todo.Push((first, last));
        while (todo.Count > 0)
        {
            var (a, b) = todo.Pop();
            var (pa, pb) = (v[a], v[b % v.Count]);
            double worst = tol;
            int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                double d = CurveApprox.DistToSegment((v[i].X, v[i].Y), (pa.X, pa.Y), (pb.X, pb.Y));
                if (d > worst) (worst, at) = (d, i);
            }
            if (at < 0) continue;
            keep[at] = true;
            todo.Push((a, at));
            todo.Push((at, b));
        }
    }
}
