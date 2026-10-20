using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ASH.Core.Runner;

public sealed record SummaryRow(string RunId, string Group, int Active, int Seed, bool Ok, string Reason, double Utilization,
    double MinStockRatio, int Supplies, int Expected, double StopTimeSec, int Deadlocks, double DurationSec);

public static class SummaryTsv
{
    const string Header = "runId\tgroup\tactive\tseed\tok\treason\tutilization\tminStockRatio\tsupplies\texpected\tstopTimeSec\tdeadlocks\tdurationSec";
    static string F(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
    static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    public static void Append(string path, SummaryRow r)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var isNew = !File.Exists(path) || new FileInfo(path).Length == 0;
        var reason = r.Reason.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        var line = string.Join("\t", r.RunId, r.Group, r.Active.ToString(CultureInfo.InvariantCulture), r.Seed.ToString(CultureInfo.InvariantCulture),
            r.Ok ? "1" : "0", reason, F(r.Utilization), F(r.MinStockRatio), r.Supplies.ToString(CultureInfo.InvariantCulture),
            r.Expected.ToString(CultureInfo.InvariantCulture), F(r.StopTimeSec), r.Deadlocks.ToString(CultureInfo.InvariantCulture), F(r.DurationSec));
        File.AppendAllText(path, (isNew ? Header + "\n" : "") + line + "\n");
    }

    public static IReadOnlyList<SummaryRow> ReadAll(string path)
    {
        if (!File.Exists(path)) return new List<SummaryRow>();
        return File.ReadLines(path).Skip(1).Where(l => l.Trim().Length > 0).Select(l =>
        {
            var c = l.TrimEnd('\r').Split('\t');
            return new SummaryRow(c[0], c[1], int.Parse(c[2], CultureInfo.InvariantCulture), int.Parse(c[3], CultureInfo.InvariantCulture), c[4] == "1", c[5],
                D(c[6]), D(c[7]), int.Parse(c[8], CultureInfo.InvariantCulture), int.Parse(c[9], CultureInfo.InvariantCulture), D(c[10]),
                int.Parse(c[11], CultureInfo.InvariantCulture), D(c[12]));
        }).ToList();
    }
}
