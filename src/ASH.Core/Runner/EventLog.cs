using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ASH.Core.Runner;

/// <summary>One line of a run's events.tsv (t = simulation seconds; Cp is empty for call/stockout/stockin).</summary>
public sealed record SimEvent(double T, string Kind, string Material, string Cp, double Value);

public static class EventLog
{
    /// <summary>Reads events.tsv; a missing file is empty, a bad or cut-off line (a killed run) is skipped.</summary>
    public static IReadOnlyList<SimEvent> Read(string path)
    {
        var list = new List<SimEvent>();
        if (!File.Exists(path)) return list;
        var lines = File.ReadAllText(path).Split('\n');
        // the last element is empty after a final newline, else a line cut mid-number: dropped either way
        for (int i = 1; i < lines.Length - 1; i++)
        {
            var f = lines[i].TrimEnd('\r').Split('\t');
            if (f.Length == 5 && double.TryParse(f[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)
                && double.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                list.Add(new SimEvent(t, f[1], f[2], f[3], v));
        }
        return list;
    }
}
