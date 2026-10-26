using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ASH.Core.Cause;

/// <summary>Materials whose share of their group's driving is far above their share of the demand.</summary>
public static class LoadShareCauses
{
    const double MinFactor = 2, MinGap = 0.20, SevereGap = 0.40; // driving share / demand share; shares are fractions

    public static IEnumerable<CauseItem> Find(CauseContext c)
    {
        if (c.Result is not { } r) yield break;
        // value7 of the material row = distance; models built before it have none (0 or missing) and give no findings
        var dist = new Dictionary<string, double>();
        foreach (var row in r.Rows.Where(x => x.Kind == "material" && x.Values.Length > 6 && x.Values[6] > 0)) dist[row.Name] = row.Values[6];
        IReadOnlyList<(string Material, CauseItem Item)>? detours = null;
        foreach (var g in r.Materials.GroupBy(m => m.Group))
        {
            var ms = g.Where(m => dist.ContainsKey(m.Name)).ToList();
            double drive = ms.Sum(m => dist[m.Name]), demand = ms.Sum(m => m.Expected);
            bool bySupplies = !(demand > 0);
            if (bySupplies) demand = ms.Sum(m => m.Supplies);
            if (!(drive > 0) || !(demand > 0)) continue;
            foreach (var m in ms)
            {
                double dr = dist[m.Name] / drive, de = (bySupplies ? m.Supplies : m.Expected) / demand;
                if (!(dr >= MinFactor * de && dr - de >= MinGap)) continue;
                detours ??= DetourCauses.ByMaterial(c);
                var leg = detours.Where(x => x.Material == m.Name).Select(x => x.Item).FirstOrDefault();
                yield return new CauseItem(CauseKinds.LoadShare, dr - de >= SevereGap ? 2 : 1, $"{m.Name} 주행 {Pct(dr)}%", g.Key,
                    $"수요 {Pct(de)}% · 주행 {Pct(dr)}%", leg == null ? "" : $"{leg.Where} 우회 확인");
            }
        }
    }

    static string Pct(double f) => Math.Round(f * 100).ToString("0", CultureInfo.InvariantCulture);
}
