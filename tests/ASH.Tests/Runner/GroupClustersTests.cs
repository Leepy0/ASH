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

    [Fact] public void Supply_Length_Follows_Route_Paths()
    {
        var (l, p) = Setup("D2", "D1");                    // M2: p4 -> p3 unreachable, so no length
        var c = GroupClusters.Build(l, p, TrafficTable.Build(l));
        Assert.Equal(13, c.SupplyLength["M1"], 6);         // C1 at 2 on p1 -> C2 at 5 on p2: 8 + 5
        Assert.False(c.SupplyLength.ContainsKey("M2"));
        p.Routes[0] = p.Routes[0] with { SupplyToCP = "C1", SupplyFromCP = "C2" };   // against the one-way direction: p2 -> p1 unreachable
        Assert.False(GroupClusters.Build(l, p, TrafficTable.Build(l)).SupplyLength.ContainsKey("M1"));
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
        Assert.Equal(2, c.Clusters.Count);
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

    static (ASH.Core.Layout.Layout, ProjectInput) Loop()
    {
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("C1", 2, 0)); l.Cps.Add(L.Cp("C2", 10, 5)); l.Cps.Add(L.Cp("C3", 5, 10)); l.Cps.Add(L.Cp("C4", 0, 5));
        var p = new ProjectInput();
        p.Groups.Add(new AgvGroup("G1", "T", 1, 3, new(), "x")); p.Groups.Add(new AgvGroup("G2", "T", 1, 3, new(), "x"));
        p.Materials.Add(new MaterialInfo("M1", "S", "G1", 1, 1, 1, 1, 1, null, null, false));
        p.Materials.Add(new MaterialInfo("M2", "S", "G2", 1, 1, 1, 1, 1, null, null, false));
        p.Routes.Add(new Route("M1", "C1", "C2", null, null, null, 0));
        p.Routes.Add(new Route("M2", "C3", "C4", null, null, null, 0));
        return (l, p);
    }
    [Fact] public void Return_Path_Only_Sharing_Makes_One_Cluster()
    {
        var (l, p) = Loop();
        var c = GroupClusters.Build(l, p, TrafficTable.Build(l));
        Assert.Single(c.Clusters);
        Assert.Equal(4, c.UsedPaths["G1"].Count);   // forward p1,p2 + return p3,p4
    }
    [Fact] public void Standby_Cp_Adds_Return_Leg_To_Standby()
    {
        var (l, p) = Loop();
        l.Cps.Add(L.Cp("S1", 5, 0));
        p.Groups[0] = p.Groups[0] with { StandbyCPs = new() { "S1" } };
        var c = GroupClusters.Build(l, p, TrafficTable.Build(l));
        Assert.Equal(4, c.UsedPaths["G1"].Count);
    }
    [Fact] public void Same_Path_Target_Behind_Warns()
    {
        var l = L.Of(L.Line("p1", 0, 0, 10, 0));
        l.Cps.Add(L.Cp("C1", 8, 0)); l.Cps.Add(L.Cp("C2", 2, 0));
        var p = new ProjectInput();
        p.Groups.Add(new AgvGroup("G1", "T", 1, 3, new(), "x"));
        p.Materials.Add(new MaterialInfo("M1", "S", "G1", 1, 1, 1, 1, 1, null, null, false));
        p.Routes.Add(new Route("M1", "C1", "C2", null, null, null, 0));
        var c = GroupClusters.Build(l, p, TrafficTable.Build(l));
        Assert.Contains(c.Warnings, w => w.Contains("loop"));
    }
    [Fact] public void Cp_Far_From_Path_Warns()
    {
        var l = L.Of(L.Line("p1", 0, 0, 10, 0));
        l.Cps.Add(L.Cp("C1", 5, 3));
        var p = new ProjectInput();
        var c = GroupClusters.Build(l, p, TrafficTable.Build(l));
        Assert.Contains(c.Warnings, w => w.Contains("C1") && w.Contains("멀리"));
    }
}
