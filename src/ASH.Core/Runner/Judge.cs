using System.Collections.Generic;
using System.Linq;
using ASH.Core.Input;

namespace ASH.Core.Runner;

public sealed record Verdict(bool Ok, string Reason);

public static class Judge
{
    /// <summary>Rules in spec 8.2 order; replications are the same group's runs. Reason names the first failing rule. (Named Evaluate: C# forbids a member named like its class.)</summary>
    public static Verdict Evaluate(IEnumerable<RunKpi> replications, Input.Settings s)
    {
        var r = replications.ToList();
        if (r.Count == 0) return new Verdict(false, "결과 없음");
        var stock = r.Average(x => x.MinStockRatio);
        if (stock < 1) return new Verdict(false, $"재고 부족: 평균 최소재고가 안전재고의 {stock:0.##}배");
        var util = r.Average(x => x.Utilization);
        if (util > s.UtilMax) return new Verdict(false, $"가동률 초과: {util:P0} > {s.UtilMax:P0}");
        var sup = r.Average(x => (double)x.Supplies); var exp = r.Average(x => (double)x.Expected);
        if (sup < 0.95 * exp) return new Verdict(false, $"공급 부족: {sup:0.#} < 기대 {exp:0.#}의 95%");
        var dead = r.Sum(x => x.Deadlocks);
        if (dead > 0) return new Verdict(false, $"교착 {dead}건");
        return new Verdict(true, "");
    }
}
