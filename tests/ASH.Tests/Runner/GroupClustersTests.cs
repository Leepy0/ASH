using ASH.Core.Input;
using ASH.Core.Runner;
using ASH.Core.Traffic;
using ASH.Tests.Layout;
using Xunit;

namespace ASH.Tests.Runner;

public class GroupClustersTests
{
    // p1->p2 on y=0, p3->p4 on y=10; G1 runs C1->C2; G2 runs from/to the given CPs
    static (ASH.Core.Layout.Layout, ProjectInput) Setup(string g2From, string g2To)
    {
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 20, 0), L.Line("p3", 0, 10, 10, 10), L.Line("p4", 10, 10, 20, 10));
        l.Cps.Add(L.Cp("C1", 2, 0)); l.Cps.Add(L.Cp("C2", 15, 0));
        l.Cps.Add(L.Cp("D1", 2, 10)); l.Cps.Add(L.Cp("D2", 15, 10));
        var p = new ProjectInput();
        p.Groups.Add(new AgvGroup("G1", "T", 1, 3, new(), "x")); p.Groups.Add(new AgvGroup("G2", "T", 1, 3, new(), "x"));
        p.Materials.Add(new MaterialInfo("M1", "S", "G1", 1, 1, 1, 1, 1, null, null, false));
        p.Materials.Add(new MaterialInfo("M2", "S", "G2", 1, 1, 1, 1, 1, null, null, false));
        p.Routes.Add(new Route("M1", "C1", "C2", null, null, null, 0));
        p.Routes.Add(new Route("M2", g2From, g2To, null, null, null, 0));
        return (l, p);
    }

    [Fact] public void Shared_Path_Makes_One_Cluster()
    {
        var (l, p) = Setup("C1", "C2");
        var c = GroupClusters.Build(l, p, TrafficTable.Build(l));
        Assert.Single(c.Clusters); Assert.Equal(2, c.Clusters[0].Count);
        Assert.Equal(2, c.UsedPaths["G1"].Count);
    }
    [Fact] public void Disjoint_Groups_Are_Separate_Clusters()
    {
        var (l, p) = Setup("D1", "D2");
        var c = GroupClusters.Build(l, p, TrafficTable.Build(l));
        Assert.Equal(2, c.Clusters.Count); Assert.Empty(c.Warnings);
    }
    [Fact] public void Shared_Block_Makes_One_Cluster()
    {
        var (l, p) = Setup("D1", "D2");
        var t = new TrafficTable();
        t.Entries.Add(new BlockEntry("B1", "p1", 5, null)); t.Entries.Add(new BlockEntry("B1", "p3", 5, null));
        var c = GroupClusters.Build(l, p, t);
        Assert.Single(c.Clusters); Assert.Contains("B1", c.UsedBlocks["G2"]);
    }
    [Fact] public void Unreachable_Pair_Warns_And_Shares_Nothing()
    {
        var (l, p) = Setup("D2", "D1");   // p4 -> p3 has no way back
        var c = GroupClusters.Build(l, p, TrafficTable.Build(l));
        Assert.NotEmpty(c.Warnings); Assert.Equal(2, c.Clusters.Count);
    }
}
