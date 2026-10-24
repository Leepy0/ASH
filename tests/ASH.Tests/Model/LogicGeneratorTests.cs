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
        p.AgvTypes.Add(new AgvType("Tug", 1.0, 1.0, 0, 0, 1, 10, 10, 0, 0, 2, 1, "red"));
        p.Groups.Add(new AgvGroup("G1", "Tug", 1, 3, new List<string> { "S001" }, "Nearest"));
        foreach (var m in new[] { "M1", "M2" })
        {
            p.Materials.Add(new MaterialInfo(m, "ST1", "G1", takt, 1, 10, 4, 3, 5, null, false)); // Call 4 > Settings SafetyStock 2
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
        p.AgvTypes.Add(new AgvType("Tug2", 1.0, 1.0, 1, 1, 1, 10, 10, 0, 0, 2, 1, "blue"));
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
        // calls wait at the group's standby CP, so that is where a free vehicle looks for work
        Assert.Equal(new[] { "S001" }, s.NameLists["nl_pick_G1"]);
        Assert.Equal(new[] { "S002" }, s.NameLists["nl_pick_G2"]);
        Assert.Equal(new[] { "S002" }, s.NameLists["nl_G2"]);
        Assert.Contains("WORKLST name U001 Oldest item nl_pick_G1 item nl_pick_G2 ", s.WorkListLines);
        Assert.Contains("PARKLST name U001 Sequential item S001 item S002 ", s.ParkListLines);
        Assert.DoesNotContain(s.ParkListLines, l => l.StartsWith("PARKLST name S00"));
        Assert.Equal(new[] { "SRCHLST name S001 Closest item All ", "SRCHLST name S002 Closest item All " }, s.SearchListLines);

        var t = LogicGenerator.Render(input, Layout2);
        Assert.Contains("begin agv.G2 work ok function\n\tif theLoad A_grp = 2 then return true", t);
        Assert.Contains("begin agv.G2 park ok function\n\tif parkLoc = agv:S002 then return true", t);
        Assert.Contains("begin P_supply_2 arriving procedure\n\tset A_grp to 2", t);

        // a type shared by two groups: each group is its own AGVS vehicle type
        var shared = TwoGroups(); shared.Groups[1] = shared.Groups[1] with { Type = "Tug" };
        Assert.Contains("begin agv.G2 work ok function", LogicGenerator.Render(shared, Layout2));
        var orphan = TwoGroups(); orphan.Materials[1] = orphan.Materials[1] with { Group = "GX" };
        Assert.Throws<ArgumentException>(() => LogicGenerator.AddDispatchLists(s, orphan, Layout2));
    }

    [Fact]
    public void Render_Group_Row_Consumed_Column_And_Return_Leg_Slots()
    {
        var p = Sample();
        p.Materials[0] = p.Materials[0] with { EmptyReturn = true };
        p.Routes[0] = p.Routes[0] with { ReturnFromCP = "U001", ReturnToCP = "L001" };
        var t = LogicGenerator.Render(p, Layout);
        Assert.Contains("print \"group\\tG1\\tG1\\t\" V_active(1) \"", t);                     // requested active count, not Max - parked
        Assert.Contains("inc V_consumed(1) by V_need(1)", t);
        Assert.Contains("V_stop(1) \"\\t\" V_consumed(1) \"\\t\" V_mDist(1) \"\\t\" V_mJobs(1) to V_out", t); // value7/8: the supply jobs' distance and count
        // supply legs of material 1 use slot 1, its return legs slot 1 + 2 = 3; durations come from the load's A_t0
        var supply = t.Substring(t.IndexOf("begin P_supply_1"), t.IndexOf("begin P_consume_2") - t.IndexOf("begin P_supply_1"));
        Assert.Equal(2, Regex.Matches(supply, @"set V_inLeg\(1\) to 1").Count);
        Assert.Equal(2, Regex.Matches(supply, @"set V_inLeg\(3\) to 1").Count);
        Assert.Contains("if ac - A_t0 > V_deadlockSec", supply);
        Assert.DoesNotContain("ac - V_legStart", supply);
        // empty container pickup at ReturnFrom (LoadTime 10) and set-down at ReturnTo (UnloadTime 10)
        var ret = supply.Substring(supply.IndexOf("dec V_pending(1) by A_qty"));
        Assert.Matches(@"travel to agv:U001[\s\S]*?wait for 10 sec[\s\S]*?travel to agv:L001[\s\S]*?wait for 10 sec[\s\S]*?travel to agv:S001[\s\S]*?send to die", ret);
        Assert.Contains("if V_inLeg(3) = 1 and ac - V_legStart(3) > V_deadlockSec", t);
        Assert.Contains("read V_tok, V_simLen, V_warmup, V_deadlockSec from V_fp", t);
    }

    [Fact]
    public void Busy_Time_Leaves_Out_The_Buffer_Wait_For_The_Unload_Threshold()
    {
        var t = LogicGenerator.Render(Sample(), Layout); // routes L001 -> B001 (buffer) -> U001
        var supply = t.Substring(t.IndexOf("begin P_supply_1"), t.IndexOf("begin P_consume_2") - t.IndexOf("begin P_supply_1"));
        // reset per job, measured around the threshold wait only (after the buffer leg, before the unload leg), taken off the busy time
        Assert.Matches(@"set A_tPick to ac\n\tset A_hold to 0\n", supply);
        Assert.Matches(@"travel to agv:B001[\s\S]*?\tset A_hold to ac\n\twhile V_stock\(1\) > V_unload\(1\) do begin\n\t\twait for 5 sec\n\tend\n\tset A_hold to ac - A_hold\n[\s\S]*?travel to agv:U001", supply);
        Assert.Contains("inc V_vBusy(A_vid) by A_ret + A_fin - A_tPick - A_hold", supply);
        // the load and unload times set in the input stay busy: they are plain waits inside the busy span
        Assert.Matches(@"set A_tPick to ac[\s\S]*?wait for 10 sec[\s\S]*?wait for 10 sec[\s\S]*?inc V_vBusy", supply);
        // without a buffer there is no hold, the busy time is unchanged
        var p = Sample(); for (int i = 0; i < p.Routes.Count; i++) p.Routes[i] = p.Routes[i] with { BufferCP = null };
        var plain = LogicGenerator.Render(p, Layout);
        Assert.DoesNotContain("set A_hold to ac", plain);
        Assert.Contains("set A_hold to 0", plain);
        // the material's supply jobs count and distance go with the vehicle's
        Assert.Contains("inc V_mDist(1) by V_x - A_veh A_dLast\n\t\tinc V_mJobs(1) by 1", supply);
    }

    [Fact]
    public void Render_Applies_Route_Extra_Time()
    {
        var p = Sample(); p.Routes[0] = p.Routes[0] with { ExtraTime = 7.5 };
        var t = LogicGenerator.Render(p, Layout);
        Assert.Single(Regex.Matches(t, @"\twait for 7\.5 sec\n"));
    }

    [Fact]
    public void Render_Has_No_Group_Block_Rows_And_Debug_Gated_Prints()
    {
        var t = LogicGenerator.Render(Sample(), Layout);
        Assert.DoesNotContain("blockgroup", t); // bottlenecks are compared by zone (block rows) only
        Assert.DoesNotContain("V_bg", t);
        Assert.Contains("read V_tok, V_debug from V_fp", t);
        foreach (var line in t.Split('\n').Where(l => l.Contains("to message") && !l.Contains("does not match")))
            Assert.Contains("V_debug = 1", line);
    }

    [Fact]
    public void Render_Opens_Events_File_Only_When_Asked()
    {
        var t = LogicGenerator.Render(Sample(), Layout);
        Assert.Contains("if V_tok = \"EVENTS\" then read V_events from V_fp", t); // params.txt from an older server ends DEBUG n / END
        Assert.Contains("open \"events.tsv\" for writing save result as V_ev", t);
        Assert.Contains("if V_events = 1", t);
        foreach (var line in t.Split('\n').Where(l => l.Contains("to V_ev") && !l.Contains("t\\tkind")))
            Assert.Contains("V_events = 1", line);
        foreach (var k in new[] { "call", "pick", "deliver", "stockout", "stockin", "wait" })
            Assert.Contains("\\t" + k + "\\tM1\\t", t);
    }

    [Fact]
    public void Breakdowns_EmptySpeed_And_Capacity_Are_Generated_Only_When_Set()
    {
        var plain = LogicGenerator.Render(Sample(), Layout);
        Assert.DoesNotContain("RS_fail", plain);
        Assert.DoesNotContain("defined velocity", plain);
        Assert.Contains("set A_qty to 1", plain);
        Assert.Empty(LogicGenerator.BuildProcessModel(Sample(), Layout, "Meters").RnStreams);

        var p = Sample();
        p.AgvTypes[0] = p.AgvTypes[0] with { SpeedEmpty = 1.5, Capacity = 2, MTBF = 3600, MTTR = 120 };
        var t = LogicGenerator.Render(p, Layout);
        Assert.Contains("set A_qty to 2", t);
        Assert.Contains("inc V_stock(1) by A_qty", t);
        // three vehicles get a first failure time; the start skips a seed-dependent stretch of the stream
        Assert.Equal(3, Regex.Matches(t, @"set V_vNextFail\(\d\) to rn RS_fail e 3600").Count);
        Assert.Contains("set V_i to (V_seed % 100) * 5000", t);
        // the repair check: after the pickup run and every leg (start = ac), and once at boarding for the idle time
        Assert.Contains("set A_rep to rn RS_fail e 120", t);
        Assert.Contains("if V_vNextFail(A_vid) >= V_warmup then inc V_vDown(A_vid) by A_rep", t);
        Assert.Contains("if ac >= V_warmup then inc V_vDown(A_vid) by A_rep", t);
        // empty to the pickup and after the unload, loaded speed in between and before the load leaves
        Assert.Contains("set A_veh defined velocity to 1.5\n\ttravel to agv:L001", t);
        Assert.Contains("set A_veh defined velocity to specification\n\tif ac >= V_warmup then begin\n\t\ttabulate ac - A_tCall in T_pick_1", t);
        Assert.Contains("V_vDown(1) \"\\t0\\t0\" to V_out", t);
        var pm = LogicGenerator.BuildProcessModel(p, Layout, "Meters");
        Assert.Equal("RS_fail", Assert.Single(pm.RnStreams).Name);
        Assert.Contains(pm.Vars, v => v.Name == "V_vDown");
    }

    [Fact]
    public void Render_Includes_One_Consume_Proc_Per_Material()
    {
        var t = LogicGenerator.Render(Sample(), Layout);
        Assert.Equal(2, Regex.Matches(t, @"begin P_consume_\d+ arriving procedure").Count);
        Assert.Equal(2, Regex.Matches(t, @"begin P_supply_\d+ arriving procedure").Count);
        Assert.Contains("create 1 load of type L_ctl to P_consume_2", t);
        Assert.Contains("begin agv.G1 task search procedure", t);
        Assert.DoesNotContain("{{", t);
    }

    [Fact]
    public void Render_Uses_Route_Cp_Names()
    {
        var t = LogicGenerator.Render(Sample(), Layout);
        Assert.Contains("move into agv:S001", t);                  // the call boards at the standby CP (first come first served)
        Assert.Contains("travel to agv:L001", t);
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
