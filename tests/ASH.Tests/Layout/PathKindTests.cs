using System.Linq;
using ASH.Core.Asy;
using ASH.Core.Layout;
using ASH.Core.Traffic;

namespace ASH.Tests.Layout;

/// <summary>The four path types: one-way, two-way (GPATH "two"), spur, crab.</summary>
public class PathKindTests
{
    static LayoutPath Two(string name, double x1, double y1, double x2, double y2) { var p = L.Line(name, x1, y1, x2, y2); p.IsTwoWay = true; return p; }

    [Theory]
    [InlineData(false, false, false, "path")]
    [InlineData(false, true, false, "two")]
    [InlineData(true, false, false, "spur")]
    [InlineData(false, false, true, "crab")]
    [InlineData(true, false, true, "spur+crab")]
    [InlineData(false, true, true, "two+crab")]
    public void Kind_Code_Round_Trips(bool spur, bool two, bool crab, string code)
    {
        Assert.Equal(code, PathKinds.Encode(spur, two, crab));
        Assert.Equal((spur, two, crab), PathKinds.Decode(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("path")]
    [InlineData("nonsense")]
    public void Unknown_Code_Is_One_Way(string? code) => Assert.Equal((false, false, false), PathKinds.Decode(code));

    [Fact] public void Labels_Are_Korean() => Assert.Equal(new[] { "단방향", "양방향", "지선", "크랩", "지선+크랩" },
        new[] { PathKinds.Label(false, false, false), PathKinds.Label(false, true, false), PathKinds.Label(true, false, false), PathKinds.Label(false, false, true), PathKinds.Label(true, false, true) });

    [Fact] public void Asy_Writes_And_Reads_Two()
    {
        var line = AsyWriter.FormatPath(new GuidePath("p", "T", false, false, new LinePiece(new(0, 0), new(1, 0)), IsTwoWay: true));
        Assert.Equal("GPATH name p type T two piece begx 0 begy 0 endx 1 endy 0 upz 1", line);
        var sys = AsyReader.Read(new System.IO.StringReader("VERSION 12.6.1.19\nSYSTYPE AGV\n" + line + "\n"));
        var gp = Assert.Single(sys.Paths);
        Assert.True(gp.IsTwoWay);
        Assert.False(gp.IsSpur);
        Assert.False(gp.IsCrab);
    }

    [Fact] public void Spur_Without_Crab_Exports_Without_Crab()
    {
        var line = AsyWriter.FormatPath(new GuidePath("p", "T", true, false, new LinePiece(new(0, 0), new(1, 0))));
        Assert.Contains(" spur piece ", line);
        Assert.DoesNotContain("crab", line);
    }

    [Fact] public void Splitting_And_Copying_Keep_Two_Way_And_Crab()
    {
        var l = L.Of(Two("a", -10, 0, 10, 0), L.Line("b", 0, -10, 0, 10));
        l.Paths[0].IsCrab = true;
        var pieces = PathSplitter.SplitAll(l.Paths, l.Tolerance).Paths.Where(p => p.Name.StartsWith("a")).ToList();
        Assert.True(pieces.Count >= 2);
        Assert.All(pieces, p => { Assert.True(p.IsTwoWay); Assert.True(p.IsCrab); });
    }

    [Fact] public void Two_Way_End_Counts_As_In_And_Out()
    {
        // a lone path between two nodes would be "only outgoing" at its start; two-way paths can be left and entered at both ends
        var oneWay = L.Of(L.Line("a", 0, 0, 5, 0), L.Line("b", 0, 0, 0, 5));
        Assert.Contains(LayoutChecker.Check(oneWay, new CheckOptions()), f => f.Code == FindingCode.DeadEndNode);
        var twoWay = L.Of(Two("a", 0, 0, 5, 0), Two("b", 0, 0, 0, 5));
        Assert.DoesNotContain(LayoutChecker.Check(twoWay, new CheckOptions()), f => f.Code == FindingCode.DeadEndNode && f.Where == new ASH.Core.Geometry.Point2(0, 0));
    }

    [Fact] public void Two_Way_Path_Gets_A_Traffic_Cp_On_Both_Sides_Of_The_Block()
    {
        var p = new TrafficParams(VehicleLength: 2.0, VehicleWidth: 1.0, SafetyGap: 0.5); // claim distance 0.5
        var l = L.Of(Two("a", -10, 0, 10, 0));
        l.Blocks.Add(L.Block("B001", -3.5, -3.5, 3.5, 3.5));
        var r = TrafficCpGenerator.Generate(l, p);
        Assert.Equal(new[] { -4.0, 4.0 }, r.Cps.Select(c => c.Position.X).OrderBy(x => x).ToArray());

        var oneWay = L.Of(L.Line("a", -10, 0, 10, 0));
        oneWay.Blocks.Add(L.Block("B001", -3.5, -3.5, 3.5, 3.5));
        Assert.Equal(new[] { -4.0 }, TrafficCpGenerator.Generate(oneWay, p).Cps.Select(c => c.Position.X).ToArray());
    }
}
