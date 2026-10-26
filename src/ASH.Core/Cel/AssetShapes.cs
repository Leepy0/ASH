using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Core.Export;
using ASH.Core.Film;
using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Core.Cel;

/// <summary>One primitive of a 3D asset, metres in the asset's own frame (x along its length, floor z = 0). A box is centred on X, Y with its bottom at Z
/// and turned RotZ degrees about its centre; Radius &gt; 0 makes it a cylinder of height H along its local z, tipped RotX degrees about x (Dx, Dy unused).</summary>
public sealed record AssetPart(string Name, int Color, double X, double Y, double Z, double Dx, double Dy, double H, double RotZ = 0, double Radius = 0, double RotX = 0);

/// <summary>An asset placed in the model: its parts are turned RotDeg about (X, Y) and moved there (metres, model frame).</summary>
public sealed record AssetCell(string Name, double X, double Y, double RotDeg, IReadOnlyList<AssetPart> Parts);

/// <summary>The 3D picture assets (design 2026-10-26 §8) as AutoMod cells: boxes 310, cylinders 320, one 700 21 group per asset under a 700 1 root, in the
/// static system ash_3d. Shapes are the prototype's (docs/superpowers/specs/2026-10-26-3d-assets). The cell units are the model's: `template Meters` and
/// `template Millimeters` place cells like `template Feet` (checked on AutoMod 14.5, 2026-10-26).</summary>
public static class AssetShapes
{
    public const string SystemName = "ash_3d";
    // AutoMod colour numbers (design §8.4)
    const int Red = 1, Green = 2, Yellow = 3, Blue = 4, Cyan = 6, LtGray = 8, DkGray = 9, Orange = 13, Khaki = 15;
    public const double WallThickM = 0.2, WallHeightM = 3, FenceHeightM = 2, FencePitchM = 2;
    const double ChordDeg = 10; // arcs of walls and fences become chords of at most this sweep

    /// <summary>Drawing frame (as DrawingReader reads it) to metres in the model frame: (p - origin) x scale, as ExportOptions.ToModel before the units.</summary>
    public static LayoutAsset InMetres(LayoutAsset a, Point2 origin, double scale)
    {
        Point2 P(Point2 p) => new Point2((p.X - origin.X) * scale, (p.Y - origin.Y) * scale);
        return new LayoutAsset
        {
            Kind = a.Kind, X = (a.X - origin.X) * scale, Y = (a.Y - origin.Y) * scale, RotDeg = a.RotDeg, Sx = a.Sx, Sy = a.Sy, WidthM = a.WidthM,
            Segments = a.Segments.Select(s => s is ArcSeg c ? new ArcSeg(P(c.Center), P(c.Start), c.SweepDeg) : (Segment)new LineSeg(P(s.Start), P(s.End))).ToList(),
        };
    }

    /// <summary>The cell of an asset in metres (model frame); null for an unknown kind, a line kind without segments or a non-positive scale.</summary>
    public static AssetCell? Cell(LayoutAsset a, int index)
    {
        var kind = (a.Kind ?? "").Trim().ToUpperInvariant();
        var name = kind.ToLowerInvariant() + index;
        if (kind == AssetKinds.Wall || kind == AssetKinds.Fence)
        {
            var chords = Chords(a.Segments).ToList();
            if (chords.Count == 0) return null;
            return new AssetCell(name, 0, 0, 0, kind == AssetKinds.Wall ? Wall(chords, a.WidthM > 0 ? a.WidthM : WallThickM) : Fence(chords));
        }
        if (!(a.Sx > 0 && a.Sy > 0)) return null;
        var parts = Parts(kind, a.Sx, a.Sy);
        return parts == null ? null : new AssetCell(name, a.X, a.Y, a.RotDeg, parts);
    }

    /// <summary>A block kind's parts at sx, sy times its base size (design §8.2); null for a kind that is not a block kind.</summary>
    public static IReadOnlyList<AssetPart>? Parts(string kind, double sx, double sy)
    {
        switch (kind)
        {
            case AssetKinds.Door: return Door(6 * sx);
            case AssetKinds.RobotL: return Robot(1.6 * sx);
            case AssetKinds.RobotM: return Robot(1.0 * sx);
            case AssetKinds.RobotS: return Robot(0.55 * sx);
            case AssetKinds.Rack: return Rack(3 * sx, 1.1 * sy);
            case AssetKinds.Machine: return Machine(2.5 * sx, 2 * sy);
            case AssetKinds.Column: return new[] { B("col", LtGray, 0, 0, 0, 0.5 * sx, 0.5 * sy, 4.5), B("stripe", Yellow, 0, 0, 0, 0.5 * sx + 0.02, 0.5 * sy + 0.02, 0.25) };
            case AssetKinds.Charger:
            {
                double w = 0.6 * sx, d = 0.4 * sy, h = 1.2;
                return new[] { B("body", DkGray, 0, 0, 0, w, d, h), B("led", Green, 0, -d / 2 - 0.01, h - 0.2, 0.15, 0.02, 0.05), B("contact", Yellow, 0, -d / 2 - 0.12, 0.15, 0.3, 0.24, 0.1) };
            }
            case AssetKinds.Zone: return Zone(3 * sx, 2 * sy);
            case AssetKinds.Bench:
            {
                double l = 1.8 * sx, d = 0.8 * sy, h = 0.9;
                var p = new List<AssetPart> { B("top", Orange, 0, 0, h - 0.05, l, d, 0.05) };
                for (int i = 0; i < 4; i++) p.Add(B("leg" + i, DkGray, (i % 2 == 0 ? 1 : -1) * (l / 2 - 0.05), (i < 2 ? 1 : -1) * (d / 2 - 0.05), 0, 0.05, 0.05, h - 0.05));
                p.Add(B("tool", Red, -0.4 * sx, 0, h, 0.3, 0.2, 0.15)); p.Add(B("bin", Blue, 0.4 * sx, 0.1 * sy, h, 0.4, 0.3, 0.2));
                return p;
            }
            default: return null;
        }
    }

    /// <summary>Korean name of a block kind (palette, AGV3D prompts, block labels); the kind itself for others.</summary>
    public static string Label(string kind) => kind switch
    {
        AssetKinds.RobotL => "로봇(대)", AssetKinds.RobotM => "로봇(중)", AssetKinds.RobotS => "로봇(소)", AssetKinds.Rack => "랙", AssetKinds.Machine => "설비",
        AssetKinds.Column => "기둥", AssetKinds.Charger => "충전기", AssetKinds.Bench => "작업대", AssetKinds.Door => "출입문", AssetKinds.Zone => "바닥 표시", _ => kind,
    };

    /// <summary>Plan bounding box (metres, asset frame) of a block kind at its base size, for the 2D symbol; null for a kind that is not a block kind.</summary>
    public static (double MinX, double MinY, double MaxX, double MaxY)? Footprint(string kind)
    {
        var ps = Parts(kind, 1, 1);
        if (ps == null) return null;
        double C(AssetPart p) => Math.Abs(Math.Cos(p.RotZ * Math.PI / 180));
        double S(AssetPart p) => Math.Abs(Math.Sin(p.RotZ * Math.PI / 180));
        double Hx(AssetPart p) => p.Radius > 0 ? p.Radius : (p.Dx * C(p) + p.Dy * S(p)) / 2; // half extents of a box turned about its centre
        double Hy(AssetPart p) => p.Radius > 0 ? p.Radius : (p.Dx * S(p) + p.Dy * C(p)) / 2;
        return (ps.Min(p => p.X - Hx(p)), ps.Min(p => p.Y - Hy(p)), ps.Max(p => p.X + Hx(p)), ps.Max(p => p.Y + Hy(p)));
    }

    static AssetPart B(string n, int c, double x, double y, double z, double dx, double dy, double h, double rz = 0) => new AssetPart(n, c, x, y, z, dx, dy, h, rz);
    static AssetPart C(string n, int c, double x, double y, double z, double r, double h, double rx = 0) => new AssetPart(n, c, x, y, z, 0, 0, h, 0, r, rx);

    static IReadOnlyList<AssetPart> Door(double l, double t = WallThickM, double h = WallHeightM)
    {
        double op = Math.Min(2.2, 0.8 * l), oh = 2.4, s = (l - op) / 2;
        return new[] { B("wall_l", LtGray, -(op / 2 + s / 2), 0, 0, s, t, h), B("wall_r", LtGray, op / 2 + s / 2, 0, 0, s, t, h), B("lintel", LtGray, 0, 0, oh, op, t, h - oh),
            B("frame_l", DkGray, -op / 2, 0, 0, 0.08, t + 0.04, oh), B("frame_r", DkGray, op / 2, 0, 0, 0.08, t + 0.04, oh) };
    }

    /// <summary>A 6-axis articulated robot; s = 1 reaches about 1.8 m. Profile seen from the south: lower arm up, upper arm reaching +x.</summary>
    static IReadOnlyList<AssetPart> Robot(double s)
    {
        double baseH = 0.25 * s, colH = 0.55 * s, a1L = 0.75 * s, a1T = 0.22 * s, a2L = 0.9 * s, a2T = 0.18 * s, wrL = 0.25 * s, wrT = 0.14 * s;
        double ze = baseH + colH + 0.15 * s + a1L - a2T / 2; // elbow height
        return new[] { C("base", DkGray, 0, 0, 0, 0.35 * s, baseH), C("j1", Yellow, 0, 0, baseH, 0.28 * s, colH),
            B("shoulder", Yellow, 0, 0, baseH + colH - 0.1 * s, 0.45 * s, 0.5 * s, 0.35 * s), B("arm1", Yellow, -0.05 * s, 0, baseH + colH + 0.15 * s, a1T, a1T, a1L),
            B("arm2", Yellow, a2L / 2 - 0.05 * s, 0, ze, a2L, a2T, a2T), C("elbow", DkGray, -0.05 * s, 0, ze - 0.02 * s, 0.16 * s, 0.3 * s, 90),
            B("wrist", DkGray, a2L - 0.05 * s + wrL / 2, 0, ze - 0.02 * s, wrL, wrT, wrT), B("tool", LtGray, a2L - 0.05 * s + wrL + 0.06 * s, 0, ze - 0.1 * s, 0.12 * s, 0.12 * s, 0.08 * s) };
    }

    /// <summary>Blue posts and yellow beams on 3 levels, one bay per about 3 m, two pallets with crates on the floor and the first level of each bay.</summary>
    static IReadOnlyList<AssetPart> Rack(double l, double d, double h = 4)
    {
        int bays = Math.Max(1, (int)Math.Round(l / 3)); double bay = l / bays;
        var p = new List<AssetPart>();
        for (int i = 0; i <= bays; i++) { p.Add(B("post_f" + i, Blue, -l / 2 + i * bay, -d / 2, 0, 0.08, 0.08, h)); p.Add(B("post_b" + i, Blue, -l / 2 + i * bay, d / 2, 0, 0.08, 0.08, h)); }
        for (int k = 0; k < 3; k++)
        {
            double z = k < 2 ? 1.3 * (k + 1) : h - 0.1;
            p.Add(B("beam_f" + k, Yellow, 0, -d / 2, z - 0.1, l, 0.05, 0.12)); p.Add(B("beam_b" + k, Yellow, 0, d / 2, z - 0.1, l, 0.05, 0.12));
        }
        double pw = Math.Min(1.1, bay / 2 - 0.1), pd = Math.Min(1.0, 0.9 * d);
        for (int b = 0; b < bays; b++)
            for (int k = 0; k < 2; k++)
                for (int j = 0; j < 2; j++)
                {
                    double x = -l / 2 + (b + 0.25 + 0.5 * j) * bay, z = 1.3 * k;
                    p.Add(B($"pal{b}{k}{j}", Orange, x, 0, z, pw, pd, 0.14)); p.Add(B($"crate{b}{k}{j}", Khaki, x, 0, z + 0.14, pw - 0.1, pd - 0.1, 0.8));
                }
        return p;
    }

    static IReadOnlyList<AssetPart> Machine(double l, double d, double h = 2.2) => new[] { B("body", LtGray, 0, 0, 0, l, d, h),
        B("panel", DkGray, l / 2 + 0.08, -d / 4, 0.9, 0.15, 0.5, 0.7), B("screen", Cyan, l / 2 + 0.16, -d / 4, 1.2, 0.01, 0.3, 0.25),
        B("lamp", Green, 0, 0, h, 0.08, 0.08, 0.3), B("door", DkGray, l / 2 + 0.01, d / 4, 0.3, 0.02, 0.8, 1.6) };

    /// <summary>Yellow 0.1 m border and a cross corner to corner, 1 cm high.</summary>
    static IReadOnlyList<AssetPart> Zone(double l, double w, double t = 0.1, double h = 0.01)
    {
        double diag = Math.Sqrt(l * l + w * w) - t, a = Math.Atan2(w, l) * 180 / Math.PI;
        return new[] { B("n", Yellow, 0, w / 2, 0, l, t, h), B("s", Yellow, 0, -w / 2, 0, l, t, h), B("e", Yellow, l / 2, 0, 0, t, w, h), B("w", Yellow, -l / 2, 0, 0, t, w, h),
            B("x1", Yellow, 0, 0, 0, diag, t, h, a), B("x2", Yellow, 0, 0, 0, diag, t, h, -a) };
    }

    /// <summary>Straight pieces of a wall or fence line: lines as they are, arcs split into chords of at most ChordDeg; zero-length pieces dropped.</summary>
    internal static IEnumerable<(Point2 A, Point2 B)> Chords(IEnumerable<Segment> segs)
    {
        foreach (var s in segs)
        {
            int n = s is ArcSeg c ? Math.Max(1, (int)Math.Ceiling(Math.Abs(c.SweepDeg) / ChordDeg - 1e-9)) : 1;
            var a = s.Start;
            for (int i = 1; i <= n; i++)
            {
                var b = i == n ? s.End : s.PointAt(s.Length * i / n);
                if (a.DistanceTo(b) > 1e-6) yield return (a, b);
                a = b;
            }
        }
    }

    static double Deg(Point2 a, Point2 b) => Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;
    static Point2 Mid(Point2 a, Point2 b) => new Point2((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    /// <summary>One box per piece, t thick and WallHeightM high, longer by t so the corners close.</summary>
    static IReadOnlyList<AssetPart> Wall(List<(Point2 A, Point2 B)> chords, double t) =>
        chords.Select((c, i) => { var m = Mid(c.A, c.B); return B("w" + i, LtGray, m.X, m.Y, 0, c.A.DistanceTo(c.B) + t, t, WallHeightM, Deg(c.A, c.B)); }).ToList();

    /// <summary>Per piece: dark mesh, yellow kick plate and top rail; yellow posts at most FencePitchM apart (one post where two pieces meet).</summary>
    static IReadOnlyList<AssetPart> Fence(List<(Point2 A, Point2 B)> chords, double h = FenceHeightM)
    {
        var p = new List<AssetPart>(); Point2? prev = null;
        for (int i = 0; i < chords.Count; i++)
        {
            var (a, b) = chords[i]; double len = a.DistanceTo(b), rz = Deg(a, b); var m = Mid(a, b);
            int n = Math.Max(1, (int)Math.Ceiling(len / FencePitchM - 1e-9));
            for (int k = prev.HasValue && prev.Value.DistanceTo(a) < 1e-6 ? 1 : 0; k <= n; k++)
                p.Add(B($"post{i}_{k}", Yellow, a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n, 0, 0.06, 0.06, h, rz));
            p.Add(B("mesh" + i, DkGray, m.X, m.Y, 0.1, len, 0.02, h - 0.2, rz)); p.Add(B("kick" + i, Yellow, m.X, m.Y, 0, len, 0.04, 0.12, rz));
            p.Add(B("rail" + i, Yellow, m.X, m.Y, h - 0.06, len, 0.05, 0.06, rz));
            prev = b;
        }
        return p;
    }

    /// <summary>The ash_3d.asy text (LF line ends): SYSTYPE Static, template = the model's units, a 700 1 root holding one 700 21 group per cell.</summary>
    public static string Asy(IReadOnlyList<AssetCell> cells, string units)
    {
        double k = new ExportOptions(Units: units).UnitsPerMeter;
        string F(double v) => VehicleGeometry.F(v * k);
        string A(double v) => VehicleGeometry.F(v);
        var l = new List<string> { "SYSTYPE Static", $"UNITS {units} Seconds", "SYSDEF", "FLAGS", "\tSystem Inherit", "STATICDEF UserId 9", " IND 0 display picpos endx 1", "\ttemplate " + units,
            "700 1", "1 1 0 1 1 " + SystemName, cells.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        foreach (var c in cells)
        {
            l.AddRange(new[] { "700 21", "1 1 0 1 1 " + c.Name, $"{F(c.X)}  {F(c.Y)}  0", "0 0 0 " + A(c.RotDeg), "\t1.00\t1.00\t1.00", c.Parts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            foreach (var p in c.Parts)
            {
                bool cyl = p.Radius > 0;
                l.Add(cyl ? "320 5" : "310 5");
                l.Add($"{p.Color} {p.Color} 0 1 1 {p.Name}");
                l.Add($"{F(p.X)}  {F(p.Y)}  {F(p.Z)}");
                l.Add($"0 {A(p.RotX)} 0 {A(p.RotZ)}");
                l.Add("\t1.00\t1.00\t1.00");
                l.Add(cyl ? $"{F(p.Radius)} {F(p.Radius)} {F(p.H)} 0 0" : $"{F(p.Dy)} {F(p.Dx)} {F(p.Dy)} {F(p.Dx)} {F(p.H)} 0 0"); // AutoMod reads the y extent first
            }
        }
        l.Add("end");
        return string.Join("\n", l) + "\n";
    }

    /// <summary>Writes &lt;arc&gt;\ash_3d.asy and its STATIC line in model.amo for the assets (metres, model frame); with none left, removes both.
    /// Returns the number of assets written. A changed static .asy needs &lt;Model&gt;.dir removed before the build (ModelBuilder.PrepareBuildDir).</summary>
    public static int AddTo(string arcDir, IEnumerable<LayoutAsset> assetsInMetres, string units)
    {
        var cells = assetsInMetres.Select((a, i) => Cell(a, i + 1)).Where(c => c != null).Select(c => c!).ToList();
        var asy = Path.Combine(arcDir, SystemName + ".asy");
        if (cells.Count == 0) { if (File.Exists(asy)) File.Delete(asy); }
        else File.WriteAllText(asy, Asy(cells, units), new UTF8Encoding(false));
        StaticDrawing.Link(arcDir, SystemName, cells.Count > 0);
        return cells.Count;
    }
}
