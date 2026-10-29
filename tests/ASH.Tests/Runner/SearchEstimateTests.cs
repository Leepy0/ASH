using System.Collections.Generic;
using System.Linq;
using ASH.Core.Runner;
using Xunit;

namespace ASH.Tests.Runner;

public class SearchEstimateTests
{
    [Fact] public void Expected_Counts_Only_Uncached_Runs()
    {
        var p = SearchStrategyTests.Input(("G1", 1, 4), ("G2", 1, 4), ("G3", 1, 3)); // Replications 2
        var c = new GroupClusters { Clusters = new IReadOnlyList<string>[] { new[] { "G1", "G2" }, new[] { "G3" } } };
        // baseline 1 (first cluster only)
        // G1,G2: search 1·(2+2) + later pass 1·(2−1) + decided (2−1) + minus1 2·(2−1) = 8
        // G3:    search 1·2 + later 0 + decided 1 + minus1 1 = 4;   confirm 0 (cached);   impact 3·2 = 6
        Assert.Equal(19, SearchEstimate.Expected(p, c, 1, true));
        Assert.Equal(13, SearchEstimate.Expected(p, c, 1, false));  // 영향표 없음: 1 + 8 + 4
        // 4 slots, both clusters at once (2 each: 2 probes per round): G1,G2 range 4 takes 2 probes then 1 (3 instead of 2) each: + 2;
        // G3 range 3 takes its 2 probes in one round (same); confirm round 0 is new: + 2
        Assert.Equal(13 + 2 + 2, SearchEstimate.Expected(p, c, 1, false, 4));
    }
    [Fact] public void Expected_Tracks_The_Actual_Runs_On_Both_Executors()
    {
        foreach (var impact in new[] { false, true })
        {
            var (p, c, f) = SearchStrategyTests.Case();
            SearchStrategy.Search(p, f, c, new SearchOptions(ImpactTable: impact));
            var b = new SearchStrategyTests.Batching(); b.Inner.Fail = SearchStrategyTests.ClusterRule;
            SearchStrategy.Search(p, b, c, new SearchOptions(ImpactTable: impact));
            // impact adds 2 new runs (G2 is at Max; G3+1 = (1,4,3) is cluster 1's decided config): 11→13; one slot batches the same runs
            Assert.Equal(impact ? (13, 13) : (11, 11), (f.Calls, b.BatchSizes.Sum()));
            int n = SearchEstimate.Expected(p, c, 1, impact);
            // impact is budgeted at k·reps = 6 (at most) against 2 actual, so only the search part is held to +3
            int slack = impact ? 3 + 6 - 2 : 3;
            foreach (var actual in new[] { f.Calls, b.BatchSizes.Sum() })
                Assert.InRange(n, actual, actual + slack);
        }
    }
    [Fact] public void Expected_Single_Group_With_No_Range_Is_Baseline_Plus_Decided()
    {
        var p = SearchStrategyTests.Input(("G1", 2, 2));   // Replications 2: baseline 1 + decided 1; no search, no minus1, confirm cached
        var f = new SearchStrategyTests.Fake();
        SearchStrategy.Search(p, f, GroupClusters.Singletons(p), new SearchOptions());
        Assert.Equal(2, SearchEstimate.Expected(p, GroupClusters.Singletons(p), 1, false));
        Assert.Equal(2, f.Calls);
    }
    [Fact] public void Expected_With_Initial_Reps_At_Full_Has_No_Decided_Or_Minus1_Runs()
    {
        var (p, c, f) = SearchStrategyTests.Case();   // Replications 2, InitialReps 2: decided and minus1 are all cached, no speculation
        SearchStrategy.Search(p, f, c, new SearchOptions(InitialReplications: 2));
        // baseline 2 + G1,G2 2·(4 + 1) + G3 2·(2 + 0) = 16
        Assert.Equal(16, SearchEstimate.Expected(p, c, 2, false));
        Assert.InRange(16, f.Calls, f.Calls + 3);
    }
    [Fact] public void Raise_Lifts_Expected_Only_When_Reached()
    {
        Assert.Equal(27, SearchEstimate.Raise(27, 20, 3));
        Assert.Equal(30, SearchEstimate.Raise(27, 27, 3));
        Assert.Equal(31, SearchEstimate.Raise(27, 30, 0));
    }
    [Fact] public void Search_Reports_Progress_And_Never_Exceeds_Expected()
    {
        var (p, c, f) = SearchStrategyTests.Case();
        var seen = new List<SearchProgress>();
        var n = SearchEstimate.Expected(p, c, 1, true);
        SearchStrategy.Search(p, f, c, new SearchOptions(ImpactTable: true, ExpectedRuns: n), default, seen.Add);
        Assert.Equal(new[] { "탐색 G1,G2", "확정 G1,G2", "탐색 G3", "확정 G3", "확인", "영향표" }, seen.Select(s => s.Phase));
        Assert.Equal(4, seen[1].Confirmed["G2"]); Assert.Equal(1, seen[1].GroupsLeft); Assert.Equal(n, seen[0].ExpectedRuns);
    }
}
