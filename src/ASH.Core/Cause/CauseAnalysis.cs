using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Cause;

/// <summary>All analysers together, ordered by kind (CauseKinds.Order) then severity.</summary>
public static class CauseAnalysis
{
    /// <summary>Layout-only findings (no run needed): the plugin's AGVCHECK shows these too.</summary>
    public static IReadOnlyList<CauseItem> Static(CauseContext c) =>
        Sort(DeadlockRisk.Find(c).Concat(ClearanceCauses.Find(c)).Concat(BlockHoldCauses.Find(c with { Result = null, Report = null })).Concat(DetourCauses.Find(c)));

    /// <summary>Everything, for a run (c.Result set); layout findings included.</summary>
    public static IReadOnlyList<CauseItem> All(CauseContext c) =>
        Sort(DeadlockCauses.Find(c).Concat(DeadlockRisk.Find(c)).Concat(ClearanceCauses.Find(c)).Concat(BlockHoldCauses.Find(c)).Concat(DetourCauses.Find(c))
            .Concat(LoadShareCauses.Find(c)).Concat(TimeLossCauses.Find(c)));

    static IReadOnlyList<CauseItem> Sort(IEnumerable<CauseItem> xs) =>
        xs.OrderBy(x => System.Array.IndexOf(CauseKinds.Order, x.Kind)).ThenByDescending(x => x.Severity).ToList();
}
