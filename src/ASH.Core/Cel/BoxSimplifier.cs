using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ASH.Core.Cel;

public enum Heading { PlusX, PlusY, MinusX, MinusY }   // which source axis points forward
public enum OriginAt { Front, Center, Back }           // where y = 0 sits along travel

/// <summary>MinPartPct: parts whose longest side is under this % of the whole length are dropped. SourceUnitToM: 0.001 for mm files (default), 1 for m, 0.0254 for in.
/// Grid: when the file has one part, split its bounding box into Grid x Grid cells (xy) and box each occupied cell (1 = off).</summary>
public sealed record SimplifyOptions(double MinPartPct = 2, int MaxBoxes = 200, Heading Heading = Heading.PlusX, OriginAt Origin = OriginAt.Center,
    int RotateDeg = 0, double SourceUnitToM = 0.001, int Grid = 1);

/// <summary>Metres, picture frame: x across, y along travel (front = -y), Z0 = bottom above the floor; Dx, Dy, H > 0.</summary>
public sealed record Box(string Name, double X, double Y, double Z0, double Dx, double Dy, double H, int Color = 9);

public sealed record SimplifyResult(IReadOnlyList<Box> Boxes, int DroppedSmall, int DroppedOverMax, double LengthM, double WidthM, double HeightM);

public static class BoxSimplifier
{
    // Rotation convention: Heading and RotateDeg are both rotations about +z (z up kept, right-handed kept),
    // positive = counter-clockwise seen from above: (x, y) -> (x cos t - y sin t, x sin t + y cos t).
    // Heading picks t so the source forward axis lands on picture -y:
    //   PlusX  -90 deg: (x, y) -> (y, -x)   e.g. source nose at (+2000, 0) mm -> picture (0, -2) m = front.
    //   PlusY  180 deg: (x, y) -> (-x, -y)
    //   MinusX +90 deg: (x, y) -> (-y, x)
    //   MinusY   0 deg: identity.
    // RotateDeg (multiple of 90) is added on top with the same sign: MinusY + RotateDeg 90 sends source +y to picture -x.
    const double MinSideM = 0.001;   // flat parts (sheet, decal) still need a positive thickness

    /// <summary>ArgumentException when no triangle survives.</summary>
    public static SimplifyResult Simplify(IReadOnlyList<MeshPart> parts, SimplifyOptions o)
    {
        if (parts == null) throw new ArgumentNullException(nameof(parts));
        if (o == null) throw new ArgumentNullException(nameof(o));
        if (o.RotateDeg % 90 != 0) throw new ArgumentException("RotateDeg must be a multiple of 90: " + o.RotateDeg, nameof(o));
        if (!(o.SourceUnitToM > 0)) throw new ArgumentException("SourceUnitToM must be > 0", nameof(o));
        int headingDeg = o.Heading switch { Heading.PlusX => -90, Heading.PlusY => 180, Heading.MinusX => 90, _ => 0 };
        int quarter = (((headingDeg + o.RotateDeg) / 90) % 4 + 4) % 4;
        double s = o.SourceUnitToM;
        Vec3 T(Vec3 v)
        {
            double x = v.X * s, y = v.Y * s, z = v.Z * s;
            return quarter switch { 1 => new Vec3(-y, x, z), 2 => new Vec3(-x, -y, z), 3 => new Vec3(y, -x, z), _ => new Vec3(x, y, z) };
        }

        // Per-part triangle AABBs in the picture frame, metres.
        var src = parts.Where(p => p.Triangles.Count > 0)
            .Select(p => (p.Name, Tris: p.Triangles.Select(t => Bounds(T(t.A), T(t.B), T(t.C))).ToList())).ToList();
        if (src.Count == 0) throw new ArgumentException("no triangles in mesh", nameof(parts));

        var all = Union(src.SelectMany(p => p.Tris));
        double length = all.Y1 - all.Y0, width = all.X1 - all.X0, height = all.Z1 - all.Z0;

        var raw = new List<(string Name, Aabb B)>();
        if (src.Count == 1 && o.Grid > 1)
        {
            // shortcut: a cell box is the cell clipped to the union of triangle AABBs touching it, so sloped faces over-fill; upgrade to triangle clipping if grid boxes look too fat.
            int g = o.Grid;
            double cx = Math.Max(width, 1e-12) / g, cy = Math.Max(length, 1e-12) / g;
            for (int i = 0; i < g; i++)
                for (int j = 0; j < g; j++)
                {
                    double x0 = all.X0 + i * cx, x1 = x0 + cx, y0 = all.Y0 + j * cy, y1 = y0 + cy;
                    var hit = src[0].Tris.Where(b => b.X0 <= x1 && b.X1 >= x0 && b.Y0 <= y1 && b.Y1 >= y0).ToList();
                    if (hit.Count == 0) continue;
                    var u = Union(hit);
                    raw.Add((src[0].Name + "_" + i + "_" + j,
                        new Aabb(Math.Max(u.X0, x0), Math.Min(u.X1, x1), Math.Max(u.Y0, y0), Math.Min(u.Y1, y1), u.Z0, u.Z1)));
                }
        }
        else
            foreach (var p in src) raw.Add((p.Name, Union(p.Tris)));

        double dz = -all.Z0;   // floor: lowest point -> 0
        double dy = o.Origin switch { OriginAt.Front => -all.Y0, OriginAt.Back => -all.Y1, _ => -(all.Y0 + all.Y1) / 2 };

        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        var boxes = new List<(Box Box, double Vol)>();
        int droppedSmall = 0;
        foreach (var (name, b) in raw)
        {
            double sx = b.X1 - b.X0, sy = b.Y1 - b.Y0, sz = b.Z1 - b.Z0;
            if (Math.Max(sx, Math.Max(sy, sz)) < o.MinPartPct / 100.0 * length) { droppedSmall++; continue; }
            double bx = Math.Max(sx, MinSideM), by = Math.Max(sy, MinSideM), bh = Math.Max(sz, MinSideM);
            boxes.Add((new Box(UniqueName(Sanitize(name), used), (b.X0 + b.X1) / 2, (b.Y0 + b.Y1) / 2 + dy, b.Z0 + dz, bx, by, bh), bx * by * bh));
        }
        int keep = Math.Max(0, Math.Min(o.MaxBoxes, boxes.Count));
        var kept = boxes.OrderByDescending(t => t.Vol).Take(keep).Select(t => t.Box).ToList();
        if (kept.Count == 0) throw new ArgumentException("no part survives the size/count filters", nameof(o));
        return new SimplifyResult(kept, droppedSmall, boxes.Count - keep, length, width, height);
    }

    readonly struct Aabb
    {
        public readonly double X0, X1, Y0, Y1, Z0, Z1;
        public Aabb(double x0, double x1, double y0, double y1, double z0, double z1) { X0 = x0; X1 = x1; Y0 = y0; Y1 = y1; Z0 = z0; Z1 = z1; }
    }

    static Aabb Bounds(Vec3 a, Vec3 b, Vec3 c) => new(
        Math.Min(a.X, Math.Min(b.X, c.X)), Math.Max(a.X, Math.Max(b.X, c.X)),
        Math.Min(a.Y, Math.Min(b.Y, c.Y)), Math.Max(a.Y, Math.Max(b.Y, c.Y)),
        Math.Min(a.Z, Math.Min(b.Z, c.Z)), Math.Max(a.Z, Math.Max(b.Z, c.Z)));

    static Aabb Union(IEnumerable<Aabb> bs)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
        foreach (var b in bs)
        {
            x0 = Math.Min(x0, b.X0); x1 = Math.Max(x1, b.X1);
            y0 = Math.Min(y0, b.Y0); y1 = Math.Max(y1, b.Y1);
            z0 = Math.Min(z0, b.Z0); z1 = Math.Max(z1, b.Z1);
        }
        return new Aabb(x0, x1, y0, y1, z0, z1);
    }

    static string Sanitize(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name ?? "")
            sb.Append((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' ? ch : '_');
        return sb.Length == 0 ? "part" : sb.ToString();
    }

    static string UniqueName(string n, Dictionary<string, int> used)
    {
        if (!used.TryGetValue(n, out int k)) { used[n] = 1; return n; }
        string c;
        do c = n + "_" + (++k); while (used.ContainsKey(c));
        used[n] = k; used[c] = 1;
        return c;
    }
}
