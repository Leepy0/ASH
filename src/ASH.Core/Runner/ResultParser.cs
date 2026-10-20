using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ASH.Core.Runner;

public static class ResultParser
{
    static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    static double V(string[] c, int i) => i < c.Length && c[i].Length > 0 ? D(c[i]) : 0;

    public static ResultSet Parse(string tsvPath)
    {
        var mats = new List<MaterialResult>(); var groups = new List<GroupResult>(); var blocks = new List<BlockResult>();
        int dead = 0; double sim = 0;
        foreach (var line in File.ReadLines(tsvPath).Skip(1))
        {
            if (line.Trim().Length == 0) continue;
            var c = line.TrimEnd('\r').Split('\t');
            // value N is column 2+N
            switch (c[0])
            {
                case "material": mats.Add(new MaterialResult(c[1], c[2], V(c, 3), V(c, 4), (int)V(c, 5), V(c, 6), V(c, 7))); break;
                case "group": groups.Add(new GroupResult(c[1], (int)V(c, 3))); break;
                case "block": blocks.Add(new BlockResult(c[1], V(c, 3), V(c, 4), V(c, 5), V(c, 6))); break;
                case "deadlock": dead = (int)V(c, 3); break;
                case "clock": sim = V(c, 4); break;
            }
        }
        return new ResultSet(mats, groups, blocks, dead, sim);
    }

    /// <summary>Parses "Vehicle Statistics" (per type: vehicle rows, then "All type:" row; 12 numbers = Delivering/Retrieving/Going To Park
    /// each pct-trips-avgTime, then Parking pct) and "Block Statistics". shortcut: no blocked/down columns exist in this AM report,
    /// so blocking is not derivable here; upgrade if the logic starts writing it.</summary>
    public static VehicleStats ParseReport(string reportPath)
    {
        var lines = File.ReadAllLines(reportPath).Select(l => l.TrimEnd('\r')).ToList();
        var types = new Dictionary<string, TypeStats>(); var blocks = new List<BlockStat>(); var raw = new List<string>();
        var vi = lines.FindIndex(l => l.Trim() == "Vehicle Statistics");
        if (vi >= 0)
        {
            var i = vi + 1;
            while (i < lines.Count && !lines[i].StartsWith("===")) i++;
            bool allNext = false; string? type = null; List<VehicleRow> rows = new(); VehicleRow? all = null;
            void Flush() { if (type != null) types[type] = new TypeStats(rows, all); rows = new(); all = null; }
            for (i++; i < lines.Count && lines[i].Trim().Length > 0; i++)
            {
                var l = lines[i]; raw.Add(l);
                if (l.EndsWith(":") && !char.IsWhiteSpace(l[0]))
                {
                    if (l.StartsWith("All ")) { /* the average row follows; type stays */ allNext = true; continue; }
                    Flush(); type = l.TrimEnd(':'); allNext = false; continue;
                }
                var c = l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (c.Length < 10) continue;
                var r = new VehicleRow(D(c[0]), (int)D(c[1]), D(c[3]), (int)D(c[4]), D(c[6]), D(c[9]));
                if (allNext) { all = r; allNext = false; } else rows.Add(r);
            }
            Flush();
        }
        var bi = lines.FindIndex(l => l.Trim() == "Block Statistics");
        if (bi >= 0)
        {
            var i = bi + 1;
            while (i < lines.Count && !lines[i].StartsWith("===")) i++;
            for (i++; i < lines.Count && lines[i].Trim().Length > 0; i++)
            {
                raw.Add(lines[i]);
                var c = lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries); // Name Total Cur Average Capacity Max Min Util Av_Time Av_Wait
                if (c.Length >= 10) blocks.Add(new BlockStat(c[0], (int)D(c[1]), D(c[7]), D(c[8]), D(c[9])));
            }
        }
        return new VehicleStats(types, blocks, raw);
    }

}
