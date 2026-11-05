using System;
using System.Collections.Generic;

namespace ASH.Core.Lite;

/// <summary>Step F of AGVLITE: removes what is smaller than the minimum size.</summary>
public static class SmallFilter
{
    /// <summary>Removes entities whose <see cref="GroupSizes"/> size is under minSize, keeping the order. removed counts them ("작은 요소").</summary>
    public static List<LiteEntity> Apply(IEnumerable<LiteEntity> src, double minSize, double snap, out int removed)
    {
        var all = new List<LiteEntity>(src);
        removed = 0;
        if (!(minSize > 0)) return all;
        var size = new double[all.Count];
        GroupSizes(all, snap, size);
        var o = new List<LiteEntity>(all.Count);
        for (int i = 0; i < all.Count; i++)
        {
            if (size[i] < minSize) removed++;
            else o.Add(all[i]);
        }
        return o;
    }

    /// <summary>Size of each item for the minimum-size rule: the box diagonal of the group it belongs to. Lines, arcs and open polylines whose end points
    /// meet (within <paramref name="snap"/>, drawing units) are one group, branches included and layers ignored, so a large outline drawn as many short pieces — common in equipment blocks
    /// from other CAD — is kept as a whole instead of losing every piece; a closed shape or a circle stands alone (a bolt hole stays a small detail).</summary>
    internal static void GroupSizes(IReadOnlyList<LiteEntity> items, double snap, double[] size)
    {
        int n = items.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        // one end point per grid cell is enough to link: every later point in or next to that cell within snap joins its item
        var cells = new Dictionary<(long, long), (double X, double Y, int Item)>();
        void End(int i, double x, double y)
        {
            long cx = (long)Math.Floor(x / snap), cy = (long)Math.Floor(y / snap);
            for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++)
                    if (cells.TryGetValue((cx + dx, cy + dy), out var c) && Math.Abs(c.X - x) <= snap && Math.Abs(c.Y - y) <= snap) Union(i, c.Item);
            if (!cells.ContainsKey((cx, cy))) cells[(cx, cy)] = (x, y, i);
        }
        for (int i = 0; i < n; i++)
        {
            switch (items[i])
            {
                case LiteLine l: End(i, l.X1, l.Y1); End(i, l.X2, l.Y2); break;
                case LiteArc a:
                    End(i, a.Cx + a.R * Math.Cos(a.StartDeg * Math.PI / 180), a.Cy + a.R * Math.Sin(a.StartDeg * Math.PI / 180));
                    End(i, a.Cx + a.R * Math.Cos(a.EndDeg * Math.PI / 180), a.Cy + a.R * Math.Sin(a.EndDeg * Math.PI / 180));
                    break;
                case LitePolyline p when !p.Closed && p.Vertices.Count >= 2:
                    End(i, p.Vertices[0].X, p.Vertices[0].Y); End(i, p.Vertices[p.Vertices.Count - 1].X, p.Vertices[p.Vertices.Count - 1].Y);
                    break;
            }
        }
        var boxes = new Dictionary<int, LiteBox>();
        for (int i = 0; i < n; i++)
        {
            var r = Find(i);
            var b = boxes.TryGetValue(r, out var x) ? x : LiteBox.Empty;
            items[i].Extend(ref b);
            boxes[r] = b;
        }
        for (int i = 0; i < n; i++)
        {
            size[i] = boxes[Find(i)].Diagonal;
        }
    }
}
