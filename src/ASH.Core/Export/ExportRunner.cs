using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
            var stem = asy + ".bak." + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            backup = stem;
            for (int i = 1; File.Exists(backup); i++) backup = stem + "_" + i;
            File.Copy(asy, backup, false);
        }
        AsyWriter.WriteFile(cur, asy);

        var tsv = Path.Combine(arcDir, "traffic.tsv");
        using (var w = new StreamWriter(tsv, false, new UTF8Encoding(false)))
            table.WriteTsv(w, o.Scale);
        return new ExportResult(asy, backup, tsv, summary, warnings);
    }
}
