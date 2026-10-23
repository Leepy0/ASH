using System.Collections.Generic;
using System.Linq;
using ASH.Core.Runner;
using Xunit;

namespace ASH.Tests.Runner;

public class SearchEstimateTests
{
    [Fact] public void Expected_Counts_Search_Decided_Minus1_Confirm_And_Impact()
    {
        var p = SearchStrategyTests.Input(("G1", 1, 4), ("G2", 1, 4), ("G3", 1, 3)); // Replications 2
        var c = new GroupClusters { Clusters = new IReadOnlyList<string>[] { new[] { "G1", "G2" }, new[] { "G3" } } };
        // G1,G2: 1·(1 + 2 + 2 + 2) + 1 speculative first midpoint (batch, initReps 1, k 2) + (2−1) + 2·2 = 13
        // G3:    1·(1 + 2 + 1) + (2−1) + 1·2 = 7;   confirm 2;   impact 3·2 = 6
        Assert.Equal(28, SearchEstimate.Expected(p, c, 1, true));
        Assert.Equal(22, SearchEstimate.Expected(p, c, 1, false));  // 영향표 없음: 13 + 7 + 2
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
        Assert.True(f.Calls <= n, $"{f.Calls} > {n}");
        Assert.Equal(new[] { "탐색 G1,G2", "확정 G1,G2", "탐색 G3", "확정 G3", "확인", "영향표" }, seen.Select(s => s.Phase));
        Assert.Equal(4, seen[1].Confirmed["G2"]); Assert.Equal(1, seen[1].GroupsLeft); Assert.Equal(n, seen[0].ExpectedRuns);
    }
    [Fact] public void Search_With_Batch_Executor_Stays_Within_Expected()
    {
        var (p, c, _) = SearchStrategyTests.Case();
        var b = new SearchStrategyTests.Batching(); b.Inner.Fail = SearchStrategyTests.ClusterRule;
        SearchStrategy.Search(p, b, c, new SearchOptions(ImpactTable: true));
        Assert.True(b.BatchSizes.Sum() <= SearchEstimate.Expected(p, c, 1, true));
    }
}
