using System.Linq;
using ASH.Core.Cause;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Tests.Layout;

namespace ASH.Tests.Cause;

public class DetourCausesTests
{
    // 100 x 100 m ring, corridor along y=0 in two halves; ring paths two-way so every leg stays reachable
    static ASH.Core.Layout.Layout Ring(LayoutPath p1, LayoutPath p2, bool oneWay = false)
    {
        var rest = new[] { L.Line("P3", 100, 0, 100, 100), L.Line("P4", 100, 100, 0, 100), L.Line("P5", 0, 100, 0, 0) };
        foreach (var p in rest.Append(p1).Append(p2)) p.IsTwoWay = !oneWay && p != p2;
        var l = L.Of(new[] { p1, p2 }.Concat(rest).ToArray());
        l.Cps.Add(L.Cp("A", 10, 0)); l.Cps.Add(L.Cp("B", 90, 0));
        return l;
    }

    public static ProjectInput Input(string material = "M7", string group = "G1", params string[] standby)
    {
        var p = new ProjectInput();
        p.Groups.Add(new AgvGroup(group, "T1", 1, 1, standby.ToList(), "Nearest"));
        p.Materials.Add(new MaterialInfo(material, "S1", group, 60, 1, 1, 1, 1, null, null, false));
        p.Routes.Add(new Route(material, "A", "B", null, null, null, 0));
        return p;
    }

    static CauseContext Ctx(ASH.Core.Layout.Layout l, ProjectInput p) => new(l, p);

    [Fact]
    public void Reversed_Path_On_The_Short_Way_Names_The_Path()
    {
        var l = Ring(L.Line("P1", 0, 0, 50, 0), L.Line("P2", 100, 0, 50, 0)); // P2 drawn against the corridor
        var f = Assert.Single(DetourCauses.Find(Ctx(l, Input())));
        Assert.Equal(CauseKinds.Detour, f.Kind); Assert.Equal(2, f.Severity); // 320 m vs 80 m = 4x
        Assert.Equal("M7 우회 320 m", f.Title); Assert.Equal("A→B", f.Where);
        Assert.Equal("실제 320 m · 방향 무시 80 m · P2 방향 반대 의심", f.Detail);
        Assert.Equal("P2 방향 확인", f.Fix);
        Assert.Equal(75, f.X!.Value, 6); Assert.Equal(0, f.Y!.Value, 6); // midpoint of P2
        Assert.Null(f.AtSec);
        Assert.Equal(new[] { "A", "B", "P2" }, f.Refs);
    }

    [Fact]
    public void Broken_Joint_Names_Both_Paths_At_The_Gap()
    {
        var l = Ring(L.Line("P1", 0, 0, 50, 0), L.Line("P2", 50.3, 0, 100, 0));
        var f = Assert.Single(DetourCauses.Find(Ctx(l, Input())));
        Assert.Equal(2, f.Severity);
        Assert.Contains("P1·P2 끊김 의심", f.Detail); Assert.Equal("P1·P2 연결 확인", f.Fix);
        Assert.Equal(50.15, f.X!.Value, 6); Assert.Equal(new[] { "A", "B", "P1", "P2" }, f.Refs);
    }

    [Fact]
    public void Leg_Reachable_Only_Against_The_Direction_Is_Unreachable()
    {
        var l = L.Of(L.Line("P1", 0, 0, 50, 0), L.Line("P2", 100, 0, 50, 0));
        l.Cps.Add(L.Cp("A", 10, 0)); l.Cps.Add(L.Cp("B", 90, 0));
        var f = Assert.Single(DetourCauses.Find(Ctx(l, Input())));
        Assert.Equal(2, f.Severity); Assert.Equal("M7 도달 불가", f.Title);
        Assert.Equal("실제 도달 불가 · 방향 무시 80 m · P2 방향 반대 의심", f.Detail);
    }

    [Fact]
    public void Clean_One_Way_Loop_Has_No_Findings()
    {
        var l = Ring(L.Line("P1", 0, 0, 50, 0), L.Line("P2", 50, 0, 100, 0), oneWay: true);
        Assert.Empty(DetourCauses.Find(Ctx(l, Input())));
    }

    [Fact]
    public void Clean_Two_Way_Corridor_And_Empty_Layout_Have_No_Findings()
    {
        var l = Ring(L.Line("P1", 0, 0, 50, 0), L.Line("P2", 50, 0, 100, 0));
        Assert.Empty(DetourCauses.Find(Ctx(l, Input())));
        Assert.Empty(DetourCauses.Find(Ctx(L.Of(), Input())));
    }

    [Fact]
    public void Standby_Trip_Is_A_Leg_And_A_Shared_Leg_Is_Reported_Once()
    {
        // standby S sits at the far end of the reversed path P2: S -> A runs the long way round
        var l = Ring(L.Line("P1", 0, 0, 50, 0), L.Line("P2", 100, 0, 50, 0));
        l.Cps.Add(L.Cp("S", 99, 0));
        var p = Input(standby: "S");
        p.Materials.Add(new MaterialInfo("M8", "S2", "G1", 60, 1, 1, 1, 1, null, null, false));
        p.Routes.Add(new Route("M8", "A", "B", null, null, null, 0));
        var wheres = DetourCauses.Find(Ctx(l, p)).Select(x => x.Where).ToList();
        Assert.Contains("A→B", wheres); Assert.Contains("B→S", wheres);
        Assert.Equal(wheres.Distinct().Count(), wheres.Count);
    }

    [Fact]
    public void One_Way_Loop_With_A_Long_Standby_Return_Has_No_Findings()
    {
        // S just before A on the loop: B -> S goes 260 m round against 140 m the other way, yet every single flip strands a leg
        var l = Ring(L.Line("P1", 0, 0, 50, 0), L.Line("P2", 50, 0, 100, 0), oneWay: true);
        l.Cps.Add(L.Cp("S", 0, 50));
        Assert.Empty(DetourCauses.Find(Ctx(l, Input(standby: "S"))));
    }

    [Fact]
    public void One_Mis_Drawn_Path_In_A_Consistent_Loop_Is_Named()
    {
        var l = Ring(L.Line("P1", 0, 0, 50, 0), L.Line("P2", 100, 0, 50, 0), oneWay: true); // the rest runs P1 -> P2 -> P3 ...
        l.Cps.Add(L.Cp("S", 0, 50));
        var f = DetourCauses.Find(Ctx(l, Input(standby: "S"))).ToList();
        Assert.Contains(f, x => x.Where == "A→B" && x.Title == "M7 도달 불가" && x.Fix == "P2 방향 확인");
        Assert.All(f, x => Assert.True(x.Fix is "P2 방향 확인" or "경로 연결 확인")); // B -> S is stranded too, but no single flip on its own route helps
    }

    [Fact]
    public void Every_Load_Cp_Of_A_List_Is_Checked()
    {
        var l = Ring(L.Line("P1", 0, 0, 50, 0), L.Line("P2", 100, 0, 50, 0)); // P2 drawn against the corridor
        l.Cps.Add(L.Cp("C", 60, 0));
        var p = Input();
        p.Routes[0] = p.Routes[0] with { SupplyFromCPs = new[] { "A", "C" } };
        var where = DetourCauses.Find(Ctx(l, p)).Select(f => f.Where).ToList();
        Assert.Contains("A→B", where); Assert.Contains("C→B", where);
    }
}
