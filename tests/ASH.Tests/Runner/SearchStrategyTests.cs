using System.Collections.Generic;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Model;
using ASH.Core.Runner;
using Xunit;

namespace ASH.Tests.Runner;

public class SearchStrategyTests
{
    internal sealed class Fake : IRunExecutor
    {
        public int Calls;
        readonly HashSet<string> keys = new();
        public System.Func<RunScenario, string, bool> Fail = (s, g) => false;
        public IReadOnlyList<RunKpi> Execute(RunScenario s)
        {
            Calls++;
            Assert.True(keys.Add(s.Seed + "|" + string.Join(",", s.ActiveByGroup.OrderBy(k => k.Key).Select(k => k.Value))), "duplicate run");
            // G2 utilization drops 0.01 per G1 vehicle (gives the impact table something to show)
            return s.ActiveByGroup.Select(kv => new RunKpi(kv.Key, kv.Value, kv.Key == "G2" ? 0.5 - 0.01 * s.ActiveByGroup["G1"] : 0.5, 2, 10, 10, 10,
                Fail(s, kv.Key) ? 10 : 0, 0)).ToList();
        }
    }
    internal static ProjectInput Input(params (string g, int min, int max)[] gs)
    {
        var p = new ProjectInput { Settings = new ASH.Core.Input.Settings { Replications = 2, SeedBase = 1 } };
        foreach (var (g, mn, mx) in gs) p.Groups.Add(new AgvGroup(g, "T", mn, mx, new(), "x"));
        return p;
    }
    // G1+G2 must reach 5 together; G3 needs 2 on its own
    internal static bool ClusterRule(RunScenario s, string g) =>
        g == "G3" ? s.ActiveByGroup["G3"] < 2 : s.ActiveByGroup["G1"] + s.ActiveByGroup["G2"] < 5;
    internal static (ProjectInput, GroupClusters, Fake) Case()
    {
        var p = Input(("G1", 1, 4), ("G2", 1, 4), ("G3", 1, 3));
        var c = new GroupClusters { Clusters = new IReadOnlyList<string>[] { new[] { "G1", "G2" }, new[] { "G3" } } };
        return (p, c, new Fake { Fail = ClusterRule });
    }

    static readonly SearchOptions WithImpact = new(ImpactTable: true);

    [Fact] public void Search_Finds_Min_Passing_Combination()
    {
        var (p, c, f) = Case();
        var r = SearchStrategy.Search(p, f, c, WithImpact);
        Assert.Equal((1, 4, 2), (r.Decided["G1"], r.Decided["G2"], r.Decided["G3"]));
        Assert.Empty(r.Warnings);
        System.Console.WriteLine($"RUNS min-passing (impact on) = {f.Calls}");
    }
    [Fact] public void Search_Cluster_Judgement_Differs_From_Independent()
    {
        // judged alone, G1 would sink to 1 with G2 left at 4, and G2 alone would sink to 1 with G1 at 4: (1,1) breaks the pair.
        var (p, c, f) = Case();
        var r = SearchStrategy.Search(p, f, c, new SearchOptions());
        Assert.Equal((1, 4), (r.Decided["G1"], r.Decided["G2"]));   // independent search would give (1,1)
    }
    [Fact] public void Search_Confirms_And_Bumps_Failing_Group()
    {
        var p = Input(("G3", 1, 4));
        var f = new Fake { Fail = (s, g) => s.ActiveByGroup["G3"] < (s.Seed == 1 ? 2 : 3) };   // second replication needs 3
        var r = SearchStrategy.Search(p, f, GroupClusters.Singletons(p), new SearchOptions());
        Assert.Equal(3, r.Decided["G3"]); Assert.Empty(r.Warnings);
        Assert.Contains(r.Trace, t => t.Phase == "confirm" && t.ClusterOk == false);
        System.Console.WriteLine($"RUNS bump = {f.Calls}");
    }
    [Fact] public void Search_Reports_Max_When_None_Pass()
    {
        var p = Input(("G1", 1, 3));
        var f = new Fake { Fail = (s, g) => true };
        var r = SearchStrategy.Search(p, f, GroupClusters.Singletons(p), new SearchOptions());
        Assert.Equal(3, r.Decided["G1"]); Assert.NotEmpty(r.Warnings);
        System.Console.WriteLine($"RUNS none-pass = {f.Calls}");
    }
    [Fact] public void Search_Impact_Rows_Present_Only_When_Asked()
    {
        var (p, c, f) = Case();
        var r = SearchStrategy.Search(p, f, c, WithImpact);
        var row = Assert.Single(r.Impact, x => x.Group == "G1" && x.OtherGroup == "G2");
        Assert.Equal(-0.01, row.UtilDelta, 6);
        var (p2, c2, f2) = Case();
        Assert.Empty(SearchStrategy.Search(p2, f2, c2, new SearchOptions()).Impact);   // off by default
        System.Console.WriteLine($"RUNS impact off = {f2.Calls}");
    }
    [Fact] public void Search_Caches_Runs_And_Keeps_The_Budget()
    {
        // G1+G2 (1..4, 2 reps): baseline 1 + G1 search 2 + G2 search 2 + cur−1 pass 0 (cached) + decided 1 + minus1 1 = 7;
        // G3 (1..3): baseline 0 (cached) + search 2 + decided 1 + minus1 1 = 4; confirm cached. Fake fails any run executed twice.
        var (p, c, f) = Case();
        SearchStrategy.Search(p, f, c, new SearchOptions());
        Assert.Equal(11, f.Calls);   // 13 before the budget trim
    }
    [Fact] public void Search_Later_Passes_Only_Step_Down_By_One()
    {
        var p = Input(("G1", 1, 8), ("G2", 1, 8));
        var c = new GroupClusters { Clusters = new IReadOnlyList<string>[] { new[] { "G1", "G2" } } };
        var f = new Fake { Fail = (s, g) => s.ActiveByGroup["G1"] + s.ActiveByGroup["G2"] < 5 };
        var r = SearchStrategy.Search(p, f, c, new SearchOptions());
        Assert.Equal((1, 4), (r.Decided["G1"], r.Decided["G2"]));
        // pass 1 is G1 8->1 (3 probes) and G2 8->4 (3 probes); pass 2 tries only G2 = 3
        var search = r.Trace.Where(t => t.Phase == "search").Select(t => (t.Scenario.ActiveByGroup["G1"], t.Scenario.ActiveByGroup["G2"])).ToList();
        Assert.Equal(new[] { (4, 8), (2, 8), (1, 8), (1, 4), (1, 2), (1, 3), (1, 3) }, search);
        System.Console.WriteLine($"RUNS step-down = {f.Calls}");
    }
    [Fact] public void Search_Infeasible_Group_Does_Not_Pin_Cluster_And_Is_Skipped_Later()
    {
        var p = Input(("G1", 1, 3), ("G2", 1, 3));
        var c = new GroupClusters { Clusters = new IReadOnlyList<string>[] { new[] { "G1", "G2" } } };
        var f = new Fake { Fail = (s, g) => g == "G1" || s.ActiveByGroup["G2"] < 2 };   // G1 never passes
        var r = SearchStrategy.Search(p, f, c, new SearchOptions());
        Assert.Equal((3, 2), (r.Decided["G1"], r.Decided["G2"]));
        Assert.Contains(r.Warnings, w => w.Contains("묶음 G1,G2: 그룹 G1는 최대 대수에서도 판정 실패"));
        Assert.DoesNotContain(r.Trace, t => t.Phase == "minus1" && t.Scenario.ActiveByGroup["G1"] != 3);   // no cur−1 tries for G1
        System.Console.WriteLine($"RUNS infeasible = {f.Calls}");
    }
    [Fact] public void Search_Fixed_Init_Stock_Runs_One_Replication()
    {
        var (p, c, f) = Case();
        p.Settings = p.Settings with { InitStockMode = "Fixed" };   // Replications 2
        var r = SearchStrategy.Search(p, f, c, new SearchOptions(InitialReplications: 2));
        Assert.Contains(r.Warnings, w => w.Contains("InitStockMode Fixed"));
        Assert.All(r.Trace, t => Assert.Equal(1, t.Replications));
        Assert.Equal((1, 4, 2), (r.Decided["G1"], r.Decided["G2"], r.Decided["G3"]));
    }
    [Fact] public void Impact_Steps_Are_Unjudged_And_Record_Seeds()
    {
        var (p, c, f) = Case();
        var r = SearchStrategy.Search(p, f, c, WithImpact);
        var s = Assert.Single(r.Trace, t => t.Phase == "impact" && t.Scenario.ActiveByGroup["G1"] == 2);
        Assert.Null(s.ClusterOk); Assert.Equal(new[] { 1, 2 }, s.Seeds); Assert.Equal(2, s.Replications);
    }
    [Fact] public void ImpactTsv_Writes_Header_And_Invariant_Rows()
    {
        var path = System.IO.Path.GetTempFileName();
        ImpactTsv.Write(path, new[] { new ImpactRow("G1", 2, "G2", -0.01, 0.5) });
        Assert.Equal("group\tplusOne\totherGroup\tutilDelta\tminStockDelta\nG1\t2\tG2\t-0.01\t0.5\n", System.IO.File.ReadAllText(path));
        System.IO.File.Delete(path);
    }
    internal sealed class Batching : IBatchRunExecutor
    {
        public readonly Fake Inner = new();
        public readonly List<int> BatchSizes = new();
        public readonly List<IReadOnlyList<RunScenario>> Batches = new();
        public int Slots { get; init; } = 1;
        public IReadOnlyList<RunKpi> Execute(RunScenario s) => Inner.Execute(s);
        public IReadOnlyList<IReadOnlyList<RunKpi>> ExecuteMany(IReadOnlyList<RunScenario> ss, System.Threading.CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); BatchSizes.Add(ss.Count); Batches.Add(ss.ToList()); return ss.Select(Inner.Execute).ToList(); }
    }
    [Fact] public void Search_Fans_Impact_Runs_Out_As_One_Batch()
    {
        var p = Input(("G1", 1, 3), ("G2", 1, 3));
        var b = new Batching();
        var seq = new Fake();
        var r = SearchStrategy.Search(p, b, GroupClusters.Singletons(p), WithImpact);
        SearchStrategy.Search(p, seq, GroupClusters.Singletons(p), WithImpact);
        Assert.Equal(seq.Calls, b.BatchSizes.Sum());   // same runs as the sequential path (singleton clusters: no speculation)
        Assert.True(b.BatchSizes.Max() >= 2, string.Join(",", b.BatchSizes));
        Assert.Equal(2, r.Impact.Count);
    }
    [Fact] public void Search_Stops_When_Cancelled()
    {
        var (p, c, _) = Case();
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<System.OperationCanceledException>(() => SearchStrategy.Search(p, new Batching(), c, new SearchOptions(), cts.Token));
    }

    [Fact] public void Confirm_Runs_Exactly_The_Given_Counts_Once_Per_Seed_And_Judges_Them()
    {
        var (p, _, f) = Case();
        var r = SearchStrategy.Confirm(p, f, new Dictionary<string, int> { ["G1"] = 2, ["G2"] = 2, ["G3"] = 3 });
        Assert.Equal(2, f.Calls);                                  // Replications = 2: seeds 1 and 2, nothing else
        Assert.Equal((2, 2, 3), (r.Decided["G1"], r.Decided["G2"], r.Decided["G3"]));
        var step = Assert.Single(r.Trace);
        Assert.Equal("confirm", step.Phase); Assert.Equal(new[] { 1, 2 }, step.Seeds);
        Assert.False(step.VerdictsByGroup["G1"].Ok);               // 2+2 < 5 breaks the pair rule
        Assert.True(step.VerdictsByGroup["G3"].Ok);
        Assert.Contains(r.Warnings, w => w.StartsWith("G1:") && w.Contains("2대"));
    }
    [Fact] public void Confirm_Rejects_Unknown_Missing_And_Out_Of_Range_Counts()
    {
        var (p, _, f) = Case();
        var ok = new Dictionary<string, int> { ["G1"] = 2, ["G2"] = 2, ["G3"] = 3 };
        Assert.Throws<System.ArgumentException>(() => SearchStrategy.Confirm(p, f, new Dictionary<string, int>(ok) { ["G9"] = 1 }));
        Assert.Throws<System.ArgumentException>(() => SearchStrategy.Confirm(p, f, new Dictionary<string, int>(ok) { ["G3"] = 4 }));
        Assert.Throws<System.ArgumentException>(() => SearchStrategy.Confirm(p, f, new Dictionary<string, int> { ["G1"] = 2 }));
        Assert.Equal(0, f.Calls);
    }
}
