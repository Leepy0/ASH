using System;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Core.Asy;
using ASH.Core.Input;

namespace ASH.Core.Model;

public static class ParamsFile
{
    public static string Render(ProjectInput input, RunScenario s)
    {
        var sb = new StringBuilder();
        void L(string line) => sb.Append(line).Append('\n');
        string N(double d) => AsyWriter.Num(d);
        var st = input.Settings;

        foreach (var kv in s.ActiveByGroup)
            if (!input.Groups.Any(g => g.Group == kv.Key) || kv.Value < 0)
                throw new ArgumentException("bad scenario entry: " + kv.Key + "=" + kv.Value);

        L("SEED " + s.Seed);
        L("GROUPS " + input.Groups.Count);
        for (int i = 0; i < input.Groups.Count; i++)
        {
            var g = input.Groups[i];
            L((i + 1) + " " + (s.ActiveByGroup.TryGetValue(g.Group, out var a) ? a : g.MinCount));
        }

        var rng = new Random(s.Seed); // one draw per material in row order, so the stream is stable
        L("MATERIALS " + input.Materials.Count);
        for (int i = 0; i < input.Materials.Count; i++)
        {
            var m = input.Materials[i];
            double u = rng.NextDouble();
            double b = m.InitStock ?? (m.CallThreshold + m.UnloadThreshold);
            double init;
            switch ((st.InitStockMode ?? "").Trim().ToLowerInvariant())
            {
                case "uniform": init = b * (1 + (u * 2 - 1) * st.InitStockSpread / 100.0); break;
                // offset = ((index-1) mod 3) * Spread containers; index is the 1-based Excel row order
                case "staggered": init = b + (i % 3) * st.InitStockSpread; break;
                default: init = b; break; // Fixed
            }
            L((i + 1) + " " + N(m.CallThreshold) + " " + N(m.UnloadThreshold) + " " + N(init) + " " + N(m.SafetyStock ?? st.SafetyStock)
              + " " + N(m.TaktTime) + " " + N(m.UnitsPerCycle) + " " + N(m.UnitsPerContainer));
        }
        L("SIM " + N(st.SimLengthSec) + " " + N(st.WarmupSec) + " " + N(st.DeadlockSec));
        L("DEBUG " + (s.Debug ? 1 : 0));
        L("END");
        return sb.ToString();
    }

    public static void Write(ProjectInput input, RunScenario s, string path) =>
        File.WriteAllText(path, Render(input, s), new UTF8Encoding(false));
}
