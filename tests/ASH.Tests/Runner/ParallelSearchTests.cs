using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using ASH.Core.Input;
using ASH.Core.Model;
using ASH.Core.Runner;
using Xunit;
using Batching = ASH.Tests.Runner.SearchStrategyTests.Batching;
using Fake = ASH.Tests.Runner.SearchStrategyTests.Fake;

namespace ASH.Tests.Runner;

/// <summary>k-ary rounds and clusters searched at once (SearchStrategy with a batch executor of several slots).</summary>
public class ParallelSearchTests
{
    /// <summary>A random monotone case: groups A..D in 1–3 clusters; a group fails below its own need or while its cluster's sum is below the cluster's need.</summary>
    static (ProjectInput, GroupClusters, Func<RunScenario, string, bool>) RandomCase(Random rnd)
    {
        var names = new[] { "A", "B", "C", "D" }.Take(rnd.Next(1, 5)).ToList();
        var p = SearchStrategyTests.Input(names.Select(n => { int mn = rnd.Next(1, 4); return (n, mn, mn + rnd.Next(0, 16)); }).ToArray());
        p.Settings = p.Settings with { Replications = rnd.Next(1, 3) };
        var clusterOf = names.ToDictionary(n => n, n => rnd.Next(0, 3));
        var c = new GroupClusters { Clusters = names.GroupBy(n => clusterOf[n]).Select(g => (IReadOnlyList<string>)g.ToList()).ToList() };
        var need = names.ToDictionary(n => n, n => rnd.Next(0, 20));
        var sumNeed = Enumerable.Range(0, 3).Select(_ => rnd.Next(0, 30)).ToArray();
        bool Fail(RunScenario s, string g) =>
            s.ActiveByGroup[g] < need[g] || names.Where(n => clusterOf[n] == clusterOf[g]).Sum(n => s.ActiveByGroup[n]) < sumNeed[clusterOf[g]];
        return (p, c, Fail);
    }

    [Fact] public void KAry_Decides_Exactly_Like_Binary_For_Monotone_Verdicts_And_Respects_The_Slots()
    {
        var rnd = new Random(7);
        for (int n = 0; n < 150; n++)
        {
            var (p, c, fail) = RandomCase(rnd);
            var seq = new Fake { Fail = fail };   // Fake asserts no scenario runs twice
            var expected = SearchStrategy.Search(p, seq, c, new SearchOptions()).Decided;
            for (int slots = 1; slots <= 10; slots++)
            {
                var b = new Batching { Slots = slots }; b.Inner.Fail = fail;
                var got = SearchStrategy.Search(p, b, c, new SearchOptions());
                Assert.Equal(expected, got.Decided);
                if (p.Settings.Replications == 1) Assert.All(b.BatchSizes, s => Assert.InRange(s, 1, Math.Max(slots, c.Clusters.Count)));
            }
        }
    }

    [Fact] public void Narrow_Takes_The_Lowest_Pass_Above_Every_Fail()
    {
        var pts = new[] { 2, 4, 6 };
        Assert.Equal((1, 2), SearchStrategy.Narrow(pts, new[] { true, true, true }, 1, 8));
        Assert.Equal((7, 8), SearchStrategy.Narrow(pts, new[] { false, false, false }, 1, 8));
        Assert.Equal((3, 4), SearchStrategy.Narrow(pts, new[] { false, true, true }, 1, 8));
        Assert.Equal((5, 6), SearchStrategy.Narrow(pts, new[] { true, false, true }, 1, 8));   // the pass at 2 below the fail at 4 is noise
        Assert.Equal(new[] { 3 }, SearchStrategy.Points(1, 5, 1));                               // one slot: the binary midpoint
        Assert.Equal(new[] { 1, 2, 3, 4 }, SearchStrategy.Points(1, 5, 4));                      // few left: all of them
        Assert.Equal(new[] { 3, 5, 7, 9 }, SearchStrategy.Points(1, 12, 4));
    }

    [Fact] public void Non_Monotone_Noise_Below_A_Fail_Is_Not_Taken()
    {
        var p = SearchStrategyTests.Input(("G1", 1, 10));
        p.Settings = p.Settings with { Replications = 1 };
        // passes from 6 up, plus a lucky pass at 2
        Func<RunScenario, string, bool> fail = (s, g) => s.ActiveByGroup["G1"] < 6 && s.ActiveByGroup["G1"] != 2;
        var b = new Batching { Slots = 9 }; b.Inner.Fail = fail;
        Assert.Equal(6, SearchStrategy.Search(p, b, GroupClusters.Singletons(p), new SearchOptions()).Decided["G1"]);
    }

    [Fact] public void Clusters_Search_At_Once_And_Share_The_Slots()
    {
        var p = SearchStrategyTests.Input(("G1", 1, 12), ("G3", 1, 12));
        p.Settings = p.Settings with { Replications = 1 };
        var c = new GroupClusters { Clusters = new IReadOnlyList<string>[] { new[] { "G1" }, new[] { "G3" } } };
        Func<RunScenario, string, bool> fail = (s, g) => s.ActiveByGroup[g] < (g == "G1" ? 4 : 9);
        var b = new Batching { Slots = 10 }; b.Inner.Fail = fail;
        var log = new List<string>();
        var r = SearchStrategy.Search(p, b, c, new SearchOptions(), default, null, log.Add);
        Assert.Equal((4, 9), (r.Decided["G1"], r.Decided["G3"]));
        Assert.Equal(1, b.BatchSizes[0]);   // one all-Max baseline serves both clusters
        var second = b.Batches[1];
        Assert.Equal(10, second.Count);     // 5 probes each
        Assert.Equal(5, second.Count(s => s.ActiveByGroup["G3"] == 12));   // G1 probes, G3 held at its start
        Assert.Equal(5, second.Count(s => s.ActiveByGroup["G1"] == 12));
        Assert.All(b.BatchSizes, s => Assert.InRange(s, 1, 10));
        Assert.Contains(log, l => l.StartsWith("탐색 라운드: ") && l.Contains("그룹 G1 [1..12] → ") && l.Contains(" · 그룹 G3 [1..12] → ") && l.EndsWith("(10회)"));
        var seq = new Fake { Fail = fail };
        SearchStrategy.Search(p, seq, c, new SearchOptions());
        Assert.True(b.BatchSizes.Count * 2 <= seq.Calls, $"{b.BatchSizes.Count} rounds vs {seq.Calls} sequential runs");
        System.Console.WriteLine($"ROUNDS 2 clusters P=10: {b.BatchSizes.Count} rounds / {b.BatchSizes.Sum()} runs, sequential {seq.Calls} runs");
    }

    [Fact] public void Replications_Of_A_Probe_Go_Out_In_The_Same_Batch()
    {
        var p = SearchStrategyTests.Input(("G1", 1, 12));   // Replications 2
        var b = new Batching { Slots = 6 }; b.Inner.Fail = (s, g) => s.ActiveByGroup["G1"] < 5;
        var r = SearchStrategy.Search(p, b, GroupClusters.Singletons(p), new SearchOptions(InitialReplications: 2));
        Assert.Equal(5, r.Decided["G1"]);
        Assert.Equal(6, b.BatchSizes[1]);   // 3 probes × 2 seeds
        Assert.Equal(3, b.Batches[1].Select(s => s.ActiveByGroup["G1"]).Distinct().Count());
    }

    [Fact] public void Expected_Stays_Near_The_Actual_Runs_For_Every_Slot_Count()
    {
        var rnd = new Random(11);
        for (int n = 0; n < 60; n++)
        {
            var (p, c, fail) = RandomCase(rnd);
            for (int slots = 1; slots <= 10; slots++)
            {
                var b = new Batching { Slots = slots }; b.Inner.Fail = fail;
                SearchStrategy.Search(p, b, c, new SearchOptions());
                int actual = b.BatchSizes.Sum(), e = SearchEstimate.Expected(p, c, 1, false, slots);
                // pass 1 is budgeted at its worst case; later passes, minus1 successes, bumps and growing shares are not
                Assert.InRange(actual, e / 3, e + 6 * c.Clusters.Count);
            }
        }
        for (int r = 1; r <= 100; r++)
        {
            int log2 = 0; while ((1 << log2) < r) log2++;
            Assert.Equal(log2, SearchEstimate.KaryRuns(r, 1));
        }
    }

    [Fact] public void Wall_Time_Drops_With_Slots()
    {
        var p = SearchStrategyTests.Input(("G1", 1, 16), ("G2", 1, 16), ("G3", 1, 16));
        p.Settings = p.Settings with { Replications = 1 };
        var c = new GroupClusters { Clusters = new IReadOnlyList<string>[] { new[] { "G1", "G2" }, new[] { "G3" } } };
        IReadOnlyList<RunKpi> Run(int slot, RunScenario s)
        {
            Thread.Sleep(50);
            bool Fail(string g) => g == "G3" ? s.ActiveByGroup["G3"] < 7 : s.ActiveByGroup["G1"] + s.ActiveByGroup["G2"] < 11;
            return s.ActiveByGroup.Select(kv => new RunKpi(kv.Key, kv.Value, 0.5, 2, 10, 10, 10, Fail(kv.Key) ? 10 : 0, 0)).ToList();
        }
        (TimeSpan, IReadOnlyDictionary<string, int>, int) Time(int slots)
        {
            var exec = new ParallelExecutor(slots, Run);
            var sw = Stopwatch.StartNew();
            var r = SearchStrategy.Search(p, exec, c, new SearchOptions());
            return (sw.Elapsed, r.Decided, exec.Runs);
        }
        var (t1, d1, n1) = Time(1);
        var (t8, d8, n8) = Time(8);
        Assert.Equal(d1, d8);
        Assert.True(t8.TotalMilliseconds < t1.TotalMilliseconds * 0.6, $"1 slot {t1.TotalMilliseconds:0} ms ({n1} runs), 8 slots {t8.TotalMilliseconds:0} ms ({n8} runs)");
        System.Console.WriteLine($"WALL 1 slot {t1.TotalMilliseconds:0} ms ({n1} runs), 8 slots {t8.TotalMilliseconds:0} ms ({n8} runs)");
    }
}
