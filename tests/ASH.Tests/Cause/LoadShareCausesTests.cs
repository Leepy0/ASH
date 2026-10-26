using System.Linq;
using ASH.Core.Cause;
using ASH.Core.Runner;
using ASH.Tests.Layout;

namespace ASH.Tests.Cause;

public class LoadShareCausesTests
{
    static ResultRow Row(string name, double dist) => new("material", name, "G1", new[] { 0, 0, 0, 0, 0, 0, dist, 0 });

    static CauseContext Ctx(double d1, double d2, ASH.Core.Layout.Layout? l = null, bool oldModel = false)
    {
        var mats = new[] { new MaterialResult("M7", "G1", 0, 0, 8, 8, 0, 8), new MaterialResult("M8", "G1", 0, 0, 92, 92, 0, 92) };
        var rows = oldModel ? mats.Select(m => new ResultRow("material", m.Name, "G1", new double[6])).ToArray() : new[] { Row("M7", d1), Row("M8", d2) };
        var rs = new ResultSet(mats, new[] { new GroupResult("G1", 1) }, new BlockResult[0], 0, 0, rows);
        return new CauseContext(l ?? L.Of(), DetourCausesTests.Input(), rs);
    }

    [Fact]
    public void Material_With_Far_More_Driving_Than_Demand_Is_Flagged()
    {
        var f = Assert.Single(LoadShareCauses.Find(Ctx(410, 590)));
        Assert.Equal(CauseKinds.LoadShare, f.Kind); Assert.Equal(1, f.Severity);
        Assert.Equal("M7 주행 41%", f.Title); Assert.Equal("G1", f.Where);
        Assert.Equal("수요 8% · 주행 41%", f.Detail); Assert.Equal("", f.Fix);
        Assert.Null(f.X); Assert.Null(f.Y);
    }

    [Fact]
    public void Fix_Names_The_Detour_Of_The_Materials_Legs()
    {
        var l = L.Of(L.Line("P1", 0, 0, 50, 0), L.Line("P2", 100, 0, 50, 0)); // P2 reversed: A -> B unreachable
        l.Cps.Add(L.Cp("A", 10, 0)); l.Cps.Add(L.Cp("B", 90, 0));
        // route of M7 only; M8 has none, so only M7 can carry a detour
        var f = Assert.Single(LoadShareCauses.Find(Ctx(410, 590, l)));
        Assert.Equal("A→B 우회 확인", f.Fix);
    }

    [Fact]
    public void Proportional_Driving_Models_Without_Distance_And_Layout_Only_Give_Nothing()
    {
        Assert.Empty(LoadShareCauses.Find(Ctx(80, 920)));
        Assert.Empty(LoadShareCauses.Find(Ctx(0, 0, oldModel: true)));
        Assert.Empty(LoadShareCauses.Find(Ctx(410, 590) with { Result = null }));
        Assert.Empty(LoadShareCauses.Find(Ctx(20, 80))); // 20% vs 8% is 2.5x but only 12 points
    }
}
