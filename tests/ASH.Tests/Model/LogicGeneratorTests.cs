using System.Text.RegularExpressions;
using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Tests.Model;

public class LogicGeneratorTests
{
    /// <summary>The Task 4 run model: 1 group G1 (Tug, Min 1 Max 3), 2 materials routed L001 -> B001 -> U001.</summary>
    public static ProjectInput Sample(double takt = 60, double simSec = 3600, double warmupSec = 360)
    {
        var p = new ProjectInput { Settings = new ASH.Core.Input.Settings { Units = "Meters", SimLengthSec = simSec, WarmupSec = warmupSec, InitStockMode = "Fixed" } };
        p.AgvTypes.Add(new AgvType("Tug", "G1", 1.0, 1.0, 1, 1, 1, 10, 10, 0, 0, 2, 1, "red"));
        p.Groups.Add(new AgvGroup("G1", "Tug", 1, 3, new List<string> { "S001" }, "Nearest"));
        foreach (var m in new[] { "M1", "M2" })
        {
            p.Materials.Add(new MaterialInfo(m, "ST1", "G1", takt, 1, 10, 2, 3, 5, null, false));
            p.Routes.Add(new Route(m, "L001", "U001", "B001", null, null, 0));
        }
        p.Stations.Add(new StationInfo("ST1", "U001", 0));
        return p;
    }

    public static readonly LayoutInfo Layout = new(new[] { "S001", "L001", "B001", "U001" }, new string[0],
        new Dictionary<string, string> { ["G1"] = "S001" });

    /// <summary>Sample() with M2 moved to group G2 (type Tug2, standby S002, pickup L002).</summary>
    public static ProjectInput TwoGroups(double takt = 60)
    {
        var p = Sample(takt);
        p.AgvTypes.Add(new AgvType("Tug2", "G2", 1.0, 1.0, 1, 1, 1, 10, 10, 0, 0, 2, 1, "blue"));
        p.Groups.Add(new AgvGroup("G2", "Tug2", 1, 2, new List<string> { "S002" }, "Nearest"));
        p.Materials[1] = p.Materials[1] with { Group = "G2" };
        p.Routes[1] = p.Routes[1] with { SupplyFromCP = "L002" };
        return p;
    }

    public static readonly LayoutInfo Layout2 = new(new[] { "S001", "S002", "L001", "L002", "B001", "U001" }, new string[0],
        new Dictionary<string, string>());

    [Fact]
    public void Two_Groups_Get_Own_Pick_Lists_And_Type_Filters()
    {
        var input = TwoGroups();
        var s = new ASH.Core.Asy.AgvsSystem();
        foreach (var cp in Layout2.CpNames) s.ControlPoints.Add(new ASH.Core.Asy.ControlPoint(cp, "DefaultControlPoint", "p1", 1, null));
        LogicGenerator.AddDispatchLists(s, input, Layout2);
        Assert.Equal(new[] { "L001" }, s.NameLists["nl_pick_G1"]);
        Assert.Equal(new[] { "L002" }, s.NameLists["nl_pick_G2"]);
        Assert.Equal(new[] { "S002" }, s.NameLists["nl_G2"]);
        Assert.Contains("WORKLST name U001 Oldest item nl_pick_G1 item nl_pick_G2 ", s.WorkListLines);
        Assert.Contains("PARKLST name U001 Sequential item S001 item S002 ", s.ParkListLines);
        Assert.DoesNotContain(s.ParkListLines, l => l.StartsWith("PARKLST name S00"));
        Assert.Equal(new[] { "SRCHLST name L001 Closest item All ", "SRCHLST name L002 Closest item All " }, s.SearchListLines);

        var t = LogicGenerator.Render(input, Layout2);
        Assert.Contains("begin agv.Tug2 work ok function\n\tif theLoad A_grp = 2 then return true", t);
        Assert.Contains("begin agv.Tug2 park ok function\n\tif parkLoc = agv:S002 then return true", t);
        Assert.Contains("begin P_supply_2 arriving procedure\n\tset A_grp to 2", t);

        var shared = TwoGroups(); shared.Groups[1] = shared.Groups[1] with { Type = "Tug" };
        Assert.Throws<ArgumentException>(() => LogicGenerator.Render(shared, Layout2));
        var orphan = TwoGroups(); orphan.Materials[1] = orphan.Materials[1] with { Group = "GX" };
        Assert.Throws<ArgumentException>(() => LogicGenerator.AddDispatchLists(s, orphan, Layout2));
    }

    [Fact]
    public void Render_Applies_Route_Extra_Time()
    {
        var p = Sample(); p.Routes[0] = p.Routes[0] with { ExtraTime = 7.5 };
        var t = LogicGenerator.Render(p, Layout);
        Assert.Single(Regex.Matches(t, @"\twait for 7\.5 sec\n"));
    }

    [Fact]
    public void Render_Includes_One_Consume_Proc_Per_Material()
    {
        var t = LogicGenerator.Render(Sample(), Layout);
        Assert.Equal(2, Regex.Matches(t, @"begin P_consume_\d+ arriving procedure").Count);
        Assert.Equal(2, Regex.Matches(t, @"begin P_supply_\d+ arriving procedure").Count);
        Assert.Contains("create 1 load of type L_ctl to P_consume_2", t);
        Assert.Contains("begin agv.Tug task search procedure", t);
        Assert.DoesNotContain("{{", t);
    }

    [Fact]
    public void Render_Uses_Route_Cp_Names()
    {
        var t = LogicGenerator.Render(Sample(), Layout);
        Assert.Contains("move into agv:L001", t);
        Assert.Contains("travel to agv:B001", t);
        Assert.Contains("travel to agv:U001", t);
        var bad = Sample(); bad.Routes[0] = bad.Routes[0] with { SupplyToCP = "X999" };
        Assert.Throws<ArgumentException>(() => LogicGenerator.Render(bad, Layout));
    }

    [Fact]
    public void BuildProcessModel_Declares_All_Vars_Used_In_Logic()
    {
        var m = LogicGenerator.BuildProcessModel(Sample(), Layout with { BlockNames = new[] { "B1" } }, "Meters");
        var src = m.LogicSource + m.UserExtSource;
        var names = new HashSet<string>(m.Vars.Select(v => v.Name).Concat(m.Procs.Select(p => p.Name)).Concat(m.Orders)
                .Concat(m.Queues.Select(q => q.Name)).Concat(m.Atts.Select(a => a.Name)).Concat(m.Funcs).Concat(m.LoadTypes));
        var used = Regex.Matches(src, @"\b(?:V|P|OL|Q|A|F|L)_\w+").Cast<Match>().Select(x => x.Value).ToList();
        Assert.Contains("V_stock", used);
        foreach (var u in used) Assert.Contains(u, names);
        Assert.Contains("block\\tB1", m.LogicSource);
        var w = new StringWriter(); ProcessAsyWriter.Write(m, w); // names valid, sections written
        Assert.Contains("SFileBegin\tname user_ext.m\n", w.ToString());
    }
}
