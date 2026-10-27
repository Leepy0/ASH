using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Cel;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Core.Cause;

/// <summary>Layout-only: paths that pass closer to an obstacle than half the vehicle width plus the safety gap, or through it.
/// Obstacles (3D picture assets, metres): walls, fences, block assets (floor marks ZONE excepted; a door counts only by its wall pieces, so its opening is free)
/// and conveyors. Distance = path centre line to the obstacle's edge, the smallest per path and obstacle.</summary>
public static class ClearanceCauses
{
    const double Step = 0.05;   // shortcut: samples every 5 cm, a path touching an obstacle between two samples reads up to ~2.5 cm too far; fine for 10 cm gaps
    const double CrossM = 0.05; // at or under this a path counts as going through the obstacle (thin fence mesh is 2 cm)
    const double MaxPartZ = 1.0; // parts starting higher (robot arms, lintels) are above a vehicle

    sealed class Obs
    {
        public double Cx, Cy, Cos = 1, Sin, Hx, Hy, R; public bool Circle;
        public double Reach => Circle ? R : Math.Sqrt(Hx * Hx + Hy * Hy); // circumscribed circle: generous but cheap
        public double Dist(double x, double y)
        {
            double dx = x - Cx, dy = y - Cy;
            if (Circle) return Math.Max(0, Math.Sqrt(dx * dx + dy * dy) - R);
            double lx = Math.Abs(dx * Cos + dy * Sin) - Hx, ly = Math.Abs(-dx * Sin + dy * Cos) - Hy;
            lx = Math.Max(lx, 0); ly = Math.Max(ly, 0);
            return Math.Sqrt(lx * lx + ly * ly);
        }
    }

    sealed record Thing(string Label, List<Obs> Parts, Rect2 Box);

    public static IEnumerable<CauseItem> Find(CauseContext c)
    {
        var l = c.Layout;
        var things = Obstacles(l);
        if (things.Count == 0 || l.Paths.Count == 0) return Array.Empty<CauseItem>();
        double need = c.VehicleWidth / 2 + c.SafetyGap;
        var res = new List<CauseItem>();
        foreach (var p in l.Paths.Where(p => p.Segments.Count > 0 && p.Length > 0))
        {
            int n = Math.Max(1, (int)Math.Ceiling(p.Length / Step));
            var pts = Enumerable.Range(0, n + 1).Select(i => p.PointAt(p.Length * i / n)).ToList();
            var box = new Rect2(new Point2(pts.Min(q => q.X), pts.Min(q => q.Y)), new Point2(pts.Max(q => q.X), pts.Max(q => q.Y)));
            foreach (var t in things)
            {
                if (!t.Box.Expand(need).Intersects(box)) continue;
                double best = double.MaxValue; Point2 at = default;
                foreach (var q in pts)
                    foreach (var o in t.Parts)
                    {
                        if (Math.Abs(q.X - o.Cx) > o.Reach + need || Math.Abs(q.Y - o.Cy) > o.Reach + need) continue;
                        double d = o.Dist(q.X, q.Y);
                        if (d < best) { best = d; at = q; }
                    }
                if (best >= need) continue;
                var refs = new[] { p.Name };
                if (best <= CrossM)
                    res.Add(new CauseItem(CauseKinds.Clearance, 2, $"{p.Name}·{t.Label} 교차", p.Name, "", "", null, at.X, at.Y, refs));
                else
                    res.Add(new CauseItem(CauseKinds.Clearance, 1, $"{p.Name} 간격 {M(best)} m", p.Name, $"기준 {M(need)} m · {t.Label}", $"{M(need - best)} m 이격", null, at.X, at.Y, refs));
            }
        }
        return res.OrderByDescending(i => i.Severity).ThenBy(i => i.Where, StringComparer.Ordinal).ToList();
    }

    static string M(double v) => Math.Round(Math.Max(0, v), 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture);

    static List<Thing> Obstacles(Layout.Layout l)
    {
        var res = new List<Thing>();
        foreach (var a in l.Assets)
        {
            var kind = (a.Kind ?? "").Trim().ToUpperInvariant();
            if (kind == AssetKinds.Zone) continue;
            var cell = AssetShapes.Cell(a, 1);
            if (cell == null) continue;
            var label = kind == AssetKinds.Wall ? "벽" : kind == AssetKinds.Fence ? "펜스" : AssetShapes.Label(kind);
            double rot = cell.RotDeg * Math.PI / 180, cr = Math.Cos(rot), sr = Math.Sin(rot);
            var parts = cell.Parts.Where(p => kind == AssetKinds.Door ? p.Name.StartsWith("wall") : p.Z < MaxPartZ && p.RotX == 0)
                .Select(p =>
                {
                    double a2 = (cell.RotDeg + p.RotZ) * Math.PI / 180;
                    return new Obs { Cx = cell.X + p.X * cr - p.Y * sr, Cy = cell.Y + p.X * sr + p.Y * cr, Cos = Math.Cos(a2), Sin = Math.Sin(a2), Hx = p.Dx / 2, Hy = p.Dy / 2, R = p.Radius, Circle = p.Radius > 0 };
                }).ToList();
            Add(res, label, parts);
        }
        foreach (var cv in l.Conveyors)
        {
            double w = cv.WidthM > 0 ? cv.WidthM : 0.6;
            Add(res, "컨베이어", AssetShapes.Chords(cv.Segments).Select(ch =>
            {
                double ang = Math.Atan2(ch.B.Y - ch.A.Y, ch.B.X - ch.A.X);
                return new Obs { Cx = (ch.A.X + ch.B.X) / 2, Cy = (ch.A.Y + ch.B.Y) / 2, Cos = Math.Cos(ang), Sin = Math.Sin(ang), Hx = ch.A.DistanceTo(ch.B) / 2, Hy = w / 2 };
            }).ToList());
        }
        return res;
    }

    static void Add(List<Thing> res, string label, List<Obs> parts)
    {
        if (parts.Count == 0) return;
        double minX = parts.Min(o => o.Cx - o.Reach), minY = parts.Min(o => o.Cy - o.Reach), maxX = parts.Max(o => o.Cx + o.Reach), maxY = parts.Max(o => o.Cy + o.Reach);
        res.Add(new Thing(label, parts, new Rect2(new Point2(minX, minY), new Point2(maxX, maxY))));
    }
}
