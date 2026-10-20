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
