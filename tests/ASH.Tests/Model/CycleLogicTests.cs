using System.Text.RegularExpressions;
using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Tests.Model;

/// <summary>Cycle supply (Task 5): the vehicles of a Cycle group loop load -> unload without calls or a standby CP.</summary>
public class CycleLogicTests
{
    /// <summary>LogicGeneratorTests.Sample() (G1 calls M1, M2) plus G2 = Cycle: type Cart (1.2 m long, 1 m/s loaded, 2 containers, load 15 s,
    /// unload 12 s), 2 vehicles circling M001 from L001 to U001 and U002, so the station materials are 3 = M001@U001 and 4 = M001@U002.</summary>
    internal static ProjectInput Input(string? buffer = null)
    {
        var p = LogicGeneratorTests.Sample();
        p.AgvTypes.Add(new AgvType("Cart", 1.0, 1.0, 0, 0, 2, 15, 12, 0, 0, 1.2, 0.8, "green"));
        p.Groups.Add(new AgvGroup("G2", "Cart", 2, 2, new List<string>(), "Nearest", SupplyMode: "Cycle"));
        p.Materials.Add(new MaterialInfo("M001", "ST1", "G2", 60, 1, 10, 4, 3, 5, null, false));
        p.Routes.Add(new Route("M001", "L001", "U001", buffer, null, null, 0));
        StationExpansion.Expand(p, new Dictionary<string, List<string>> { ["M001"] = new() { "U001", "U002" } });
        return p;
    }

    /// <summary>The layout CPs plus G2's start CPs (the model builder adds them from the exported agv.asy).</summary>
    internal static readonly LayoutInfo Layout = LogicGeneratorTests.Layout with { CpNames = new[] { "S001", "L001", "B001", "U001", "U002", "SP_G2_1", "SP_G2_2" } };

    static string Proc(string t, string begin)
    {
        int i = t.IndexOf(begin, StringComparison.Ordinal);
        Assert.True(i >= 0, begin);
        return t.Substring(i, t.IndexOf("\nend\n", i, StringComparison.Ordinal) - i);
    }

    static string Cycle(string t) => Proc(t, "begin P_cycle_2 arriving procedure");

    // closing the station's open swap on arrival at its wait point, right before the hold: FOLLOW = (1.2 m + StopGapM 0.5 m) / 1 m/s + 5 s
    const string Close = "\t\t\tif V_swapOpen(A_m) = 1 then begin\n\t\t\t\tset V_x to ac - V_lastLeave(A_m)\n\t\t\t\tinc V_swapN(A_m) by 1\n\t\t\t\tinc V_swapSum(A_m) by V_x\n" +
                         "\t\t\t\tif V_x > V_swapMax(A_m) then set V_swapMax(A_m) to V_x\n\t\t\t\tif V_x <= 6.7 then inc V_follow(A_m) by 1\n\t\t\t\tset V_swapOpen(A_m) to 0\n\t\t\tend\n";
    const string Hold = "\t\t\tinc V_waitQty(A_m) by A_qty\n\t\t\tinc V_waitVeh(A_m) by 1\n\t\t\tset A_bw to ac\n\t\t\tset A_hold to ac\n\t\t\twhile V_stock(A_m) > V_unload(A_m) do begin\n";
    // a line of the station branch (three tabs in) or a blank one
    const string Branch = @"(?:\t\t\t.*\n|\n)*?";

    [Fact]
    public void Cycle_Stations_Are_Consumed_But_Never_Called()
    {
        var t = LogicGenerator.Render(Input(), Layout);
        foreach (var k in new[] { 3, 4 })
        {
            var c = Proc(t, $"begin P_consume_{k} arriving procedure");
            Assert.DoesNotContain("clone 1 load to P_supply_", c);
            Assert.DoesNotContain("V_pending", c);
            Assert.Contains($"\t\t\tdec V_stock({k}) by V_need({k})\n", c);
            Assert.Contains($"\t\t\tinc V_waitSum({k}) by V_waitQty({k})\n", c);
            Assert.DoesNotContain($"P_supply_{k}", t);
        }
        // the call materials keep their call
        Assert.Contains("\t\t\tclone 1 load to P_supply_1\n", Proc(t, "begin P_consume_1 arriving procedure"));
        Assert.Contains("begin P_supply_2 arriving procedure", t);
    }

    [Fact]
    public void A_Lap_Loads_Then_Takes_The_Next_Station()
    {
        var c = Cycle(LogicGenerator.Render(Input(), Layout));
        // the stations take turns, one per lap; A_m = the station's material, the load shows as its picture
        Assert.Contains("\t\tinc V_toRot(2) by 1\n\t\tif V_toRot(2) > 2 then set V_toRot(2) to 1\n\t\tif V_toRot(2) = 1 then begin\n\t\t\tset A_m to 3\n\t\t\tset load type to L_m3\n", c);
        Assert.Contains("\t\telse begin\n\t\t\tset A_m to 4\n\t\t\tset load type to L_m4\n", c);
        // the lead times count from the lap start, into the station's own tables
        Assert.Contains("\t\t\tif ac >= V_warmup then begin\n\t\t\t\ttabulate ac - A_tCall in T_pick_3\n\t\t\t\tif V_events = 1 then print ac \"\\tpick\\tM001@U001\\tL001\\t\" ac - A_tCall to V_ev\n", c);
        Assert.Contains("\t\t\t\tinc V_supplies(4) by A_qty\n\t\t\t\ttabulate ac - A_tCall in T_deliv_4\n\t\t\t\tif V_events = 1 then print ac \"\\tdeliver\\tM001@U002\\tU002\\t\" ac - A_tCall to V_ev\n", c);
        // each station's run is its own branch, its legs on its own slots and names
        Assert.Matches(@"\t\tif A_m = 3 then begin\n" + Branch + @"\t\t\tset V_inLeg\(3\) to 1\n" + Branch + @"\t\t\ttravel to agv:U001\n" + Branch + @"\t\tend\n\t\telse begin\n" + Branch + @"\t\t\ttravel to agv:U002\n", c);
        Assert.Contains("\\twait\\tM001@U002\\tU002\\t", c);
        // the hold for the unload threshold is the station's (the call supply's HoldBlock on A_m)
        Assert.Equal(2, Regex.Matches(c, Regex.Escape(Hold)).Count);
        // lap order: load CP, the load, the station's run, the unload into the station's stock, the lap's end
        int Ix(string s) { int i = c.IndexOf(s, StringComparison.Ordinal); Assert.True(i >= 0, s); return i; }
        Assert.True(Ix("\twhile 1 = 1 do begin\n\t\tset A_tCall to ac\n\t\tset A_tPick to ac\n\t\tset A_ret to 0\n") < Ix("\t\ttravel to agv:L001\n"));
        Assert.True(Ix("\t\ttravel to agv:L001\n") < Ix("\t\tinc V_toRot(2) by 1"));
        Assert.True(Ix("\t\tinc V_toRot(2) by 1") < Ix("\t\tscale to x 1, y 1, z 1\n\t\twait for 15 sec\n"));
        Assert.True(Ix("\t\twait for 15 sec\n") < Ix("travel to agv:U001"));
        Assert.True(Ix("travel to agv:U002") < Ix("\t\tset V_extMat to A_m\n\t\tset V_tmp to F_ext_before_unload()\n\t\twait for 12 sec\n\t\tinc V_stock(A_m) by A_qty\n"));
        Assert.True(Ix("inc V_stock(A_m) by A_qty") < Ix("tabulate ac - A_tCall in T_deliv_3"));
        Assert.True(Ix("tabulate ac - A_tCall in T_deliv_4") < Ix("\t\tset A_fin to ac\n"));
        Assert.EndsWith("\t\tset A_veh A_dLast to A_veh total distance traveled\n\tend", c);
    }

    [Fact]
    public void Swap_Closes_On_Arrival_At_The_Unload_Cp_Without_A_Buffer()
    {
        var c = Cycle(LogicGenerator.Render(Input(), Layout));
        foreach (var u in new[] { "U001", "U002" })
            Assert.Matches(@"\t\t\ttravel to agv:" + u + @"\n" + Branch + Regex.Escape(Close + Hold), c);
        Assert.Equal(2, Regex.Matches(c, Regex.Escape(Close)).Count);
        // leaving after the unload: a next vehicle held there already swaps in at once (0 s, no maximum), else the swap opens
        Assert.Contains("\t\tinc V_stock(A_m) by A_qty\n", c);
        const string Leave = "\t\tif ac >= V_warmup then begin\n\t\t\tif V_waitVeh(A_m) > 0 then begin\n\t\t\t\tinc V_swapN(A_m) by 1\n\t\t\t\tinc V_follow(A_m) by 1\n\t\t\t\tset V_swapOpen(A_m) to 0\n" +
                             "\t\t\tend\n\t\t\telse begin\n\t\t\t\tset V_lastLeave(A_m) to ac\n\t\t\t\tset V_swapOpen(A_m) to 1\n\t\t\tend\n\t\tend\n";
        Assert.Matches(@"\t\tinc V_stock\(A_m\) by A_qty\n(?:\t\t/\*.*\n)?" + Regex.Escape(Leave), c);
        Assert.Single(Regex.Matches(c, "set V_lastLeave"));
    }

    [Fact]
    public void With_A_Buffer_The_Swap_Closes_At_The_Buffer()
    {
        var c = Cycle(LogicGenerator.Render(Input("B001"), Layout));
        Assert.Equal(2, Regex.Matches(c, Regex.Escape(Close + Hold)).Count);
        Assert.Matches(@"\t\t\ttravel to agv:B001\n" + Branch + Regex.Escape(Close + Hold) + Branch + @"\t\t\ttravel to agv:U001\n", c);
        // not again at the unload CP
        Assert.DoesNotMatch(@"travel to agv:U00\d\n" + Branch + @"\t\t\tif V_swapOpen", c);
    }

    [Fact]
    public void The_Start_Load_Clones_A_Load_Per_Active_Vehicle_Onto_Its_Start_Cp()
    {
        var t = LogicGenerator.Render(Input(), Layout);
        Assert.Contains("\tcreate 1 load of type L_m3 to P_cycle_2\n", t);
        Assert.DoesNotContain("to P_cycle_1", t);
        var c = Cycle(t);
        Assert.Contains("\tif A_k = 0 then begin\n", c);
        Assert.Contains("\t\tset V_i to 1\n\t\twhile V_i <= V_active(2) do begin\n\t\t\tset A_k to V_i\n\t\t\tclone 1 load to P_cycle_2\n\t\t\tinc V_i by 1\n\t\tend\n\t\tsend to die\n\tend\n", c);
        Assert.Contains("\tset A_qty to 2\n", c);
        Assert.Contains("\tif A_k = 1 then begin\n\t\tmove into agv:SP_G2_1\n\tend\n\telse begin\n\t\tmove into agv:SP_G2_2\n\tend\n\tset A_veh to this load vehicle\n\tset A_vid to A_veh A_index + 3\n", c);
        // the start CPs come from the export: none placed is an error, not a model that never starts
        Assert.Throws<ArgumentException>(() => LogicGenerator.Render(Input(), LogicGeneratorTests.Layout with { CpNames = new[] { "S001", "L001", "B001", "U001", "U002" } }));
    }

    [Fact]
    public void A_Lap_Is_A_Job_And_Counts_After_Warmup_Like_A_Supply_Job()
    {
        var t = LogicGenerator.Render(Input(), Layout);
        var c = Cycle(t);
        // the busy time and the time split, the same block as the call supply's, by the lap's station
        Assert.Contains("\t\tif A_tPick >= V_warmup then begin\n\t\t\tinc V_vBusy(A_vid) by A_ret + A_fin - A_tPick - A_hold\n\t\t\tinc V_vJobs(A_vid) by 1\n", c);
        Assert.Contains("\t\t\tinc V_vHand(A_vid) by 27\n", c);                                  // load 15 + unload 12
        Assert.Contains("\t\t\tinc V_mDist(A_m) by V_x - A_veh A_dLast\n\t\t\tinc V_mJobs(A_m) by 1\n", c);
        Assert.Contains("\t\t\tinc V_gT(2) by 1\n\t\t\tinc V_gT(4) by A_fin - A_tPick - A_hold\n", c);  // group 2 of 2: V_gT(k x 2 + 2)
        Assert.Contains("\t\t\tinc V_gT(8) by V_vT(A_vid + 5) - A_js0\n", c);                    // 5 vehicles in all
        Assert.Contains("\t\tset A_js0 to V_vT(A_vid + 5)\n\t\tset A_jt0 to V_vT(A_vid + 10)\n", c);
        Assert.True(c.IndexOf("\t\tset A_fin to ac\n", StringComparison.Ordinal) < c.IndexOf("if A_tPick >= V_warmup", StringComparison.Ordinal));
        var block = Proc(t, "begin P_supply_1 arriving procedure");
        foreach (var line in new[] { "/* where the busy time went", "inc V_vBufB(A_vid) by A_bw - A_hold", "set A_veh A_dLast to A_veh total distance traveled" })
        {
            Assert.Contains(line, c);
            Assert.Contains(line, block);
        }
    }

    [Fact]
    public void A_Cycle_Group_Never_Uses_A_Standby_Or_Finish_Cp()
    {
        // StandbyCPs and FinishCP of a Cycle group are warned unused by the validator
        var p = Input();
        p.Groups[1] = p.Groups[1] with { StandbyCPs = new List<string> { "S001" }, FinishCP = "B001" };
        var t = LogicGenerator.Render(p, Layout);
        Assert.Contains("begin agv.G2 park ok function\n\n\treturn false\nend", t);
        Assert.Contains("begin agv.G1 park ok function\n\tif parkLoc = agv:S001 then return true", t);
        var c = Cycle(t);
        foreach (var s in new[] { "S001", "B001", "HOME", "nl_", "{{" }) Assert.DoesNotContain(s, c);
    }

    [Fact]
    public void One_Station_And_One_Start_Cp_Need_No_Chain()
    {
        var p = LogicGeneratorTests.Sample();
        p.AgvTypes.Add(new AgvType("Cart", 1.0, 1.0, 0, 0, 2, 15, 12, 0, 0, 1.2, 0.8, "green"));
        p.Groups.Add(new AgvGroup("G2", "Cart", 1, 1, new List<string>(), "Nearest", SupplyMode: "Cycle"));
        p.Materials.Add(new MaterialInfo("M001", "ST1", "G2", 60, 1, 10, 4, 3, 5, null, false));
        p.Routes.Add(new Route("M001", "L001", "U001", null, null, null, 0));
        var c = Cycle(LogicGenerator.Render(p, Layout));
        Assert.Contains("\tmove into agv:SP_G2_1\n\tset A_veh to this load vehicle\n", c);
        Assert.Contains("\t\tif V_toRot(2) > 1 then set V_toRot(2) to 1\n\t\tset A_m to 3\n", c);
        Assert.DoesNotContain("if A_m =", c);
        Assert.Contains(Close.Replace("\t\t\t", "\t\t") + Hold.Replace("\t\t\t", "\t\t"), c);
    }

    [Fact]
    public void BuildProcessModel_Declares_The_Cycle_Procedure_And_Its_Names()
    {
        var m = LogicGenerator.BuildProcessModel(Input(), Layout, "Meters");
        var procs = m.Procs.Select(x => x.Name).ToList();
        foreach (var x in new[] { "P_cycle_2", "P_consume_3", "P_consume_4", "P_supply_1", "P_supply_2" }) Assert.Contains(x, procs);
        foreach (var x in new[] { "P_supply_3", "P_supply_4", "P_cycle_1" }) Assert.DoesNotContain(x, procs);
        Assert.Contains(m.Atts, a => a.Name == "A_m" && a.Type == "Integer");
        Assert.Contains(m.Atts, a => a.Name == "A_k" && a.Type == "Integer");
        Assert.Contains(m.Vars, v => v.Name == "V_toRot" && v.Type == "Integer" && v.ArraySize == 2);
        // every name the logic uses is declared (as for the call supply)
        var names = new HashSet<string>(m.Vars.Select(v => v.Name).Concat(procs).Concat(m.Orders).Concat(m.Queues.Select(q => q.Name))
            .Concat(m.Atts.Select(a => a.Name)).Concat(m.Funcs).Concat(m.LoadTypes).Concat(m.Tables));
        foreach (var u in Regex.Matches(m.LogicSource, @"\b(?:V|P|OL|Q|A|F|L|T)_\w+").Cast<Match>().Select(x => x.Value).Distinct()) Assert.Contains(u, names);
        var w = new StringWriter(); ProcessAsyWriter.Write(m, w);
    }

    [Fact]
    public void Vehicle_Groups_Start_A_Cycle_Group_At_Its_First_Load_Cp()
    {
        var p = Input();
        p.Routes[2] = p.Routes[2] with { SupplyFromCPs = new[] { "L002", "L001" }, SupplyFromCP = "L002" };
        var specs = ModelBuilder.VehicleGroups(p);
        Assert.Null(specs[0].CycleStartCp);
        Assert.Equal("L002", specs[1].CycleStartCp);
    }
}
