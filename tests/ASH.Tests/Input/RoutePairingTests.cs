using ASH.Core.Input;
using ASH.Core.Layout;

namespace ASH.Tests.Input;

public class RoutePairingTests
{
    static LayoutCp Cp(string name, string type, string group) => new() { Name = name, Type = type, Group = group };

    [Fact]
    public void Seed_Pairs_Each_Load_With_The_Same_Groups_Cps_In_Name_Order()
    {
        // placed out of order and interleaved between groups: G2's unload must never land on a G1 row
        var l = new ASH.Core.Layout.Layout();
        foreach (var c in new[] {
            Cp("U002", "unload", "G1"), Cp("L003", "load", "G2"), Cp("L002", "load", "G1"), Cp("U003", "unload", "G2"), Cp("RL002", "rtnload", "G1"),
            Cp("L001", "load", "G1"), Cp("RU001", "rtnunload", "G1"), Cp("U001", "unload", "G1"), Cp("RL001", "rtnload", "G1"), Cp("RU002", "rtnunload", "G1"),
            Cp("S001", "standby", "G1"), Cp("S002", "standby", "G2") })
            l.Cps.Add(c);
        var routes = ExcelTemplate.Seed(l)["Routes"];
        var mats = ExcelTemplate.Seed(l)["Materials"];
        string Row(string load) { var r = routes.Single(x => x[1] == load); return string.Join(" ", r[1], r[2], r[4], r[5]); }
        Assert.Equal("L001 U001 RL001 RU001", Row("L001"));
        Assert.Equal("L002 U002 RL002 RU002", Row("L002"));
        Assert.Equal("L003 U003  ", Row("L003"));                                   // G2 has no return CPs: blank, not G1's
        Assert.Equal("G2", mats.Single(m => m[0] == routes.Single(x => x[1] == "L003")[0])[2]);
    }

    [Fact]
    public void Fewer_Unloads_Than_Loads_Reuse_The_Last_Unload_Of_The_Group()
    {
        var l = new ASH.Core.Layout.Layout();
        foreach (var c in new[] { Cp("L1", "load", "G1"), Cp("L2", "load", "G1"), Cp("U1", "unload", "G1"), Cp("U9", "unload", "G2"), Cp("L9", "load", "G2") }) l.Cps.Add(c);
        var routes = ExcelTemplate.Seed(l)["Routes"];
        Assert.Equal("U1", routes.Single(x => x[1] == "L2")[2]);
        Assert.Equal("U9", routes.Single(x => x[1] == "L9")[2]);
    }

    [Fact]
    public void ByName_Sorts_Numbers_As_Numbers() =>
        Assert.Equal(new[] { "L2", "L10", "U1" }, ExcelTemplate.ByName(new[] { "L10", "U1", "L2" }));
}

public class RouteGroupCheckTests
{
    [Fact]
    public void A_Route_Cp_Of_Another_Group_Is_An_Error_On_Its_Row()
    {
        var l = new ASH.Core.Layout.Layout();
        l.Cps.Add(new LayoutCp { Name = "L001", Type = "load", Group = "G1" });
        l.Cps.Add(new LayoutCp { Name = "U003", Type = "unload", Group = "G2" });
        l.Cps.Add(new LayoutCp { Name = "U001", Type = "unload", Group = "" }); // no group: not judged
        var p = new ProjectInput();
        p.Materials.Add(new MaterialInfo("M001", "", "G1", 60, 1, 1, 1, 1, null, null, false, Row: 3));
        p.Routes.Add(new Route("M001", "L001", "U003", null, null, null, 0, Row: 3));
        p.Routes.Add(new Route("M001", "L001", "U001", null, null, null, 0, Row: 4));
        var f = Assert.Single(RouteGroupCheck.Check(p, l));
        Assert.Equal(ASH.Core.Layout.FindingCode.InputError, f.Code);
        Assert.Equal("Routes!3", f.EntityId);
        Assert.Contains("SupplyToCP", f.Message); Assert.Contains("U003", f.Message);
    }
}
