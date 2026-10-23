using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Input;

namespace ASH.Core.Runner;

/// <summary>What SearchStrategy.Search reports at each cluster start/end, confirm and impact. Confirmed = groups whose cluster is done;
/// Current = the cluster being searched (counts at its start); GroupsLeft = groups in clusters not done yet; ExpectedRuns = SearchEstimate.Expected at the start.</summary>
public sealed record SearchProgress(string Phase, IReadOnlyDictionary<string, int> Confirmed, IReadOnlyDictionary<string, int> Current, int GroupsLeft, int ExpectedRuns);

public static class SearchEstimate
{
    /// <summary>Model runs the search is expected to take, following SearchStrategy.Search per cluster b with k_b groups:
    /// initReps × (baseline 1 + binary search ⌈log2(Max−Min+1)⌉ per group + one later cur−1 pass k_b)
    /// + k_b−1 stale speculative first midpoints (batch executor, initReps 1, k_b ≥ 2: SearchJob always uses ParallelExecutor)
    /// + decided (reps − initReps; the first initReps seeds are cached) + minus1 k_b × reps (all cached when reps = 1);
    /// then confirm reps and impact k × reps. Not a hard bound (extra later passes, confirm bumps): Raise lifts it once reached.</summary>
    public static int Expected(ProjectInput input, GroupClusters clusters, int initialReps, bool impact)
    {
        int reps = Math.Max(1, input.Settings.Replications), initReps = Math.Max(1, initialReps);
        if (string.Equals((input.Settings.InitStockMode ?? "").Trim(), "Fixed", StringComparison.OrdinalIgnoreCase)) reps = initReps = 1;
        var byName = input.Groups.ToDictionary(g => g.Group);
        static int Log2Ceil(int r) { int n = 0; while ((1 << n) < r) n++; return n; }
        int total = 0;
        foreach (var cluster in clusters.Clusters)
        {
            int k = cluster.Count;
            int search = cluster.Sum(g => Log2Ceil(byName[g].MaxCount - byName[g].MinCount + 1));
            total += initReps * (1 + search + k) + (initReps == 1 && k >= 2 ? k - 1 : 0) + (reps - initReps) + (reps > 1 ? k * reps : 0);
        }
        return total + reps + (impact ? input.Groups.Count * reps : 0);
    }

    /// <summary>Keeps the shown total ahead of the runs done once the estimate is reached.</summary>
    public static int Raise(int expected, int runsDone, int groupsLeft) => runsDone >= expected ? runsDone + Math.Max(1, groupsLeft) : expected;
}
