using System;
using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Cause;

/// <summary>All analysers together, ordered by kind (CauseKinds.Order) then severity. An analyser that throws on unusual data is left out (and reported
/// through <see cref="Failed"/>) instead of taking the whole page down with it.</summary>
public static class CauseAnalysis
{
    /// <summary>(analyser, exception) of an analyser that threw; the server logs it.</summary>
    public static event Action<string, Exception>? Failed;

    /// <summary>Layout-only findings (no run needed): the plugin's AGVCHECK shows these too.</summary>
    public static IReadOnlyList<CauseItem> Static(CauseContext c) =>
        Sort(Safe("교착 위험", () => DeadlockRisk.Find(c)).Concat(Safe("간격", () => ClearanceCauses.Find(c)))
            .Concat(Safe("block 점유", () => BlockHoldCauses.Find(c with { Result = null, Report = null }))).Concat(Safe("우회", () => DetourCauses.Find(c))));

    /// <summary>Everything, for a run (c.Result set); layout findings included.</summary>
    public static IReadOnlyList<CauseItem> All(CauseContext c) =>
        Sort(Safe("교착", () => DeadlockCauses.Find(c)).Concat(Safe("교착 위험", () => DeadlockRisk.Find(c))).Concat(Safe("간격", () => ClearanceCauses.Find(c)))
            .Concat(Safe("block 점유", () => BlockHoldCauses.Find(c))).Concat(Safe("우회", () => DetourCauses.Find(c)))
            .Concat(Safe("작업 편중", () => LoadShareCauses.Find(c))).Concat(Safe("시간 손실", () => TimeLossCauses.Find(c))).Concat(Safe("자재 품절", () => StockoutCauses.Find(c))));

    // materialized here: the analysers are lazy, so an exception would otherwise surface later, while the page is being written
    static List<CauseItem> Safe(string name, Func<IEnumerable<CauseItem>> find)
    {
        try { return find().ToList(); }
        catch (Exception e) when (!(e is OutOfMemoryException)) { Failed?.Invoke(name, e); return new List<CauseItem>(); }
    }

    static IReadOnlyList<CauseItem> Sort(IEnumerable<CauseItem> xs) =>
        xs.OrderBy(x => Array.IndexOf(CauseKinds.Order, x.Kind)).ThenByDescending(x => x.Severity).ToList();
}
