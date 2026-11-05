using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ASH.Core.Settings;

namespace ASH.Core.Lite;

/// <summary>DXF R12 (AC1009) ASCII writer for the lite drawing: HEADER ($ACADVER, $INSBASE, $EXTMIN/$EXTMAX, $LIMMIN/$LIMMAX), TABLES (LTYPE CONTINUOUS, LAYER),
/// an empty BLOCKS section, then LINE, POLYLINE/VERTEX/SEQEND (R12 has no LWPOLYLINE), CIRCLE and ARC on their layers, colour BYLAYER.
/// Group codes are right-aligned in 3 columns and reals always carry a decimal point, as AutoCAD writes R12; lines end in CRLF; text is plain ASCII
/// (layer names go through LiteLayers.R12Names first). Every entity is moved by (-dx, -dy).</summary>
public static class LiteDxfWriter
{
    const int AngleDecimals = 4; // degrees: 0.0001 is 0.2 mm on a 100 m radius, whatever the unit

    static readonly CultureInfo C = CultureInfo.InvariantCulture;
    static readonly string[] Formats = Enumerable.Range(0, 16).Select(d => "0." + new string('#', d)).ToArray();

    /// <summary>Decimals of a length in this unit: the power of ten nearest 0.01 mm (mm 2, inch 3, m 5).</summary>
    public static int Precision(int insUnits) => Math.Max(0, (int)Math.Round(-Math.Log10(1e-5 / UnitScale.FromInsUnits(insUnits))));

    /// <summary>Header, tables and the start of ENTITIES for a stream of entities (LiteBuild.Write): box is their extents, layers the original layer names they use.</summary>
    public static void Begin(TextWriter w, LiteBox box, IEnumerable<string> layers, IReadOnlyDictionary<string, string> names, IReadOnlyDictionary<string, short> colors, double dx, double dy, int decimals)
    {
        if (box.IsEmpty) box = new LiteBox { MinX = dx, MinY = dy, MaxX = dx, MaxY = dy };
        Pair(w, 0, "SECTION"); Pair(w, 2, "HEADER");
        Pair(w, 9, "$ACADVER"); Pair(w, 1, "AC1009");
        Pair(w, 9, "$INSBASE"); Xyz(w, 10, 0, 0, decimals);
        Pair(w, 9, "$EXTMIN"); Xyz(w, 10, box.MinX - dx, box.MinY - dy, decimals);
        Pair(w, 9, "$EXTMAX"); Xyz(w, 10, box.MaxX - dx, box.MaxY - dy, decimals);
        Pair(w, 9, "$LIMMIN"); Real(w, 10, box.MinX - dx, decimals); Real(w, 20, box.MinY - dy, decimals);
        Pair(w, 9, "$LIMMAX"); Real(w, 10, box.MaxX - dx, decimals); Real(w, 20, box.MaxY - dy, decimals);
        Pair(w, 0, "ENDSEC");

        // layer table: "0" always, then every layer an entity uses, in name order
        var table = layers.Distinct(StringComparer.Ordinal).Select(l => (Out: Name(names, l), Color: colors.TryGetValue(l, out var c) ? c : (short)7))
            .Where(l => l.Out != "0").GroupBy(l => l.Out, StringComparer.Ordinal).Select(g => g.First()).OrderBy(l => l.Out, StringComparer.Ordinal).ToList();
        Pair(w, 0, "SECTION"); Pair(w, 2, "TABLES");
        Pair(w, 0, "TABLE"); Pair(w, 2, "LTYPE"); Int(w, 70, 1);
        Pair(w, 0, "LTYPE"); Pair(w, 2, "CONTINUOUS"); Int(w, 70, 0); Pair(w, 3, "Solid line"); Int(w, 72, 65); Int(w, 73, 0); Real(w, 40, 0, decimals);
        Pair(w, 0, "ENDTAB");
        Pair(w, 0, "TABLE"); Pair(w, 2, "LAYER"); Int(w, 70, table.Count + 1);
        Layer(w, "0", colors.TryGetValue("0", out var c0) ? c0 : (short)7);
        foreach (var (o, col) in table) Layer(w, o, col);
        Pair(w, 0, "ENDTAB");
        Pair(w, 0, "ENDSEC");
        Pair(w, 0, "SECTION"); Pair(w, 2, "BLOCKS"); Pair(w, 0, "ENDSEC");

        Pair(w, 0, "SECTION"); Pair(w, 2, "ENTITIES");
    }

    public static void End(TextWriter w)
    {
        Pair(w, 0, "ENDSEC");
        Pair(w, 0, "EOF");
    }

    /// <summary>Bytes Entity writes for e: the size estimate is the writer itself, so it is exact for the entities (the header and tables add a few hundred bytes).</summary>
    public static long Bytes(LiteEntity e, string layer, double dx, double dy, int decimals)
    {
        var cw = new CountingWriter();
        Entity(cw, e, layer, dx, dy, decimals);
        return cw.Count;
    }

    public static void Entity(TextWriter w, LiteEntity e, string layer, double dx, double dy, int decimals)
    {
        switch (e)
        {
            case LiteLine l:
                Pair(w, 0, "LINE"); Pair(w, 8, layer);
                Xyz(w, 10, l.X1 - dx, l.Y1 - dy, decimals); Xyz(w, 11, l.X2 - dx, l.Y2 - dy, decimals);
                break;
            case LiteCircle c:
                Pair(w, 0, "CIRCLE"); Pair(w, 8, layer);
                Xyz(w, 10, c.Cx - dx, c.Cy - dy, decimals); Real(w, 40, c.R, decimals);
                break;
            case LiteArc a:
                Pair(w, 0, "ARC"); Pair(w, 8, layer);
                Xyz(w, 10, a.Cx - dx, a.Cy - dy, decimals); Real(w, 40, a.R, decimals); Real(w, 50, a.StartDeg, AngleDecimals); Real(w, 51, a.EndDeg, AngleDecimals);
                break;
            case LitePolyline p:
                Pair(w, 0, "POLYLINE"); Pair(w, 8, layer); Int(w, 66, 1);
                Xyz(w, 10, 0, 0, decimals); Int(w, 70, p.Closed ? 1 : 0);
                foreach (var v in p.Vertices)
                {
                    Pair(w, 0, "VERTEX"); Pair(w, 8, layer);
                    Xyz(w, 10, v.X - dx, v.Y - dy, decimals);
                    if (v.Bulge != 0) Bulge(w, v.Bulge);
                }
                Pair(w, 0, "SEQEND"); Pair(w, 8, layer);
                break;
            default: throw new ArgumentException("unknown lite entity " + e.GetType().Name);
        }
    }

    public static string Name(IReadOnlyDictionary<string, string> names, string layer) => names.TryGetValue(layer, out var n) ? n : "0";

    static void Layer(TextWriter w, string name, short color)
    {
        Pair(w, 0, "LAYER"); Pair(w, 2, name); Int(w, 70, 0);
        Int(w, 62, color >= 1 && color <= 255 ? color : 7); // negative = layer off in DXF; the lite drawing shows every layer it keeps
        Pair(w, 6, "CONTINUOUS");
    }

    static void Pair(TextWriter w, int code, string value)
    {
        w.Write(code < 10 ? "  " : code < 100 ? " " : "");
        w.Write(code.ToString(C));
        w.Write("\r\n");
        w.Write(value);
        w.Write("\r\n");
    }

    static void Int(TextWriter w, int code, int v) => Pair(w, code, v.ToString(C));
    static void Real(TextWriter w, int code, double v, int decimals) => Pair(w, code, Num(v, Formats[decimals]));
    static void Bulge(TextWriter w, double v) => Pair(w, 42, Num(v, "0.##########"));
    static void Xyz(TextWriter w, int code, double x, double y, int decimals) { Real(w, code, x, decimals); Real(w, code + 10, y, decimals); Pair(w, code + 20, "0.0"); }

    /// <summary>format holds the decimals; "100" is written "100.0".</summary>
    static string Num(double v, string format)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
        var s = v.ToString(format, C);
        if (s == "-0") s = "0";
        return s.IndexOf('.') < 0 ? s + ".0" : s;
    }

    internal sealed class CountingWriter : TextWriter
    {
        public long Count;
        public override System.Text.Encoding Encoding => System.Text.Encoding.ASCII;
        public override void Write(char value) => Count++;
        public override void Write(string? value) => Count += value?.Length ?? 0;
    }
}
