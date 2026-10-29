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
    /// <summary>Model runs SearchStrategy.Search starts with an executor of <paramref name="slots"/> slots, counting only runs not already in its cache.
    /// Per cluster b, with m_b = its groups whose range R = Max−Min+1 is above 1 (a group with R = 1 is never probed below Max):
    /// baseline initReps (first cluster only: a later cluster's baseline is the previous cluster's decided counts, or the same all-Max run)
    /// + initReps × Σ KaryRuns(R, p) (pass-1 search, at most; p = probes per round = the cluster's slot share / initReps, ⌈log2 R⌉ for p = 1)
    /// + initReps × (m_b − 1) (first later cur−1 pass, at most: the last group's cur−1 was its final pass-1 probe)
    /// + (reps − initReps) (decided: its first initReps seeds are the last search run)
    /// + m_b × (reps − initReps) (minus1, at most: its configs ran at initReps seeds in the last pass);
    /// confirm round 0 is the last decided state (cached; with slots ≥ 2 and several clusters searched at once it is new: + reps);
    /// impact k × reps (at most: groups at Max are skipped, a +1 config may be cached).
    /// An estimate, not a bound: extra later passes, minus1 successes, confirm bumps and shares growing as clusters finish add runs; Raise lifts it once reached.</summary>
    public static int Expected(ProjectInput input, GroupClusters clusters, int initialReps, bool impact, int slots = 1)
    {
        int reps = Math.Max(1, input.Settings.Replications), initReps = Math.Max(1, initialReps);
        if (SearchStrategy.FixedStock(input.Settings)) reps = initReps = 1;
        slots = Math.Max(1, slots);
        var byName = input.Groups.ToDictionary(g => g.Group);
        int total = initReps, extra = Math.Max(0, reps - initReps), k = clusters.Clusters.Count;
        bool together = slots > 1 && k > 1;
        for (int i = 0; i < k; i++)
        {
            var cluster = clusters.Clusters[i];
            int share = together ? Math.Max(1, slots / k + (i < slots % k ? 1 : 0)) : slots, p = Math.Max(1, share / initReps);
            var ranges = cluster.Select(g => byName[g].MaxCount - byName[g].MinCount + 1).ToList();
            int m = ranges.Count(r => r > 1), later = Math.Max(0, m - 1);
            total += initReps * (ranges.Sum(r => KaryRuns(r, p)) + later) + extra + m * extra;
        }
        return total + (together ? reps : 0) + (impact ? input.Groups.Count * reps : 0);
    }

    /// <summary>Probes the k-ary search of one group over range <paramref name="r"/> makes at most with <paramref name="p"/> probes per round
    /// (follows the widest sub-interval each round; p = 1 gives ⌈log2 r⌉).</summary>
    internal static int KaryRuns(int r, int p)
    {
        int runs = 0;
        for (int n = r - 1; n > 0;)
        {
            var pts = SearchStrategy.Points(0, n, p);
            runs += pts.Count;
            if (n <= p) break;
            int widest = Math.Max(pts[0], n - pts[pts.Count - 1] - 1);
            for (int j = 0; j + 1 < pts.Count; j++) widest = Math.Max(widest, pts[j + 1] - pts[j] - 1);
            n = widest;
        }
        return runs;
    }

    /// <summary>Keeps the shown total ahead of the runs done once the estimate is reached.</summary>
    public static int Raise(int expected, int runsDone, int groupsLeft) => runsDone >= expected ? runsDone + Math.Max(1, groupsLeft) : expected;
}
