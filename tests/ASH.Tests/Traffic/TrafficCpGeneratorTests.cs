using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Core.Traffic;
using ASH.Tests.Layout;

namespace ASH.Tests.Traffic;

public class TrafficCpGeneratorTests
{
    static readonly TrafficParams P = new(VehicleLength: 2.0, VehicleWidth: 1.0, SafetyGap: 0.5);   // ClaimDist 0.5
    static ASH.Core.Layout.Layout CrossWithBlock() { var l = L.Of(L.Line("a",-10,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0), L.Line("d",0,0,0,10)); l.Blocks.Add(L.Block("B001",-3.5,-3.5,3.5,3.5)); return l; }

    [Fact] public void Entering_Paths_Get_One_Cp_Before_Boundary()
    {
        var r = TrafficCpGenerator.Generate(CrossWithBlock(), P);
        Assert.Equal(2, r.Cps.Count);
        var ca = r.Cps.Single(c => Math.Abs(c.Position.Y) < 1e-9); Assert.Equal(-4.0, ca.Position.X, 9); Assert.Equal("T001", ca.Name); Assert.Equal("traffic", ca.Type);
        Assert.Empty(r.Warnings);
    }
    [Fact] public void Existing_Cp_At_Same_Spot_Is_Not_Duplicated()
    {
        var l = CrossWithBlock(); l.Cps.Add(L.Cp("T005", -4.0, 0));
        var r = TrafficCpGenerator.Generate(l, P); Assert.Single(r.Cps); Assert.Equal("T006", r.Cps[0].Name);
    }
    [Fact] public void Short_Approach_Warns_And_Clamps()
    {
        var l = L.Of(L.Line("a",-3.7,0,0,0), L.Line("b",0,0,10,0), L.Line("c",0,-10,0,0)); l.Blocks.Add(L.Block("B001",-3.5,-3.5,3.5,3.5));
        var r = TrafficCpGenerator.Generate(l, P);
        Assert.Contains(r.Warnings, w => w.StartsWith("a:")); Assert.Equal(-3.7 + 0.001, r.Cps.Single(c => Math.Abs(c.Position.Y) < 1e-9).Position.X, 6);
    }
    [Fact] public void Generate_PathFullyInsideBlock_WarnsNoCp()
    {
        var l = L.Of(L.Line("in",-1,1,1,1)); l.Blocks.Add(L.Block("B001",-3.5,-3.5,3.5,3.5));
        var r = TrafficCpGenerator.Generate(l, P); Assert.Empty(r.Cps); Assert.Contains(r.Warnings, w => w.Contains("in"));
    }
    [Fact] public void No_Blocks_Gives_Guidance_Warning() { var r = TrafficCpGenerator.Generate(L.Of(L.Line("a",0,0,10,0)), P); Assert.Empty(r.Cps); Assert.Contains("AGVBLOCKAUTO", r.Warnings[0]); }
    [Fact] public void Spacing_Cps_Every_10m()
    {
        var l = L.Of(L.Line("a",0,0,35,0)); l.Blocks.Add(L.Block("B001",100,100,101,101));
        var r = TrafficCpGenerator.Generate(l, P with { SpacingCps = true });
        Assert.Equal(new[]{10.0,20.0,30.0}, r.Cps.Select(c => c.Position.X).OrderBy(x=>x));
    }
    [Fact] public void Traffic_Table_Links_Entry_Cp()
    {
        var l = CrossWithBlock(); l.Cps.AddRange(TrafficCpGenerator.Generate(l, P).Cps);
        var t = TrafficTable.Build(l);
        Assert.Equal(2, t.Entries.Count); Assert.All(t.Entries, e => Assert.NotNull(e.CpName));
        Assert.Equal(6.5, t.Entries.Single(e => e.PathName=="a").BoundaryDist, 9);
        var sw = new StringWriter(); t.WriteTsv(sw); Assert.StartsWith("block\tpath\tboundary_dist\tentry_cp", sw.ToString());
    }
}
