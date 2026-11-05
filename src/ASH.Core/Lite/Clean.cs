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
