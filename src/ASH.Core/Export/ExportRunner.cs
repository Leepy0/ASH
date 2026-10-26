using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Core.Asy;
using ASH.Core.Traffic;

namespace ASH.Core.Export;

public static class ExportRunner
{
    /// <summary>Everything that can fail (export, diff, traffic table) runs before any file is touched; the old agv.asy is backed up, never overwritten.</summary>
    public static ExportResult Run(Layout.Layout l, ExportOptions o, string arcDir)
    {
        var asy = Path.Combine(arcDir, "agv.asy");
        AgvsSystem? prev = null;
        if (File.Exists(asy))
            try { prev = AsyReader.ReadFile(asy); } catch (Exception) { prev = null; } // unreadable old file: diff as all-new, backup kept
        var warnings = new List<string>();
        var cur = LayoutExporter.ToAgvs(l, o, warnings);
        var summary = ExportDiff.Compare(prev, cur);
        var table = TrafficTable.Build(l);

        Directory.CreateDirectory(arcDir);
        string? backup = null;
        if (File.Exists(asy))
        {
            var stem = asy + ".bak." + SeoulTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            backup = stem;
            for (int i = 1; File.Exists(backup); i++) backup = stem + "_" + i;
            File.Copy(asy, backup, false);
        }
        AsyWriter.WriteFile(cur, asy);

        var tsv = Path.Combine(arcDir, "traffic.tsv");
        using (var w = new StreamWriter(tsv, false, new UTF8Encoding(false)))
            table.WriteTsv(w, o.Scale);
        // meters, like traffic.tsv (ArcLayout reads the model's agv.asy back in meters too)
        var blocks = Path.Combine(arcDir, "blocks.tsv");
        using (var w = new StreamWriter(blocks, false, new UTF8Encoding(false)))
        {
            string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            w.Write("name\tcap\tminx\tminy\tmaxx\tmaxy\n");
            foreach (var b in l.Blocks)
                w.Write(b.Name + "\t" + b.Cap.ToString(CultureInfo.InvariantCulture) + "\t" + F((b.Rect.Min.X - o.Origin.X) * o.Scale) + "\t" + F((b.Rect.Min.Y - o.Origin.Y) * o.Scale)
                    + "\t" + F((b.Rect.Max.X - o.Origin.X) * o.Scale) + "\t" + F((b.Rect.Max.Y - o.Origin.Y) * o.Scale) + "\n");
        }
        // 3D picture assets, metres like blocks.tsv: segments "L x1 y1 x2 y2" or "A cx cy x1 y1 sweepDeg", joined by ';'
        using (var w = new StreamWriter(Path.Combine(arcDir, "assets.tsv"), false, new UTF8Encoding(false)))
        {
            string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            w.Write("kind\tx\ty\trot\tsx\tsy\twidth\tsegments\n");
            foreach (var a in l.Assets.Select(a => Cel.AssetShapes.InMetres(a, o.Origin, o.Scale)))
                w.Write(string.Join("\t", a.Kind, F(a.X), F(a.Y), F(a.RotDeg), F(a.Sx), F(a.Sy), F(a.WidthM), string.Join(";", a.Segments.Select(s => s is Geometry.ArcSeg c
                    ? $"A {F(c.Center.X)} {F(c.Center.Y)} {F(c.Start.X)} {F(c.Start.Y)} {F(c.SweepDeg)}" : $"L {F(s.Start.X)} {F(s.Start.Y)} {F(s.End.X)} {F(s.End.Y)}"))) + "\n");
        }
        return new ExportResult(asy, backup, tsv, summary, warnings, blocks);
    }
}
