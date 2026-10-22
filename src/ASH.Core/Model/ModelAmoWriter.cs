using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Asy;
using ASH.Core.Geometry;

namespace ASH.Core.Model;

public static class ModelAmoWriter
{
    /// <summary>autorep: without it AM writes no .report. autoView appends the autoview token (default off: the run is headless). drawPos is in model units.</summary>
    public static string Render(string modelName, string units, double simLenHours, IEnumerable<string> moveSystems, Rect2 drawPos, string version = "12.6.1.19", bool autoView = false)
    {
        string N(double d) => AsyWriter.Num(d);
        var lines = new List<string>
        {
            "VERSION " + version, $"UNITS {units} Seconds", "RNSET 1",
            $"DRAWPOS minx {N(drawPos.Min.X)} maxx {N(drawPos.Max.X)} miny {N(drawPos.Min.Y)} maxy {N(drawPos.Max.Y)} minz -10 maxz 10 trx 0 try 0",
        };
        foreach (var s in moveSystems) { lines.Add("MOVESYS name " + s); lines.Add("\tSYSPOS endx 1"); }
        lines.Add($"PROCSYS name {modelName}~"); lines.Add("\tSYSPOS endx 1");
        lines.Add("CONTROL snaplen " + simLenHours.ToString("0.###############", CultureInfo.InvariantCulture) + " Hours counts 1 autorep" + (autoView ? " autoview" : ""));
        lines.Add("");
        return string.Join("\n", lines);
    }

    /// <summary>Film runs (guide 2.1): the CONTROL line(s) become up to three snaps so that only [fromSec, toSec] is recorded for AutoView.
    /// autorep stays on the last snap, so the .report is still written at the end; result.tsv is the logic's own (cumulative from warmup) counters.</summary>
    public static string WithRecording(string amo, double simLenSec, double fromSec, double toSec)
    {
        fromSec = System.Math.Max(0, System.Math.Min(fromSec, simLenSec)); toSec = System.Math.Max(fromSec, System.Math.Min(toSec, simLenSec));
        var snaps = new List<(double Sec, string Flags)> { (fromSec, ""), (toSec - fromSec, " autoview"), (simLenSec - toSec, "") }.Where(s => s.Sec > 1e-6).ToList();
        string H(double sec) => (sec / 3600).ToString("0.###############", CultureInfo.InvariantCulture);
        var control = string.Join("\n", snaps.Select((s, i) => "CONTROL snaplen " + H(s.Sec) + " Hours counts 1" + (i == snaps.Count - 1 ? " autorep" : "") + s.Flags));
        var m = System.Text.RegularExpressions.Regex.Match(amo, @"^CONTROL [^\n]*\n?", System.Text.RegularExpressions.RegexOptions.Multiline);
        if (!m.Success) throw new System.IO.InvalidDataException("model.amo has no CONTROL line");
        var rest = System.Text.RegularExpressions.Regex.Replace(amo.Substring(m.Index), @"^CONTROL [^\n]*\n?", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        return amo.Substring(0, m.Index) + control + "\n" + rest;
    }
}
