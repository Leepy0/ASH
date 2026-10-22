using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Input;

namespace ASH.Core.Runner;

/// <summary>Note = data problems the numbers hide (missing report/result row); null when clean. Supplies (deliveries) is reported only;
/// Consumed (containers consumed after warmup) against Expected is what Judge checks.</summary>
public sealed record RunKpi(string Group, int Active, double Utilization, double MinStockRatio, int Supplies, double Consumed, double Expected,
    double StopTimeSec, int Deadlocks, string? Note = null);

public static class Kpi
{
    const double Pass = 1e9;

    /// <summary>One KPI per group. Utilization = (delivering+retrieving time share summed over the type's vehicles) / active,
    /// since excess vehicles are parked and add nothing; NaN when the report is missing or lacks the type. Utilization comes from the AM
    /// .report, which covers the whole run including warmup (AM has no statistics reset from logic), so it is diluted by the warmup share
    /// when the warmup is quieter or busier than the rest. MinStockRatio = min over the
    /// group's input materials of minStock/safety (a material without a result row counts 0; no materials, or zero safety stock with
    /// no stop time, pass). Deadlocks is the model-wide count, repeated per group.</summary>
    public static IReadOnlyList<RunKpi> FromRun(ResultSet rs, ReportStats? report, ProjectInput input)
    {
        var list = new List<RunKpi>();
        foreach (var g in input.Groups)
        {
            var notes = new List<string>();
            var active = rs.Groups.FirstOrDefault(x => x.Group == g.Group)?.ActiveCount ?? 0;
            double util = double.NaN;
            // the logic's own busy time (pickup run + job until Finish CP / free-run return) for the active vehicles; the AutoMod report only for older models
            var veh = rs.Vehicles?.Where(v => v.Group == g.Group && v.Index <= active).ToList();
            if (active == 0) util = 0;
            else if (veh != null && veh.Count > 0 && veh[0].WindowSec > 0) util = Math.Min(1.0, veh.Sum(v => v.BusySec) / (active * veh[0].WindowSec));
            else if (report == null) notes.Add("보고서 없음: 가동률 미산출");
            // the AGVS type is named after the group; models built before 0.15 named it after the AGV_Types row
            else if (!report.Types.TryGetValue(g.Group, out var ts) && !report.Types.TryGetValue(g.Type, out ts)) notes.Add($"보고서에 {g.Group} 통계 없음: 가동률 미산출");
            else util = Math.Min(1.0, ts.Vehicles.Sum(v => v.DeliveringPct + v.RetrievingPct) / active);

            double ratio = Pass, supplies = 0, consumed = 0, expected = 0, stop = 0;
            foreach (var mi in input.Materials.Where(x => x.Group == g.Group))
            {
                var m = rs.Materials.FirstOrDefault(x => x.Name == mi.Material && x.Group == g.Group);
                if (m == null) { ratio = 0; notes.Add($"자재 {mi.Material} 결과 행 없음"); continue; }
                var safety = mi.SafetyStock ?? input.Settings.SafetyStock;
                var rr = safety > 0 ? m.MinStock / safety : (m.MinStock >= 0 && m.StopTimeSec == 0 ? Pass : 0);
                ratio = Math.Min(ratio, rr);
                supplies += m.Supplies; consumed += m.Consumed; expected += m.Expected; stop += m.StopTimeSec;
                // a stock that only just stays above the safety level on average is a warning, not a failure
                var st = rs.MaterialStats?.FirstOrDefault(x => x.Name == mi.Material && x.Group == g.Group);
                if (st != null && st.StockStd > 0 && st.StockAvg - 2 * st.StockStd < safety)
                    notes.Add($"자재 {mi.Material} 재고 변동 큼(평균 {st.StockAvg:0.##} - 2σ {2 * st.StockStd:0.##} < 안전재고 {safety:0.##})");
            }
            list.Add(new RunKpi(g.Group, active, util, ratio, (int)supplies, consumed, expected, stop, rs.Deadlocks,
                notes.Count == 0 ? null : string.Join("; ", notes)));
        }
        return list;
    }
}
