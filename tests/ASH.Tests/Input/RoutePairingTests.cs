using System.IO;
using System.Linq;
using ASH.Core.Input;
using ASH.Core.Layout;

namespace ASH.Tests.Input;

[Collection("ExcelTemplateStatics")]
public class RoutePairingTests
{
    static LayoutCp Cp(string name, string type, string group) => new() { Name = name, Type = type, Group = group };

    /// <summary>A column of a seed row (template order); the route is on the material's row.</summary>
    static int C(string col, string sheet = "Materials") => System.Array.IndexOf(ExcelTemplate.Sheets.First(s => s.Sheet == sheet).Cols, col);

    static string Route(string[] m) => string.Join(" ", m[C("SupplyFromCP")], m[C("SupplyToCP")], m[C("ReturnFromCP")], m[C("ReturnToCP")]);

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
        Assert.False(ExcelTemplate.Seed(l).ContainsKey("Routes"));
        var mats = ExcelTemplate.Seed(l)["Materials"];
        string Row(string load) => Route(mats.Single(x => x[C("SupplyFromCP")] == load));
        Assert.Equal("L001 U001 RL001 RU001", Row("L001"));
        Assert.Equal("L002 U002 RL002 RU002", Row("L002"));
        Assert.Equal("L003 U003  ", Row("L003"));                                   // G2 has no return CPs: blank, not G1's
        Assert.Equal("G2", mats.Single(m => m[C("SupplyFromCP")] == "L003")[C("Group")]);
    }

    [Fact]
    public void Fewer_Unloads_Than_Loads_Reuse_The_Last_Unload_Of_The_Group()
    {
        var l = new ASH.Core.Layout.Layout();
        foreach (var c in new[] { Cp("L1", "load", "G1"), Cp("L2", "load", "G1"), Cp("U1", "unload", "G1"), Cp("U9", "unload", "G2"), Cp("L9", "load", "G2") }) l.Cps.Add(c);
        var mats = ExcelTemplate.Seed(l)["Materials"];
        Assert.Equal("U1", mats.Single(x => x[C("SupplyFromCP")] == "L2")[C("SupplyToCP")]);
        Assert.Equal("U9", mats.Single(x => x[C("SupplyFromCP")] == "L9")[C("SupplyToCP")]);
    }

    [Fact]
    public void Material_Form_Starts_As_One_The_Groups_Vehicle_Carries()
    {
        var seed = ExcelTemplate.Seed(OneLoadTwoUnloads(second: false));
        var type = seed["AGV_Groups"][0][1];
        var kind = seed["AGV_Types"].First(t => t[0] == type)[C("Kind", "AGV_Types")];
        Assert.Equal(VehicleKinds.Find(kind)!.Carries[0], seed["Materials"][0][C("Form")]);

        // the workbook's own types count on update: an ANT (trolley lifting) group gets Trolley
        var path = Path.Combine(Path.GetTempPath(), "ash_" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, OneLoadTwoUnloads(second: false));
            using (var wb = ExcelReader.Open(path)) { wb.Worksheet("AGV_Types").Cell(3, C("Kind", "AGV_Types") + 1).Value = "Ant"; wb.Save(); }
            ExcelTemplate.Update(path, OneLoadTwoUnloads());
            using (var wb = ExcelReader.Open(path))
                Assert.Equal("Trolley", wb.Worksheet("Materials").Cell(4, C("Form") + 1).GetString()); // the new M002 row
        }
        finally { File.Delete(path); }
    }

    static ASH.Core.Layout.Layout OneLoadTwoUnloads(bool second = true)
    {
        var l = new ASH.Core.Layout.Layout();
        foreach (var c in new[] { Cp("S1", "standby", "1"), Cp("L1", "load", "1"), Cp("U1", "unload", "1"), Cp("RL1", "rtnload", "1"), Cp("RU1", "rtnunload", "1") }) l.Cps.Add(c);
        if (second) { l.Cps.Add(Cp("U2", "unload", "1")); l.Cps.Add(Cp("RL2", "rtnload", "1")); }
        return l;
    }

    [Fact]
    public void More_Unloads_Than_Loads_Make_A_Route_Each_Reusing_The_Last_Load_And_Return()
    {
        var mats = ExcelTemplate.Seed(OneLoadTwoUnloads())["Materials"];
        Assert.Equal(new[] { "L1 U1 RL1 RU1", "L1 U2 RL2 RU1" }, mats.Select(Route));
    }

    [Fact]
    public void Update_Adds_The_Route_Of_A_New_Unload_For_A_Load_Already_In_The_Sheet()
    {
        var path = Path.Combine(Path.GetTempPath(), "ash_" + System.Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            ExcelTemplate.Write(path, OneLoadTwoUnloads(second: false));
            var changes = ExcelTemplate.Update(path, OneLoadTwoUnloads());
            Assert.Single(changes, c => c.StartsWith("Materials: load CP"));
            using (var wb = ExcelReader.Open(path))
            {
                var ws = wb.Worksheet("Materials");
                int from = C("SupplyFromCP") + 1, to = C("SupplyToCP") + 1;
                var rows = ws.RowsUsed().Where(r => r.RowNumber() >= 3 && r.Cell(from).GetString().Length > 0).Select(r => r.Cell(from).GetString() + " " + r.Cell(to).GetString()).ToList();
                Assert.Equal(new[] { "L1 U1", "L1 U2" }, rows);
            }
            Assert.Empty(ExcelTemplate.Update(path, OneLoadTwoUnloads()).Where(c => c.StartsWith("Materials: load CP"))); // nothing new the second time
        }
        finally { File.Delete(path); }
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
        Assert.Equal("Materials!3", f.EntityId); // the route is on the material's row
        Assert.Contains("SupplyToCP", f.Message); Assert.Contains("U003", f.Message);
        p.RouteSheet = "Routes"; // an older workbook's own sheet
        Assert.Equal("Routes!3", Assert.Single(RouteGroupCheck.Check(p, l)).EntityId);
    }

    [Fact]
    public void Every_Load_Cp_Of_A_List_Is_Judged()
    {
        var l = new ASH.Core.Layout.Layout();
        l.Cps.Add(new LayoutCp { Name = "L001", Type = "load", Group = "G1" });
        l.Cps.Add(new LayoutCp { Name = "L002", Type = "load", Group = "G2" });
        l.Cps.Add(new LayoutCp { Name = "U001", Type = "unload", Group = "G1" });
        var p = new ProjectInput();
        p.Materials.Add(new MaterialInfo("M001", "", "G1", 60, 1, 1, 1, 1, null, null, false, Row: 3));
        p.Routes.Add(new Route("M001", "L001", "U001", null, null, null, 0, Row: 3, SupplyFromCPs: new[] { "L001", "L002" }));
        var f = Assert.Single(RouteGroupCheck.Check(p, l));
        Assert.Contains("SupplyFromCP: L002", f.Message);
    }
}
