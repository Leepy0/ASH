using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ASH.Core.Asy;
using ASH.Core.Dxf;
using ASH.Core.Export;

namespace ASH.Core.Film;

/// <summary>Puts the factory drawing into a film model as an AutoMod static system (docs/autoview-guide.md section 11): ash_drw.asy with one HMF Polyline per run of
/// connected segments, plus a `STATIC name ash_drw` line in model.amo before PROCSYS. Works on a copy of the project, never on the user's model.
/// Lines only (no text, one grey colour for all layers); z = 0. After changing the .asy, delete &lt;Model&gt;.dir before the build (ModelBuilder.PrepareBuildDir).</summary>
public static class StaticDrawing
{
    public const string SystemName = "ash_drw";
    public static bool Supported => true;

    /// <param name="drawingInModelMetres">DxfLite.ToModel output (metres, ArcLayout frame).</param>
    /// <param name="units">The model's UNITS (ExportOptions.Units); ArgumentException for one ASH cannot convert.</param>
    /// <remarks>When no segment survives the hidden layers, an earlier call's ash_drw.asy and STATIC lines are removed instead.</remarks>
    public static void AddTo(string arcDir, DxfDrawing drawingInModelMetres, string units, IEnumerable<string>? hiddenLayers)
    {
        var upm = new ExportOptions(Units: units).UnitsPerMeter;
        var hidden = new HashSet<string>(hiddenLayers ?? new string[0], StringComparer.OrdinalIgnoreCase); // IReadOnlySet does not exist on netstandard2.0
        var segs = drawingInModelMetres.Segs.Where(s => !hidden.Contains(s.Layer)).ToList();
        var amoPath = Path.Combine(arcDir, "model.amo");
        var asyPath = Path.Combine(arcDir, SystemName + ".asy");
        const string Link = "STATIC name " + SystemName + "\n\tSYSPOS endx 1\n";
        if (segs.Count == 0) // nothing to draw: undo an earlier call so the model has no stale drawing
        {
            if (File.Exists(asyPath)) File.Delete(asyPath);
            if (File.Exists(amoPath)) File.WriteAllText(amoPath, File.ReadAllText(amoPath).Replace(Link, ""), new UTF8Encoding(false));
            return;
        }

        string N(double v) => AsyWriter.Num(v * upm);
        var runs = new List<List<(double X, double Y)>>(); string? layer = null;
        foreach (var s in segs)
        {
            // shortcut: exact-equality joins only (DXF polylines come in order); upgrade to endpoint matching if files get big
            if (runs.Count > 0 && s.Layer == layer && runs[runs.Count - 1][runs[runs.Count - 1].Count - 1] == (s.X1, s.Y1)) runs[runs.Count - 1].Add((s.X2, s.Y2));
            else runs.Add(new List<(double, double)> { (s.X1, s.Y1), (s.X2, s.Y2) });
            layer = s.Layer;
        }
        var xs = segs.SelectMany(s => new[] { s.X1, s.X2 }); var ys = segs.SelectMany(s => new[] { s.Y1, s.Y2 });

        var sb = new StringBuilder();
        void L(string t) => sb.Append(t).Append("\r\n");
        L("SYSTYPE Static"); L($"UNITS {units} Seconds"); L("SYSDEF"); L("FLAGS"); L("\tSystem Inherit"); L("STATICDEF UserId 9");
        L(" IND 0 display picpos endx 1"); L("\ttemplate " + units);
        L("511 81"); L($"{N(xs.Min())} {N(xs.Max())} {N(ys.Min())} {N(ys.Max())} 0 0"); L("-1 -1 0 1 1 " + SystemName);
        L(";; HMF V1.25 TEXT"); L("(Segment \"\" ("); L("  (Line_Weight 3)");
        L("  (Segment \"floor\" ((Color_By_Value \"Line\" \"RGB\" 0.6 0.6 0.6)"); // two levels as in guide 11.2
        foreach (var r in runs) L("    (Polyline (" + string.Join(" ", r.Select(p => $"({N(p.X)} {N(p.Y)} 0)")) + "))");
        L("  ))"); L("))"); L("#HMF END"); sb.Append("end");
        File.WriteAllText(asyPath, sb.ToString(), new UTF8Encoding(false));

        var amo = File.ReadAllText(amoPath);
        if (amo.Contains(Link)) return;
        var m = Regex.Match(amo, "^PROCSYS ", RegexOptions.Multiline);
        if (!m.Success) throw new InvalidDataException("model.amo에 PROCSYS 줄이 없습니다: " + amoPath);
        File.WriteAllText(amoPath, amo.Insert(m.Index, Link), new UTF8Encoding(false));
    }
}
