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
    /// <summary>Model runs SearchStrategy.Search starts, counting only runs not already in its cache. Per cluster b, with m_b = its groups whose
    /// range R = Max−Min+1 is above 1 (a group with R = 1 is never probed below Max):
    /// baseline initReps (first cluster only: a later cluster's baseline is the previous cluster's decided counts)
    /// + initReps × Σ⌈log2 R⌉ (pass-1 binary search, at most)
    /// + initReps × (m_b − 1) (first later cur−1 pass, at most: the last group's cur−1 was its final binary-search probe)
    /// + (m_b − 1) when initReps is 1 (stale speculative first midpoints, at most and outcome-dependent: SearchJob always uses the batch ParallelExecutor)
    /// + (reps − initReps) (decided: its first initReps seeds are the last search run)
    /// + m_b × (reps − initReps) (minus1, at most: its configs ran at initReps seeds in the last pass);
    /// confirm round 0 is the last decided state (cached); impact k × reps (at most: groups at Max are skipped, a +1 config may be cached).
    /// An estimate, not a bound: extra later passes, minus1 successes and confirm bumps add runs; Raise lifts it once reached.</summary>
    public static int Expected(ProjectInput input, GroupClusters clusters, int initialReps, bool impact)
    {
        int reps = Math.Max(1, input.Settings.Replications), initReps = Math.Max(1, initialReps);
        if (SearchStrategy.FixedStock(input.Settings)) reps = initReps = 1;
        var byName = input.Groups.ToDictionary(g => g.Group);
        static int Log2Ceil(int r) { int n = 0; while ((1 << n) < r) n++; return n; }
        int total = initReps, extra = Math.Max(0, reps - initReps);
        foreach (var cluster in clusters.Clusters)
        {
            var ranges = cluster.Select(g => byName[g].MaxCount - byName[g].MinCount + 1).ToList();
            int m = ranges.Count(r => r > 1), later = Math.Max(0, m - 1);
            total += initReps * (ranges.Sum(Log2Ceil) + later) + (initReps == 1 ? later : 0) + extra + m * extra;
        }
        return total + (impact ? input.Groups.Count * reps : 0);
    }

    /// <summary>Keeps the shown total ahead of the runs done once the estimate is reached.</summary>
    public static int Raise(int expected, int runsDone, int groupsLeft) => runsDone >= expected ? runsDone + Math.Max(1, groupsLeft) : expected;
}
