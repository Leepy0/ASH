using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ASH.Core.Layout;

namespace ASH.Core.Traffic;

public sealed record BlockEntry(string BlockName, string PathName, double BoundaryDist, string? CpName);

public sealed class TrafficTable
{
    public List<BlockEntry> Entries { get; } = new List<BlockEntry>();

    public static TrafficTable Build(Layout.Layout l)
    {
        var t = new TrafficTable();
        var cps = new List<(string Name, LayoutPath Path, double Dist)>();
        foreach (var c in l.Cps.Where(c => c.Type == "traffic"))
        {
            var pr = LayoutChecker.Project(l, c.Position);
            if (pr != null && pr.Value.Offset <= l.Tolerance * 10) cps.Add((c.Name, pr.Value.Path, pr.Value.Dist));
        }
        foreach (var b in l.Blocks)
            foreach (var path in l.Paths)
                foreach (var d in TrafficCpGenerator.Entries(path, b.Rect, l.Tolerance, out _))
                {
                    var cp = cps.Where(c => c.Path == path && c.Dist < d).OrderByDescending(c => c.Dist).Select(c => c.Name).FirstOrDefault();
                    t.Entries.Add(new BlockEntry(b.Name, path.Name, d, cp));
                }
        return t;
    }

    /// <summary>scale converts layout units to meters (ExportOptions.Scale), same as the .asy output.</summary>
    public void WriteTsv(TextWriter w, double scale = 1.0)
    {
        w.Write("block\tpath\tboundary_dist\tentry_cp\n");
        foreach (var e in Entries)
            w.Write(e.BlockName + "\t" + e.PathName + "\t" + (e.BoundaryDist * scale).ToString("R", CultureInfo.InvariantCulture) + "\t" + (e.CpName ?? "") + "\n");
    }
}
