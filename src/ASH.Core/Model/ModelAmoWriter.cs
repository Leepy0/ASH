using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Asy;
using ASH.Core.Geometry;

namespace ASH.Core.Model;

public static class ModelAmoWriter
{
    /// <summary>autorep: without it AM writes no .report. No autoview (the run is headless). drawPos is in model units.</summary>
    public static string Render(string modelName, string units, double simLenHours, IEnumerable<string> moveSystems, Rect2 drawPos, string version = "12.6.1.19")
    {
        string N(double d) => AsyWriter.Num(d);
        var lines = new List<string>
        {
            "VERSION " + version, $"UNITS {units} Seconds", "RNSET 1",
            $"DRAWPOS minx {N(drawPos.Min.X)} maxx {N(drawPos.Max.X)} miny {N(drawPos.Min.Y)} maxy {N(drawPos.Max.Y)} minz -10 maxz 10 trx 0 try 0",
        };
        foreach (var s in moveSystems) { lines.Add("MOVESYS name " + s); lines.Add("\tSYSPOS endx 1"); }
        lines.Add($"PROCSYS name {modelName}~"); lines.Add("\tSYSPOS endx 1");
        lines.Add("CONTROL snaplen " + simLenHours.ToString("0.###############", CultureInfo.InvariantCulture) + " Hours counts 1 autorep");
        lines.Add("");
        return string.Join("\n", lines);
    }
}
