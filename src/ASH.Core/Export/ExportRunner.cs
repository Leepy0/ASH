using System;
using System.IO;
using System.Text;
using ASH.Core.Asy;
using ASH.Core.Traffic;

namespace ASH.Core.Export;

public static class ExportRunner
{
    public static ExportResult Run(Layout.Layout l, ExportOptions o, string arcDir)
    {
        Directory.CreateDirectory(arcDir);
        var asy = Path.Combine(arcDir, "agv.asy");
        string? backup = null;
        AgvsSystem? prev = null;
        if (File.Exists(asy))
        {
            backup = asy + ".bak." + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Copy(asy, backup, true);
            try { prev = AsyReader.ReadFile(asy); } catch (Exception) { prev = null; } // unreadable old file: diff as all-new, backup kept
        }
        var cur = LayoutExporter.ToAgvs(l, o);
        AsyWriter.WriteFile(cur, asy);

        var tsv = Path.Combine(arcDir, "traffic.tsv");
        using (var w = new StreamWriter(tsv, false, new UTF8Encoding(false)))
            TrafficTable.Build(l).WriteTsv(w);
        return new ExportResult(asy, backup, tsv, ExportDiff.Compare(prev, cur));
    }
}
