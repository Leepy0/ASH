using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Input;

namespace ASH.Core.Runner;

public enum RuleCode { None, NoResult, StopTime, Stock, Utilization, Supply, Deadlock }

/// <summary>Rule = first failing rule (None when Ok). Reason of an Ok verdict carries any KPI data notes.</summary>
public sealed record Verdict(bool Ok, string Reason, RuleCode Rule = RuleCode.None);

public static class Judge
{
    static string F(double d, string f) => d.ToString(f, CultureInfo.InvariantCulture);

    /// <summary>Stop time first (any process stop means the stock ran out), then spec 8.2 order; replications are the same group's runs.
    /// (Named Evaluate: C# forbids a member named like its class.)</summary>
    public static Verdict Evaluate(IEnumerable<RunKpi> replications, Input.Settings s)
    {
        var r = replications.ToList();
        if (r.Count == 0) return new Verdict(false, "결과 없음", RuleCode.NoResult);
        if (r.Any(x => double.IsNaN(x.Utilization) || double.IsNaN(x.MinStockRatio))) return new Verdict(false, "KPI 결측(NaN)", RuleCode.NoResult);
        var stop = r.Average(x => x.StopTimeSec);
        if (stop > 0) return new Verdict(false, $"재고 부족(공정 정지 {F(stop, "0.#")}초)", RuleCode.StopTime);
        var stock = r.Average(x => x.MinStockRatio);
        if (stock < 1) return new Verdict(false, $"재고 부족: 평균 최소재고가 안전재고의 {F(stock, "0.##")}배", RuleCode.Stock);
        var util = r.Average(x => x.Utilization);
        if (util > s.UtilMax) return new Verdict(false, $"가동률 초과: {F(util, "P0")} > {F(s.UtilMax, "P0")}", RuleCode.Utilization);
        var sup = r.Average(x => (double)x.Supplies); var exp = r.Average(x => x.Expected);
        if (sup < 0.95 * exp) return new Verdict(false, $"공급 부족: {F(sup, "0.#")} < 기대 {F(exp, "0.#")}의 95%", RuleCode.Supply);
        var dead = r.Sum(x => x.Deadlocks);
        if (dead > 0) return new Verdict(false, $"교착 {dead}건", RuleCode.Deadlock);
        return new Verdict(true, string.Join("; ", r.Select(x => x.Note).Where(n => !string.IsNullOrEmpty(n)).Distinct()), RuleCode.None);
    }
}
