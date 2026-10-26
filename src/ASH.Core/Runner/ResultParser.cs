using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ASH.Core.Cause;

namespace ASH.Core.Runner;

public static class ResultParser
{
    // "--" and other non-numbers become NaN so one bad cell cannot sink the whole report
    static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    static int I(string s) { var d = D(s); return double.IsNaN(d) || double.IsInfinity(d) ? 0 : (int)d; }

    /// <summary>Header + data rows (kind, name, group, value1..). Throws InvalidDataException("result.tsv: ...") for an empty file,
    /// a wrong header, a row with fewer than 3 columns, a non-numeric value cell, or no "clock" row (the completeness marker
    /// the logic writes last, so a missing one means the run died mid-write).</summary>
    public static ResultSet Parse(string tsvPath)
    {
        var lines = File.ReadAllLines(tsvPath);
        if (lines.Length == 0 || lines[0].TrimEnd('\r').Split('\t')[0] != "kind") throw new InvalidDataException("result.tsv: empty or no header");
        var mats = new List<MaterialResult>(); var groups = new List<GroupResult>(); var blocks = new List<BlockResult>();
        var rows = new List<ResultRow>();
        var vehicles = new List<VehicleResult>(); var mstats = new List<MaterialStat>(); var convs = new List<ConveyorResult>(); var times = new List<GroupTime>();
        var claims = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal); // group -> block -> claims (tblock rows)
        Dictionary<string, int> Claims(string g) => claims.TryGetValue(g, out var d) ? d : claims[g] = new Dictionary<string, int>(StringComparer.Ordinal);
        int dead = 0; double sim = 0; bool clock = false;
        var dlocks = new List<(double Ep, double T, DeadlockLink Link)>();
        for (var n = 1; n < lines.Length; n++)
        {
            if (lines[n].Trim().Length == 0) continue;
            var c = lines[n].TrimEnd('\r').Split('\t');
            if (c.Length < 3) throw new InvalidDataException($"result.tsv: line {n + 1}: {c.Length} column(s), need at least 3");
            if (c[0] == "dlock") { dlocks.Add(Dlock(c, n + 1)); continue; } // block names in the value columns: not a ResultRow
            var vals = new double[c.Length - 3];
            for (var i = 0; i < vals.Length; i++)
            {
                if (c[3 + i].Length == 0) continue;
                vals[i] = D(c[3 + i]);
                if (double.IsNaN(vals[i]) && c[3 + i] != "NaN") throw new InvalidDataException($"result.tsv: line {n + 1}: bad number '{c[3 + i]}'");
            }
            rows.Add(new ResultRow(c[0], c[1], c[2], vals));
            double V(int k) => k < vals.Length ? vals[k] : 0; // k = value index - 1
            switch (c[0])
            {
                case "material": mats.Add(new MaterialResult(c[1], c[2], V(0), V(1), (int)V(2), V(3), V(4), V(5))); break;
                case "group": groups.Add(new GroupResult(c[1], (int)V(0))); break;
                case "block": blocks.Add(new BlockResult(c[1], V(0), V(1), V(2), V(3))); break;
                case "vehicle": vehicles.Add(new VehicleResult(c[1], c[2], V(0), V(1), (int)V(2), V(3), (int)V(4), V(5),
                    vals.Length >= 11 ? new TimeSplit(V(3), V(0), V(6), V(7), V(8), V(9), V(10)) : null)); break;
                case "matstat": mstats.Add(new MaterialStat(c[1], c[2], V(0), V(1), V(2), V(3), V(4), V(5), V(6), V(7))); break;
                case "tsplit": times.Add(new GroupTime(c[1], (int)V(0), V(1), V(2), V(3), V(4), V(5), Claims(c[1]))); break;
                case "tblock": Claims(c[2])[c[1]] = (int)V(0); break;
                case "conveyor": convs.Add(new ConveyorResult(c[1], c[2], (int)V(0), V(1), V(2))); break;
                case "deadlock": dead = (int)V(0); break;
                case "clock": sim = V(1); clock = true; break;
            }
        }
        if (!clock) throw new InvalidDataException("result.tsv: no clock row (incomplete result)");
        var events = dlocks.GroupBy(x => x.Ep).Select(g => new DeadlockEvent(g.Min(x => x.T), g.Select(x => x.Link).ToList())).OrderBy(e => e.AtSec).ToList();
        return new ResultSet(mats, groups, blocks, dead, sim, rows, vehicles, mstats, convs, events, times.Count > 0 ? times : null);
    }

    /// <summary>dlock row (code_dlock.m): vehicle, group, time, episode, x, y (metres, empty = unknown), the block it waits for (or the CP where
    /// the vehicle it waits for stands), the blocks it holds.</summary>
    static (double Ep, double T, DeadlockLink Link) Dlock(string[] c, int line)
    {
        if (c.Length < 8) throw new InvalidDataException($"result.tsv: line {line}: dlock row has {c.Length} column(s), need at least 8");
        double Num(int i, bool opt = false)
        {
            if (opt && c[i].Length == 0) return double.NaN;
            var v = D(c[i]);
            if (double.IsNaN(v)) throw new InvalidDataException($"result.tsv: line {line}: bad number '{c[i]}'");
            return v;
        }
        // a CP prints with its system ("agv:L001"); a block name has none
        var waits = c[7].Substring(c[7].LastIndexOf(':') + 1);
        return (Num(4), Num(3), new DeadlockLink(c[1], c[2], waits, c.Skip(8).Where(x => x.Length > 0).ToList(), Num(5, true), Num(6, true)));
    }

    /// <summary>Parse without throwing: false + error message ("result.tsv: ...") when the file is unreadable or invalid.</summary>
    public static bool Validate(string tsvPath, out string? error)
    {
        try { Parse(tsvPath); error = null; return true; }
        catch (Exception e) when (e is InvalidDataException || e is IOException || e is UnauthorizedAccessException)
        {
            error = e is InvalidDataException ? e.Message : "result.tsv: " + e.Message;
            return false;
        }
    }

    /// <summary>Parses "Vehicle Statistics" (per type: vehicle rows, then "All type:" row; 12 numbers = Delivering/Retrieving/Going To Park
    /// each pct-trips-avgTime, then Parking pct) and "Block Statistics". shortcut: no blocked/down columns exist in this AM report,
    /// so blocking is not derivable here; upgrade if the logic starts writing it.</summary>
    public static ReportStats ParseReport(string reportPath)
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
                var r = new VehicleRow(D(c[0]), I(c[1]), D(c[3]), I(c[4]), D(c[6]), D(c[9]));
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
                if (c.Length >= 9) blocks.Add(new BlockStat(c[0], I(c[1]), D(c[7]), D(c[8]), c.Length > 9 ? D(c[9]) : double.NaN)); // Av_Wait may be absent
            }
        }
        var paths = new Dictionary<string, int>(StringComparer.Ordinal);
        var pi = lines.FindIndex(l => l.Trim() == "Guide Path Statistics");
        if (pi >= 0)
        {
            var i = pi + 1;
            while (i < lines.Count && !lines[i].StartsWith("===")) i++;
            for (i++; i < lines.Count && lines[i].Trim().Length > 0; i++)
            {
                var c = lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries); // Name Total Cur Average Capacity Max Min Util Av_Time Av_Wait
                if (c.Length >= 2 && int.TryParse(c[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) paths[c[0]] = n;
            }
        }
        return new ReportStats(types, blocks, raw, paths);
    }

}
