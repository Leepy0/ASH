using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Core.Asy;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;

namespace ASH.Core.Model;

/// <summary>
/// The conveyor system ash_conv.asy (SYSTYPE Conveyor, 14.x syntax as in the unload demo): one section per polyline segment, joined by transfers,
/// one motor and one accumulating section type per conveyor, every section drawn with the inline graphics template ASH_Roller_Green.
/// A named conveyor with a Conveyors row also gets the stations &lt;name&gt;_in (one product length from the start), _use (ConsumeAt, default the end)
/// and _out (the end) that code_conv.m drives; the others (no row, unnamed, AGV_3D_CONV) are sections only.
/// Arcs: AutoMod's angle is the sweep in tenths of a degree, counter-clockwise positive (the demo's -900 is a clockwise quarter turn).
/// </summary>
public static class ConveyorAsyWriter
{
    public const string SystemName = "ash_conv";
    public const double DefaultWidthM = 0.6, HeightM = 0.76, DefaultSpeedMps = 0.3;
    /// <summary>The product box (L_prod) length; the entry station sits this far from the start so the whole box is on the first section.</summary>
    public const double ProductLengthM = 0.5, ProductGapM = 0.1;

    static string Res(string name)
    {
        using var s = typeof(ConveyorAsyWriter).Assembly.GetManifestResourceStream("ASH.Core.Model.Templates." + name)
            ?? throw new InvalidOperationException("missing template " + name);
        using var r = new StreamReader(s);
        return r.ReadToEnd().Replace("\r\n", "\n");
    }

    /// <summary>The conveyors that carry products: named AGV_CONV polylines that have a Conveyors row (validation reports the rest).</summary>
    public static bool Flows(LayoutConveyor c, IEnumerable<ConveyorInfo> rows) =>
        !c.GraphicOnly && c.Name.Length > 0 && rows.Any(r => string.Equals(r.Conveyor, c.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Polyline length in metres (layout coordinates are drawing units).</summary>
    public static double LengthM(LayoutConveyor c, double scale) => c.Segments.Sum(s => s.Length) * scale;

    /// <summary>System name for sections: the conveyor's own name, G1, G2 ... for picture-only and unnamed ones.</summary>
    static List<string> Names(IReadOnlyList<LayoutConveyor> convs)
    {
        int g = 0;
        return convs.Select(c => !c.GraphicOnly && c.Name.Length > 0 && LayoutChecker.IsValidName(c.Name) ? c.Name : "G" + ++g).ToList();
    }

    public static void WriteFile(IReadOnlyList<LayoutConveyor> convs, IReadOnlyList<ConveyorInfo> rows, ExportOptions o, string path) =>
        File.WriteAllText(path, Render(convs, rows, o), new UTF8Encoding(false));

    public static string Render(IReadOnlyList<LayoutConveyor> convs, IReadOnlyList<ConveyorInfo> rows, ExportOptions o)
    {
        string N(double d) => AsyWriter.Num(d);
        double upm = o.UnitsPerMeter, toModel = o.Scale * upm, z = HeightM * upm;
        string u = o.Units;
        var sb = new StringBuilder();
        void L(string s) => sb.Append(s).Append('\n');
        L("SYSTYPE Conveyor");
        L($"UNITS {u} Seconds");
        L("SYSDEF timeout 60 Seconds confname Config1");
        L("FLAGS");
        foreach (var f in new[] { "System Inherit", "Text Invisible Inherit", "Sections GTemplate Inherit", "Section Names Invisible Inherit", "Direction Invisible Inherit",
                     "Fixed Interval Invisible Inherit", "Loads Inherit", "Stations Invisible Inherit", "Station Names Invisible Inherit", "Transfers Invisible Inherit",
                     "Photoeyes Invisible Inherit", "Photoeye Names Invisible Inherit", "Center Line Invisible Inherit" })
            L("\t" + f);
        L("CONVDEF UserId 8");
        L($"\tNEXTSEC name s9 type DefaultSection width {N(DefaultWidthM * upm)} {u}");
        L("\tNEXTSTA name t9 type DefaultStation");
        L("\tNEXTTRAN name tr9 type DefaultTransfer");
        L("\tNEXTMOTOR name motor1 type DefaultMotor");
        L("\tNEXTPHOTOEYE name photoeye1 type DefaultPhotoeye");
        L("\tALTERNATE NONE EXTRASECTIONWIDTH 0 Feet");
        L("CONVTOL minang 450 maxang 1350");
        L("CONVMOTORTYPE name DefaultMotor");
        var names = Names(convs);
        var secs = new List<string>(); var stations = new List<string>(); var transfers = new List<string>();
        // stop / move / induct space = 1 x the load's length (its x size) + a gap, so backed-up products stay apart as boxes
        var gap = $"1 {N(ProductGapM * upm)} {u}";
        string Type(double widthM, double speedMps) =>
            $"width {N(widthM * upm)} {u} color -1 motor {{0}} vel {N(speedMps * upm)} {u} Seconds acc 1 Feet Seconds Seconds dec 1 Feet Seconds Seconds " +
            $"accum load stopsize {gap} movesize {gap} inductsize {gap} nofixed align centered_in_interval attach rigid nav 1";
        // AutoMod wants a section type called DefaultSection (CONVDEF NEXTSEC); it carries the first motor
        bool defaultDone = false;
        for (int k = 0; k < convs.Count; k++)
        {
            var c = convs[k]; var n = names[k];
            var segs = c.Segments.Where(s => s.Length * toModel > 1e-6).ToList();
            if (segs.Count == 0) continue;
            var row = Flows(c, rows) ? rows.First(r => string.Equals(r.Conveyor, c.Name, StringComparison.OrdinalIgnoreCase)) : null;
            double w = c.WidthM > 0 ? c.WidthM : DefaultWidthM, v = row != null && row.SpeedMps > 0 ? row.SpeedMps : DefaultSpeedMps;
            string motor = "M_" + n, type = "T_" + n;
            L($"CONVMOTOR name {motor} type DefaultMotor");
            if (!defaultDone) { L("CONVSECTIONTYPE name DefaultSection " + string.Format(Type(w, v), motor)); defaultDone = true; }
            L($"CONVSECTIONTYPE name {type} " + string.Format(Type(w, v), motor));
            var lens = new List<double>();
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i]; string sec = $"{n}_s{i + 1}";
                var b = o.ToModel(s.Start);
                string piece = s is ArcSeg a
                    ? $"cenx {N(o.ToModel(a.Center).X)} ceny {N(o.ToModel(a.Center).Y)} cenz {N(z)} begx {N(b.X)} begy {N(b.Y)} begz {N(z)} upz 1 angle {N(a.SweepDeg * 10)}"
                    : $"begx {N(b.X)} begy {N(b.Y)} begz {N(z)} endx {N(o.ToModel(s.End).X)} endy {N(o.ToModel(s.End).Y)} endz {N(z)} upz 1";
                secs.Add($"CONVSECTION name {sec} type {type} width {N(w * upm)} {u} motor {motor} piece {piece}");
                lens.Add(s.Length * toModel);
                if (i > 0) transfers.Add($"CONVTRANSFER name {n}_t{i} type DefaultTransfer from {n}_s{i} {N(lens[i - 1])} to {sec} 0");
            }
            if (row == null) continue;
            // a distance along the polyline -> section and offset; a point on a joint belongs to the section that ends there
            string At(double d)
            {
                for (int i = 0; i < lens.Count; i++) { if (d <= lens[i] + 1e-9 || i == lens.Count - 1) return $"{n}_s{i + 1} {N(Math.Max(0, Math.Min(d, lens[i])))}"; d -= lens[i]; }
                throw new InvalidOperationException("no sections");
            }
            double total = lens.Sum();
            stations.Add($"CONVSTATION name {n}_in type DefaultStation at {At(Math.Min(ProductLengthM * upm, total))}");
            stations.Add($"CONVSTATION name {n}_use type DefaultStation at {At(UseM(c, row, total / upm) * upm)}");
            stations.Add($"CONVSTATION name {n}_out type DefaultStation at {At(total)}");
        }
        foreach (var s in secs) L(s);
        L("CONVSTATIONTYPE name DefaultStation raise 0 Seconds lower 0 Seconds dist 0 Feet release norestriction align leading cap Infinite scale 1 color -1 nrot 0 nscale 1");
        foreach (var s in stations) L(s);
        L("CONVPHOTOEYETYPE name DefaultPhotoeye blocktimeout 5 Seconds cleartimeout 5 Seconds");
        L("CONVTRANSFERTYPE name DefaultTransfer inductsize 1 0 Feet aheadinductsize 1 0 Feet speedadjust Origin starttime 0 Seconds finishtime 0 Seconds style double movemethod movesection");
        foreach (var t in transfers) L(t);
        sb.Append(Res("conv_gtemplate.txt").TrimEnd('\n')).Append('\n');
        foreach (var s in secs) { L("PATH_GT name " + s.Split(' ')[2]); L("\tATTACH_GT ASH_Roller_Green"); }
        return sb.ToString();
    }

    /// <summary>The use point in metres from the start: the drawing marker, else the Excel ConsumeAt (the marker wins).</summary>
    public static double? ConsumeAt(LayoutConveyor c, ConveyorInfo row) => c.ConsumeAtM ?? row.ConsumeAtM;

    /// <summary>The use point in metres from the start: <see cref="ConsumeAt"/>, or the end when it is empty or past the end.</summary>
    public static double UseM(LayoutConveyor c, ConveyorInfo row, double lengthM) => ConsumeAt(c, row) is double d && d < lengthM ? d : lengthM;
}
