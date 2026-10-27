using System.Linq;
using ASH.Core.Cause;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Tests.Layout;

namespace ASH.Tests.Cause;

public class RouteViewTests
{
    static ProjectInput Input(bool emptyReturn = false, string? buffer = null, string? finish = null, params string[] standby)
    {
        var p = new ProjectInput();
        p.Groups.Add(new AgvGroup("G1", "T1", 1, 1, standby.ToList(), "Nearest", 0, finish));
        p.Materials.Add(new MaterialInfo("M7", "S1", "G1", 60, 1, 1, 1, 1, null, null, emptyReturn));
        p.Routes.Add(new Route("M7", "A", "B", buffer, emptyReturn ? "R1" : null, emptyReturn ? "R2" : null, 0));
        return p;
    }

    static ASH.Core.Layout.Layout Corridor(bool twoWay = true)
    {
        var line = L.Line("P1", 0, 0, 100, 0);
        line.IsTwoWay = twoWay;
        var l = L.Of(line);
        foreach (var (n, x) in new[] { ("S", 5.0), ("A", 10), ("K", 30), ("B", 50), ("R1", 70), ("R2", 90) }) l.Cps.Add(L.Cp(n, x, 0));
        return l;
    }

    [Fact]
    public void Straight_Supply_Leg_Has_Distance_Points_And_Numbered_Stops()
    {
        var r = RouteView.Build(Corridor(), Input(), "M7")!;
        var leg = Assert.Single(r.Legs);
        Assert.Equal(RouteLegKind.Supply, leg.Kind); Assert.Equal(40, leg.Distance!.Value, 6); Assert.Null(leg.Problem);
        Assert.Equal(new Point2(10, 0), leg.Points[0]); Assert.Equal(new Point2(50, 0), leg.Points.Last());
        Assert.Equal(new[] { ("A", "1"), ("B", "2") }, r.Stops.Select(s => (s.Name, s.Label)));
        Assert.Equal("공급 출발", r.Stops[0].Role);
    }

    [Fact]
    public void Buffer_Return_And_Standby_Legs_Get_Their_Kinds()
    {
        var r = RouteView.Build(Corridor(), Input(true, "K", "R2", "S"), "M7")!;
        Assert.Equal(new[] { ("A", "K", RouteLegKind.Supply), ("K", "B", RouteLegKind.Supply), ("B", "R1", RouteLegKind.Return), ("R1", "R2", RouteLegKind.Return), ("S", "A", RouteLegKind.Standby), ("R2", "S", RouteLegKind.Standby) },
            r.Legs.Select(l => (l.From, l.To, l.Kind)));
        Assert.All(r.Legs, l => Assert.Null(l.Problem));
        Assert.Equal(new[] { "A", "K", "B", "R1", "R2", "S" }, r.Stops.Select(s => s.Name));
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "" }, r.Stops.Select(s => s.Label));
        Assert.Equal("대기", r.Stops.Last().Role);
    }

    [Fact]
    public void Finish_Cp_Is_Listed_Without_Number()
    {
        var l = Corridor(); l.Cps.Add(L.Cp("F", 95, 0));
        var r = RouteView.Build(l, Input(false, null, "F"), "M7")!;
        var f = r.Stops.Last();
        Assert.Equal(("F", "종료", ""), (f.Name, f.Role, f.Label));
    }

    [Fact]
    public void Reversed_Path_Makes_The_Long_Way_Round()
    {
        // 100 x 100 ring: the corridor's second half runs against the leg, so A→B goes round three sides (320 m, as the detour check says)
        var p2 = L.Line("P2", 100, 0, 50, 0);
        var ps = new[] { L.Line("P1", 0, 0, 50, 0), p2, L.Line("P3", 100, 0, 100, 100), L.Line("P4", 100, 100, 0, 100), L.Line("P5", 0, 100, 0, 0) };
        foreach (var p in ps) p.IsTwoWay = p != p2;
        var l = L.Of(ps); l.Cps.Add(L.Cp("A", 10, 0)); l.Cps.Add(L.Cp("B", 90, 0));
        var leg = Assert.Single(RouteView.Build(l, Input(), "M7")!.Legs);
        Assert.Equal(320, leg.Distance!.Value, 6);
        Assert.Equal(new Point2(10, 0), leg.Points[0]); Assert.Equal(new Point2(90, 0), leg.Points.Last());
        foreach (var corner in new[] { new Point2(0, 0), new Point2(0, 100), new Point2(100, 100), new Point2(100, 0) }) Assert.Contains(corner, leg.Points);
        double len = 0;
        for (int i = 1; i < leg.Points.Count; i++) len += leg.Points[i - 1].DistanceTo(leg.Points[i]);
        Assert.Equal(320, len, 6); // the drawn polyline is as long as the stated distance
    }

    [Fact]
    public void Unreachable_Leg_Says_So()
    {
        var leg = Assert.Single(RouteView.Build(Corridor(false), Input(), "M7")!.Legs); // A→B forward is fine ...
        Assert.Equal(40, leg.Distance!.Value, 6);
        var back = Input(); back.Routes[0] = new Route("M7", "B", "A", null, null, null, 0);
        var l = Assert.Single(RouteView.Build(Corridor(false), back, "M7")!.Legs);
        Assert.Null(l.Distance); Assert.Empty(l.Points); Assert.Equal("B→A 도달 불가", l.Problem); // ... B→A runs against the one-way path
    }

    [Fact]
    public void Missing_Cp_And_Unknown_Material()
    {
        var p = Input(); p.Routes[0] = new Route("M7", "A", "Z", null, null, null, 0);
        var r = RouteView.Build(Corridor(), p, "M7")!;
        Assert.Equal("A→Z CP 없음 (Z)", Assert.Single(r.Legs).Problem);
        Assert.Null(r.Stops.Single(s => s.Name == "Z").Position);
        Assert.Null(RouteView.Build(Corridor(), Input(), "NOPE"));
    }

    [Fact]
    public void Arc_Is_Sampled_Into_Short_Steps()
    {
        var arc = new LayoutPath("P1") { IsTwoWay = true, Segments = { new ArcSeg(new Point2(0, 0), new Point2(10, 0), 90) } };
        var pts = RouteView.Sample(arc, 0, arc.Length);
        Assert.Equal(10, pts.Count); // 90 degrees in 9 steps of 10
        Assert.All(pts, p => Assert.Equal(10, p.DistanceTo(new Point2(0, 0)), 6));
        Assert.Equal(pts[0], RouteView.Sample(arc, arc.Length, 0).Last()); // either order
    }
}
