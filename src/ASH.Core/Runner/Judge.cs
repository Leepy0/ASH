using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Input;

namespace ASH.Core.Runner;

public enum RuleCode { None, NoResult, StopTime, Stock, Utilization, Supply, Deadlock, Follow }

/// <summary>Rule = first failing rule (None when Ok). Reason of an Ok verdict carries any KPI data notes.</summary>
public sealed record Verdict(bool Ok, string Reason, RuleCode Rule = RuleCode.None);

public static class Judge
{
    /// <summary>Start of the reason of a utilization verdict (summary.tsv keeps the reason, not the rule).</summary>
    public const string UtilPrefix = "가동률 초과";

    static string F(double d, string f) => d.ToString(f, CultureInfo.InvariantCulture);

    /// <summary>Stop time first (any process stop means the stock ran out), then supply and deadlock, utilization last; replications are the same group's runs.
    /// A minimum stock under the safety stock is only a note on an Ok verdict. A cycle-supply group has its own rules (<see cref="Cycle"/>).
    /// (Named Evaluate: C# forbids a member named like its class.)</summary>
    public static Verdict Evaluate(IEnumerable<RunKpi> replications, Input.Settings s)
    {
        var r = replications.ToList();
        if (r.Count == 0) return new Verdict(false, "결과 없음", RuleCode.NoResult);
        if (r.Any(x => double.IsNaN(x.Utilization) || double.IsNaN(x.MinStockRatio))) return new Verdict(false, "KPI 결측(NaN)", RuleCode.NoResult);
        if (r[0].Cycle) return Cycle(r, s);
        var stop = r.Average(x => x.StopTimeSec);
        if (stop > 0) return new Verdict(false, $"재고 부족(공정 정지 {F(stop, "0.#")}초)", RuleCode.StopTime);
        // dipping under the safety stock without a process stop is a warning only (user decision 2026-10-22): the stop rule above catches real shortages
        var stock = r.Average(x => x.MinStockRatio);
        string? stockNote = stock < 1 ? $"주의: 평균 최소재고가 안전재고의 {F(stock, "0.##")}배" : null;
        // production attainment, not deliveries: deliveries lag consumption while the initial stock is drawn down
        var con = r.Average(x => x.Consumed); var exp = r.Average(x => x.Expected);
        if (con < 0.95 * exp) return new Verdict(false, $"생산 미달: 소비 {F(con, "0.#")} < 이론 {F(exp, "0.#")}의 95%", RuleCode.Supply);
        var dead = r.Sum(x => x.Deadlocks);
        if (dead > 0) return new Verdict(false, $"교착 {dead}건", RuleCode.Deadlock);
        // last, so a utilization verdict means every other rule passed: the pages show it as a warning, the search still adds vehicles for it
        var util = r.Average(x => x.Utilization);
        if (util > s.UtilMax) return new Verdict(false, $"{UtilPrefix}: {F(util, "P0")} > {F(s.UtilMax, "P0")}", RuleCode.Utilization);
        return new Verdict(true, string.Join("; ", new[] { stockNote }.Concat(r.Select(x => x.Note)).Where(n => !string.IsNullOrEmpty(n)).Distinct()), RuleCode.None);
    }

    /// <summary>A cycle-supply group: deadlock, then the follow rate (replications without swaps left out of the average). Vehicles circulate
    /// whether needed or not, so utilization, production and stock are not judged; a process stop is a note on an Ok verdict.</summary>
    static Verdict Cycle(List<RunKpi> r, Input.Settings s)
    {
        var dead = r.Sum(x => x.Deadlocks);
        if (dead > 0) return new Verdict(false, $"교착 {dead}건", RuleCode.Deadlock);
        var rates = r.Select(x => x.FollowRate).Where(x => !double.IsNaN(x)).ToList();
        if (rates.Count == 0) return new Verdict(false, "교체 없음", RuleCode.Follow);
        var rate = rates.Average();
        // "90%" without the space invariant P0 puts in
        if (rate < s.CycleFollowRate) return new Verdict(false, $"뒤차 대기 {F(rate * 100, "0")}% < {F(s.CycleFollowRate * 100, "0")}%", RuleCode.Follow);
        var stop = r.Average(x => x.StopTimeSec);
        string? stopNote = stop > 0 ? $"주의: 공정 정지 {F(stop, "0.#")}초" : null;
        return new Verdict(true, string.Join("; ", new[] { stopNote }.Concat(r.Select(x => x.Note)).Where(n => !string.IsNullOrEmpty(n)).Distinct()), RuleCode.None);
    }
}
