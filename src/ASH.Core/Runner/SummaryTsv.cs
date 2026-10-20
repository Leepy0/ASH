using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ASH.Core.Runner;

/// <param name="Scenario">e.g. "G1=1;G2=2" (see ModelRunner.ScenarioText).</param>
public sealed record SummaryRow(string RunId, string Group, int Active, int Seed, bool Ok, string Reason, double Utilization,
    double MinStockRatio, int Supplies, double Expected, double StopTimeSec, int Deadlocks, double DurationSec, string Scenario = "");

public static class SummaryTsv
{
    const string Header = "runId\tgroup\tactive\tseed\tok\treason\tutilization\tminStockRatio\tsupplies\texpected\tstopTimeSec\tdeadlocks\tdurationSec\tscenario";
    const int Cols = 14;
    static readonly object Gate = new object(); // shortcut: in-process lock only; one writer process per summary file

    /// <summary>Doubles are written with at most 6 decimals, so a value read back is rounded to that.</summary>
    static string F(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
    static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public static void Append(string path, SummaryRow r)
    {
        var line = string.Join("\t", r.RunId, r.Group, r.Active.ToString(CultureInfo.InvariantCulture), r.Seed.ToString(CultureInfo.InvariantCulture),
            r.Ok ? "1" : "0", Clean(r.Reason), F(r.Utilization), F(r.MinStockRatio), r.Supplies.ToString(CultureInfo.InvariantCulture),
            F(r.Expected), F(r.StopTimeSec), r.Deadlocks.ToString(CultureInfo.InvariantCulture), F(r.DurationSec), Clean(r.Scenario));
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
            if (!isNew) CheckHeader(path);
            File.AppendAllText(path, (isNew ? Header + "\n" : "") + line + "\n");
        }
    }

    static void CheckHeader(string path)
    {
        string? first;
        using (var rd = new StreamReader(path)) first = rd.ReadLine();
        if ((first ?? "").TrimEnd('\r') != Header) throw new InvalidDataException("summary.tsv: unexpected header in " + path);
    }

    public static IReadOnlyList<SummaryRow> ReadAll(string path) => ReadAll(path, out _);

    /// <summary>skipped = rows dropped for a wrong column count or unparsable cell (e.g. a torn last line). Throws InvalidDataException on a foreign header.</summary>
    public static IReadOnlyList<SummaryRow> ReadAll(string path, out int skipped)
    {
        skipped = 0;
        var rows = new List<SummaryRow>();
        if (!File.Exists(path)) return rows;
        string[] lines;
        lock (Gate) lines = File.ReadAllLines(path);
        if (lines.Length == 0) return rows;
        if (lines[0].TrimEnd('\r') != Header) throw new InvalidDataException("summary.tsv: unexpected header in " + path);
        foreach (var l in lines.Skip(1))
        {
            if (l.Trim().Length == 0) continue;
            var c = l.TrimEnd('\r').Split('\t');
            try
            {
                if (c.Length != Cols) { skipped++; continue; }
                rows.Add(new SummaryRow(c[0], c[1], int.Parse(c[2], CultureInfo.InvariantCulture), int.Parse(c[3], CultureInfo.InvariantCulture), c[4] == "1", c[5],
                    D(c[6]), D(c[7]), int.Parse(c[8], CultureInfo.InvariantCulture), D(c[9]), D(c[10]),
                    int.Parse(c[11], CultureInfo.InvariantCulture), D(c[12]), c[13]));
            }
            catch (System.FormatException) { skipped++; }
        }
        return rows;
    }
}
