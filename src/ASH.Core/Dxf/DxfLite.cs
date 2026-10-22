using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ASH.Core.Dxf;

public sealed record DxfSeg(double X1, double Y1, double X2, double Y2, string Layer);
public sealed record DxfText(double X, double Y, double Height, string Text, string Layer);
public sealed record DxfDrawing(IReadOnlyList<DxfSeg> Segs, IReadOnlyList<DxfText> Texts, IReadOnlyList<string> Layers);

/// <summary>Reads just the lines and labels of an ASCII DXF (R12..2018 group codes) so the cut editor and the film model can show the factory drawing.
/// Curves become short segments. Anything else (hatch, dimension, spline, ellipse, 3D, INSERT arrays, extrusion mirroring) is ignored.
/// Coordinates stay in drawing units; ToModel moves them into model metres.</summary>
public static class DxfLite
{
    const int MaxNest = 4, MaxSegs = 2_000_000;
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    sealed class Ent { public string Type = ""; public List<(int Code, string Val)> P = new(); }
    sealed class Blk { public double Bx, By; public List<Ent> Ents = new(); }

    /// <summary>Throws InvalidDataException("binary DXF") for a binary file, FormatException for a broken group code.</summary>
    public static DxfDrawing Read(TextReader r)
    {
        var top = new List<Ent>();
        var blocks = new Dictionary<string, Blk>(StringComparer.OrdinalIgnoreCase);
        string section = ""; Ent? cur = null; Blk? blk = null; bool first = true;
        for (string? cl; (cl = r.ReadLine()) != null;)
        {
            if (first && cl.StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal)) throw new InvalidDataException("binary DXF");
            first = false;
            var val = r.ReadLine() ?? "";
            if (!int.TryParse(cl.Trim(), NumberStyles.Integer, C, out var code)) throw new FormatException("DXF 그룹 코드가 숫자가 아닙니다: " + cl.Trim());
            if (code == 0)
            {
                var t = val.Trim();
                if (t == "EOF") break;
                if (t == "ENDBLK") blk = null;
                cur = new Ent { Type = t };
                if (section == "ENTITIES") top.Add(cur);
                else if (section == "BLOCKS" && t != "SECTION" && t != "ENDSEC")
                {
                    if (t == "BLOCK") { blk = new Blk(); cur.Type = "BLOCK"; }
                    else if (blk != null) blk.Ents.Add(cur);
                }
                if (t == "ENDSEC") section = "";
                continue;
            }
            if (cur == null) continue;
            if (cur.Type == "SECTION" && code == 2) { section = val.Trim(); continue; }
            if (cur.Type == "BLOCK" && section == "BLOCKS")
            {
                if (code == 2) { blk = new Blk(); blocks[val.Trim()] = blk; }
                else if (blk != null && code == 10) blk.Bx = Num(val);
                else if (blk != null && code == 20) blk.By = Num(val);
                continue;
            }
            cur.P.Add((code, val));
        }
        var segs = new List<DxfSeg>(); var texts = new List<DxfText>();
        Expand(top, Xf.Id, "0", 0, blocks, segs, texts);
        var layers = segs.Select(s => s.Layer).Concat(texts.Select(t => t.Layer)).Distinct().ToList();
        return new DxfDrawing(segs, texts, layers);
    }

    /// <summary>model = (drawing - Origin) x Scale, the same frame ArcLayout uses (metres).</summary>
    public static DxfDrawing ToModel(DxfDrawing d, ModelFrame f)
    {
        double X(double v) => (v - f.OriginX) * f.Scale;
        double Y(double v) => (v - f.OriginY) * f.Scale;
        return new DxfDrawing(d.Segs.Select(s => new DxfSeg(X(s.X1), Y(s.Y1), X(s.X2), Y(s.Y2), s.Layer)).ToList(),
            d.Texts.Select(t => new DxfText(X(t.X), Y(t.Y), t.Height * f.Scale, t.Text, t.Layer)).ToList(), d.Layers);
    }

    static double Num(string s) => double.TryParse(s.Trim(), NumberStyles.Float, C, out var d) ? d : 0;
    static double Get(Ent e, int code, double def = 0) { foreach (var (c, v) in e.P) if (c == code) return Num(v); return def; }
    static bool Has(Ent e, int code) => e.P.Any(p => p.Code == code);
    static string Layer(Ent e, string inherit) { var l = e.P.FirstOrDefault(p => p.Code == 8).Val?.Trim(); return string.IsNullOrEmpty(l) || l == "0" ? inherit : l!; }

    /// <summary>x' = A x + B y + E, y' = C x + D y + F.</summary>
    readonly record struct Xf(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Xf Id = new(1, 0, 0, 1, 0, 0);
        public (double, double) Ap(double x, double y) => (A * x + B * y + E, C * x + D * y + F);
        public Xf Then(Xf o) => new(o.A * A + o.B * C, o.A * B + o.B * D, o.C * A + o.D * C, o.C * B + o.D * D, o.A * E + o.B * F + o.E, o.C * E + o.D * F + o.F);
    }

    static void Expand(List<Ent> ents, Xf xf, string inherit, int depth, Dictionary<string, Blk> blocks, List<DxfSeg> segs, List<DxfText> texts)
    {
        void Poly(List<(double X, double Y)> pts, bool closed, string layer)
        {
            if (closed && pts.Count > 2) pts = pts.Append(pts[0]).ToList();
            for (int i = 1; i < pts.Count && segs.Count < MaxSegs; i++)
            {
                var (x1, y1) = xf.Ap(pts[i - 1].X, pts[i - 1].Y); var (x2, y2) = xf.Ap(pts[i].X, pts[i].Y);
                segs.Add(new DxfSeg(x1, y1, x2, y2, layer));
            }
        }
        for (int i = 0; i < ents.Count; i++)
        {
            var e = ents[i]; var layer = Layer(e, inherit);
            switch (e.Type)
            {
                case "LINE": Poly(new() { (Get(e, 10), Get(e, 20)), (Get(e, 11), Get(e, 21)) }, false, layer); break;
                case "LWPOLYLINE":
                {
                    var v = new List<(double X, double Y, double B)>();
                    foreach (var (c, s) in e.P)
                        if (c == 10) v.Add((Num(s), 0, 0));
                        else if (c == 20 && v.Count > 0) v[v.Count - 1] = (v[v.Count - 1].X, Num(s), v[v.Count - 1].B);
                        else if (c == 42 && v.Count > 0) v[v.Count - 1] = (v[v.Count - 1].X, v[v.Count - 1].Y, Num(s));
                    { bool cl = ((int)Get(e, 70) & 1) != 0; Poly(Bulged(v, cl), cl, layer); }
                    break;
                }
                case "POLYLINE":
                {
                    var v = new List<(double X, double Y, double B)>();
                    int flags = (int)Get(e, 70);
                    for (i++; i < ents.Count && ents[i].Type != "SEQEND"; i++)
                        if (ents[i].Type == "VERTEX" && ((int)Get(ents[i], 70) & 128) == 0) v.Add((Get(ents[i], 10), Get(ents[i], 20), Get(ents[i], 42)));
                    if ((flags & (16 | 64)) == 0) { bool cl = (flags & 1) != 0; Poly(Bulged(v, cl), cl, layer); } // 3D meshes and polyface meshes are skipped
                    break;
                }
                case "CIRCLE": Poly(Arc(Get(e, 10), Get(e, 20), Get(e, 40), 0, 360), false, layer); break;
                case "ARC":
                {
                    double a0 = Get(e, 50), a1 = Get(e, 51), sw = a1 - a0;
                    if (sw <= 0) sw += 360;
                    Poly(Arc(Get(e, 10), Get(e, 20), Get(e, 40), a0, sw), false, layer);
                    break;
                }
                case "TEXT":
                case "MTEXT":
                {
                    bool aligned = e.Type == "TEXT" && (Has(e, 11) && ((int)Get(e, 72) != 0 || (int)Get(e, 73) != 0));
                    var (x, y) = xf.Ap(aligned ? Get(e, 11) : Get(e, 10), aligned ? Get(e, 21) : Get(e, 20));
                    var raw = e.Type == "TEXT" ? e.P.FirstOrDefault(p => p.Code == 1).Val ?? "" : string.Concat(e.P.Where(p => p.Code == 3).Select(p => p.Val)) + (e.P.FirstOrDefault(p => p.Code == 1).Val ?? "");
                    var text = (e.Type == "TEXT" ? raw.Replace("%%d", "°").Replace("%%p", "±").Replace("%%c", "Ø") : Mtext(raw)).Trim();
                    double h = Get(e, 40) * Math.Sqrt(Math.Abs(xf.A * xf.D - xf.B * xf.C));
                    if (text.Length > 0) texts.Add(new DxfText(x, y, h, text, layer));
                    break;
                }
                case "INSERT":
                    if (depth < MaxNest && blocks.TryGetValue(e.P.FirstOrDefault(p => p.Code == 2).Val?.Trim() ?? "", out var b))
                    {
                        double sx = Get(e, 41, 1), sy = Get(e, 42, 1), rot = Get(e, 50) * Math.PI / 180, co = Math.Cos(rot), si = Math.Sin(rot);
                        var local = new Xf(co * sx, -si * sy, si * sx, co * sy, 0, 0);
                        local = local with { E = Get(e, 10) - (local.A * b.Bx + local.B * b.By), F = Get(e, 20) - (local.C * b.Bx + local.D * b.By) };
                        Expand(b.Ents, local.Then(xf), layer, depth + 1, blocks, segs, texts);
                    }
                    break;
            }
        }
    }

    /// <summary>Bulge b = tan(sweep/4); the arc is cut into steps of at most 15 degrees, never fewer than 8 (error under 0.5% of the radius).</summary>
    static List<(double X, double Y)> Bulged(List<(double X, double Y, double B)> v, bool closed)
    {
        var o = new List<(double X, double Y)>();
        int n = closed ? v.Count : v.Count - 1;
        for (int i = 0; i < n; i++)
        {
            var (x1, y1, b) = v[i]; var (x2, y2, _) = v[(i + 1) % v.Count];
            o.Add((x1, y1));
            if (b == 0) continue;
            double sweep = 4 * Math.Atan(b), chord = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            if (chord == 0) continue;
            double r = chord / (2 * Math.Sin(Math.Abs(sweep) / 2)), mx = (x1 + x2) / 2, my = (y1 + y2) / 2;
            double d = r * Math.Cos(sweep / 2) * Math.Sign(sweep); // centre sits left of the chord for CCW arcs
            double cx = mx - (y2 - y1) / chord * d, cy = my + (x2 - x1) / chord * d;
            double a0 = Math.Atan2(y1 - cy, x1 - cx);
            int k = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweep) * 180 / Math.PI / 15));
            for (int j = 1; j < k; j++) o.Add((cx + r * Math.Cos(a0 + sweep * j / k), cy + r * Math.Sin(a0 + sweep * j / k)));
        }
        if (!closed) o.Add((v[v.Count - 1].X, v[v.Count - 1].Y));
        return o;
    }

    static List<(double X, double Y)> Arc(double cx, double cy, double r, double startDeg, double sweepDeg)
    {
        int k = Math.Max(1, (int)Math.Ceiling(36 * Math.Abs(sweepDeg) / 360));
        var o = new List<(double X, double Y)>();
        for (int j = 0; j <= k; j++) { double a = (startDeg + sweepDeg * j / k) * Math.PI / 180; o.Add((cx + r * Math.Cos(a), cy + r * Math.Sin(a))); }
        return o;
    }

    static readonly Regex MtextCodes = new(@"\\[fFcCtTqQwWaAhH][^;]*;|\\S([^;]*);|\\[pP]|\\[LlOoKk]|\\([\\{}])|\\~|[{}]", RegexOptions.Compiled);
    static string Mtext(string s) => MtextCodes.Replace(s, m =>
        m.Value.StartsWith("\\S") ? m.Groups[1].Value : m.Value is "\\P" or "\\p" or "\\~" ? " " : m.Groups[2].Success ? m.Groups[2].Value : "");
}
