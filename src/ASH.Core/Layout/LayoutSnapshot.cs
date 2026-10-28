using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Core.Geometry;

namespace ASH.Core.Layout;

/// <summary>
/// The Layout DrawingReader read, as plain text the server can build a model from (it cannot read the drawing's XData and block attributes itself).
/// First line "ash-layout\t1", then one object per line, tab separated, the kind first; numbers InvariantCulture "R". A segment list ends the line:
/// L x1 y1 x2 y2 or A cx cy sx sy sweep, one value per field. Tabs and line breaks in names become spaces.
/// </summary>
public static class LayoutSnapshot
{
    public const string FileName = "layout.tsv";
    const string Header = "ash-layout\t1";
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static void Write(Layout l, string path)
    {
        var sb = new StringBuilder(Header + "\n");
        void Line(params string[] f) => sb.Append(string.Join("\t", f)).Append('\n');
        Line("tol", N(l.Tolerance));
        foreach (var p in l.Paths)
            Line(new[] { "path", T(p.Name), T(p.Id), (p.IsSpur ? "s" : "") + (p.IsTwoWay ? "t" : "") + (p.IsCrab ? "c" : "") }.Concat(Segs(p.Segments)).ToArray());
        foreach (var c in l.Cps) Line("cp", T(c.Name), T(c.Type), T(c.Group), c.Cap.ToString(C), N(c.Position.X), N(c.Position.Y), T(c.Id), T(c.RouteId));
        foreach (var b in l.Blocks) Line("block", T(b.Name), b.Cap.ToString(C), N(b.Rect.Min.X), N(b.Rect.Min.Y), N(b.Rect.Max.X), N(b.Rect.Max.Y), T(b.Id), T(b.TagId));
        foreach (var s in l.Stations) Line("station", T(s.Name), N(s.Position.X), N(s.Position.Y));
        foreach (var a in l.Assets) Line(new[] { "asset", T(a.Kind), N(a.X), N(a.Y), N(a.RotDeg), N(a.Sx), N(a.Sy), N(a.WidthM) }.Concat(Segs(a.Segments)).ToArray());
        foreach (var v in l.Conveyors)
            Line(new[] { "conv", T(v.Name), v.GraphicOnly ? "1" : "0", N(v.WidthM), T(v.Id), v.ConsumeAtM is double u ? N(u) : "" }.Concat(Segs(v.Segments)).ToArray());
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Reads a file <see cref="Write"/> made. FormatException names the line for anything else.</summary>
    public static Layout Read(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0 || lines[0] != Header) throw new FormatException($"{FileName} 1행: 레이아웃 스냅샷이 아닙니다.");
        var l = new Layout();
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            var f = lines[i].Split('\t');
            try { ReadLine(l, f); }
            catch (Exception e) when (e is FormatException || e is IndexOutOfRangeException)
            {
                throw new FormatException($"{FileName} {i + 1}행이 올바르지 않습니다: {(e is FormatException ? e.Message : "칸 부족")}", e);
            }
        }
        return l;
    }

    static void ReadLine(Layout l, string[] f)
    {
        void Count(int n) { if (f.Length != n) throw new FormatException($"{f[0]} 칸 수 {f.Length} (필요 {n})"); }
        switch (f[0])
        {
            case "tol": Count(2); l.Tolerance = D(f[1]); break;
            case "path":
                var flags = f[3];
                if (flags.Any(c => c != 's' && c != 't' && c != 'c')) throw new FormatException("path 표시 '" + flags + "'");
                l.Paths.Add(new LayoutPath(f[1]) { Id = f[2], IsSpur = flags.IndexOf('s') >= 0, IsTwoWay = flags.IndexOf('t') >= 0, IsCrab = flags.IndexOf('c') >= 0, Segments = Segs(f, 4) });
                break;
            case "cp":
                Count(9);
                l.Cps.Add(new LayoutCp { Name = f[1], Type = f[2], Group = f[3], Cap = I(f[4]), Position = new Point2(D(f[5]), D(f[6])), Id = f[7], RouteId = f[8] });
                break;
            case "block":
                Count(9);
                l.Blocks.Add(new LayoutBlock { Name = f[1], Cap = I(f[2]), Rect = new Rect2(new Point2(D(f[3]), D(f[4])), new Point2(D(f[5]), D(f[6]))), Id = f[7], TagId = f[8] });
                break;
            case "station": Count(4); l.Stations.Add(new LayoutStation { Name = f[1], Position = new Point2(D(f[2]), D(f[3])) }); break;
            case "asset":
                l.Assets.Add(new LayoutAsset { Kind = f[1], X = D(f[2]), Y = D(f[3]), RotDeg = D(f[4]), Sx = D(f[5]), Sy = D(f[6]), WidthM = D(f[7]), Segments = Segs(f, 8) });
                break;
            case "conv":
                if (f[2] != "0" && f[2] != "1") throw new FormatException("conv graphicOnly '" + f[2] + "'");
                l.Conveyors.Add(new LayoutConveyor { Name = f[1], GraphicOnly = f[2] == "1", WidthM = D(f[3]), Id = f[4], ConsumeAtM = f[5].Length == 0 ? null : D(f[5]), Segments = Segs(f, 6) });
                break;
            default: throw new FormatException("알 수 없는 종류 '" + f[0] + "'");
        }
    }

    static IEnumerable<string> Segs(IEnumerable<Segment> segs)
    {
        foreach (var s in segs)
        {
            if (s is LineSeg a) { yield return "L"; yield return N(a.Start.X); yield return N(a.Start.Y); yield return N(a.End.X); yield return N(a.End.Y); }
            else if (s is ArcSeg r) { yield return "A"; yield return N(r.Center.X); yield return N(r.Center.Y); yield return N(r.Start.X); yield return N(r.Start.Y); yield return N(r.SweepDeg); }
            else throw new ArgumentException("unknown segment " + s.GetType().Name);
        }
    }

    static List<Segment> Segs(string[] f, int at)
    {
        var res = new List<Segment>();
        while (at < f.Length)
        {
            if (f[at] == "L" && at + 4 < f.Length) { res.Add(new LineSeg(new Point2(D(f[at + 1]), D(f[at + 2])), new Point2(D(f[at + 3]), D(f[at + 4])))); at += 5; }
            else if (f[at] == "A" && at + 5 < f.Length) { res.Add(new ArcSeg(new Point2(D(f[at + 1]), D(f[at + 2])), new Point2(D(f[at + 3]), D(f[at + 4])), D(f[at + 5]))); at += 6; }
            else throw new FormatException($"{at + 1}번째 칸의 선분 '{f[at]}'");
        }
        return res;
    }

    static string N(double v) => v.ToString("R", C);
    static string T(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    static double D(string s) => double.TryParse(s, NumberStyles.Float, C, out var d) ? d : throw new FormatException("숫자가 아님 '" + s + "'");
    static int I(string s) => int.TryParse(s, NumberStyles.Integer, C, out var n) ? n : throw new FormatException("정수가 아님 '" + s + "'");
}
