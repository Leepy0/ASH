using System.Collections.Generic;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Model;
using ASH.Core.Runner;
using Xunit;

namespace ASH.Tests.Runner;

public class SearchStrategyTests
{
    sealed class Fake : IRunExecutor
    {
        public int Calls;
        readonly HashSet<string> keys = new();
        public System.Func<RunScenario, string, bool> Fail = (s, g) => false;
        public IReadOnlyList<RunKpi> Execute(RunScenario s)
        {
            Calls++;
            Assert.True(keys.Add(s.Seed + "|" + string.Join(",", s.ActiveByGroup.OrderBy(k => k.Key).Select(k => k.Value))), "duplicate run");
            // G2 utilization drops 0.01 per G1 vehicle (gives the impact table something to show)
            return s.ActiveByGroup.Select(kv => new RunKpi(kv.Key, kv.Value, kv.Key == "G2" ? 0.5 - 0.01 * s.ActiveByGroup["G1"] : 0.5, 2, 10, 10,
                Fail(s, kv.Key) ? 10 : 0, 0)).ToList();
        }
    }
    static ProjectInput Input(params (string g, int min, int max)[] gs)
    {
        var p = new ProjectInput { Settings = new ASH.Core.Input.Settings { Replications = 2, SeedBase = 1 } };
        foreach (var (g, mn, mx) in gs) p.Groups.Add(new AgvGroup(g, "T", mn, mx, new(), "x"));
        return p;
    }
    // G1+G2 must reach 5 together; G3 needs 2 on its own
    static bool ClusterRule(RunScenario s, string g) =>
        g == "G3" ? s.ActiveByGroup["G3"] < 2 : s.ActiveByGroup["G1"] + s.ActiveByGroup["G2"] < 5;
    static (ProjectInput, GroupClusters, Fake) Case()
    {
        var p = Input(("G1", 1, 4), ("G2", 1, 4), ("G3", 1, 3));
        var c = new GroupClusters { Clusters = new IReadOnlyList<string>[] { new[] { "G1", "G2" }, new[] { "G3" } } };
        return (p, c, new Fake { Fail = ClusterRule });
    }

    [Fact] public void Search_Finds_Min_Passing_Combination()
    {
        var (p, c, f) = Case();
        var r = SearchStrategy.Search(p, f, c, new SearchOptions());
        Assert.Equal(5, r.Decided["G1"] + r.Decided["G2"]); Assert.Equal(2, r.Decided["G3"]);
        Assert.Empty(r.Warnings);
        System.Console.WriteLine($"RUNS min-passing (impact on) = {f.Calls}");
    }
    [Fact] public void Search_Cluster_Judgement_Differs_From_Independent()
    {
        // judged alone, G1 would sink to 1 with G2 left at 4, and G2 alone would sink to 1 with G1 at 4: (1,1) breaks the pair.
        var (p, c, f) = Case();
        var r = SearchStrategy.Search(p, f, c, new SearchOptions());
        Assert.True(r.Decided["G1"] + r.Decided["G2"] >= 5);
        Assert.NotEqual(1, r.Decided["G2"]);
    }
    [Fact] public void Search_Confirms_And_Bumps_Failing_Group()
    {
        var p = Input(("G3", 1, 4));
        var f = new Fake { Fail = (s, g) => s.ActiveByGroup["G3"] < (s.Seed == 1 ? 2 : 3) };   // second replication needs 3
        var r = SearchStrategy.Search(p, f, GroupClusters.Singletons(p), new SearchOptions(ImpactTable: false));
        Assert.Equal(3, r.Decided["G3"]); Assert.Empty(r.Warnings);
        Assert.Contains(r.Trace, t => t.Phase == "confirm" && !t.ClusterOk);
        System.Console.WriteLine($"RUNS bump = {f.Calls}");
    }
    [Fact] public void Search_Reports_Max_When_None_Pass()
    {
        var p = Input(("G1", 1, 3));
        var f = new Fake { Fail = (s, g) => true };
        var r = SearchStrategy.Search(p, f, GroupClusters.Singletons(p), new SearchOptions(ImpactTable: false));
        Assert.Equal(3, r.Decided["G1"]); Assert.NotEmpty(r.Warnings);
        System.Console.WriteLine($"RUNS none-pass = {f.Calls}");
    }
    [Fact] public void Search_Impact_Rows_Present()
    {
        var (p, c, f) = Case();
        var r = SearchStrategy.Search(p, f, c, new SearchOptions());
        var row = Assert.Single(r.Impact, x => x.Group == "G1" && x.OtherGroup == "G2");
        Assert.Equal(-0.01, row.UtilDelta, 6);
        var (p2, c2, f2) = Case();
        Assert.Empty(SearchStrategy.Search(p2, f2, c2, new SearchOptions(ImpactTable: false)).Impact);
        System.Console.WriteLine($"RUNS impact off = {f2.Calls}");
    }
    [Fact] public void Search_Caches_Runs()
    {
        var (p, c, f) = Case();
        SearchStrategy.Search(p, f, c, new SearchOptions());
        Assert.InRange(f.Calls, 1, 60);   // Fake also fails any (scenario, seed) executed twice
    }
}
