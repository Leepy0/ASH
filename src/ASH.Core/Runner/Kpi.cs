using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Input;

namespace ASH.Core.Runner;

public sealed record RunKpi(string Group, int Active, double Utilization, double MinStockRatio, int Supplies, int Expected,
    double StopTimeSec, int Deadlocks);

public static class Kpi
{
    /// <summary>One KPI per group. Utilization = (delivering+retrieving time share summed over the type's vehicles) / active,
    /// since excess vehicles are parked and add nothing. Deadlocks is the model-wide count, repeated per group.</summary>
    public static IReadOnlyList<RunKpi> FromRun(ResultSet rs, VehicleStats vs, ProjectInput input)
    {
        var list = new List<RunKpi>();
        foreach (var g in input.Groups)
        {
            var active = rs.Groups.FirstOrDefault(x => x.Group == g.Group)?.ActiveCount ?? 0;
            double util = 0;
            if (active > 0 && vs.Types.TryGetValue(g.Type, out var ts))
                util = Math.Min(1.0, ts.Vehicles.Sum(v => v.DeliveringPct + v.RetrievingPct) / active);
            var mats = rs.Materials.Where(m => m.Group == g.Group).ToList();
            double ratio = double.PositiveInfinity;
            foreach (var m in mats)
            {
                var info = input.Materials.FirstOrDefault(x => x.Material == m.Name);
                var safety = info?.SafetyStock ?? input.Settings.SafetyStock;
                ratio = Math.Min(ratio, safety > 0 ? m.MinStock / safety : double.PositiveInfinity);
            }
            if (double.IsPositiveInfinity(ratio)) ratio = mats.Count == 0 ? 0 : 1e9; // no materials: nothing to fail; zero safety: always satisfied
            list.Add(new RunKpi(g.Group, active, util, ratio, mats.Sum(m => m.Supplies), (int)Math.Round(mats.Sum(m => m.Expected)),
                mats.Sum(m => m.StopTimeSec), rs.Deadlocks));
        }
        return list;
    }
}
