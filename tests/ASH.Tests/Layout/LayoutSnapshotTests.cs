using ASH.Core.Geometry;
using ASH.Core.Layout;

namespace ASH.Tests.Layout;

public class LayoutSnapshotTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "ash_ls_" + Guid.NewGuid().ToString("N") + ".tsv");

    static ASH.Core.Layout.Layout Sample()
    {
        var l = new ASH.Core.Layout.Layout { Tolerance = 0.1 + 0.2 };
        l.Paths.Add(new LayoutPath("p1") { Id = "h1", Segments = { new LineSeg(new Point2(0, 0), new Point2(10.000000000000002, -1e-300)), new ArcSeg(new Point2(10, 5), new Point2(10, 0), 90) } });
        l.Paths.Add(new LayoutPath("p2") { Id = "h2", IsSpur = true, IsCrab = true, Segments = { new ArcSeg(new Point2(1, 1), new Point2(2, 1), -45.5) } });
        l.Paths.Add(new LayoutPath("p3") { Id = "h3", IsTwoWay = true, Segments = { new LineSeg(new Point2(1, 2), new Point2(3, 4)) } });
        l.Cps.Add(new LayoutCp { Name = "S001", Type = "standby", Group = "G1", Cap = 2, Position = new Point2(5, 0), Id = "c1", RouteId = "" });
        l.Cps.Add(new LayoutCp { Name = "L001", Type = "load", Group = "", Cap = 0, Position = new Point2(-1.5, 3.25), Id = "c2", RouteId = "r9" });
        l.Blocks.Add(new LayoutBlock { Name = "B1", Cap = 3, Rect = new Rect2(new Point2(-1, -2), new Point2(3, 4)), Id = "b1", TagId = "t1" });
        l.Blocks.Add(new LayoutBlock { Name = "B2", Cap = 1, Rect = new Rect2(new Point2(0, 0), new Point2(1, 1)), Id = "b2", TagId = "" });
        l.Stations.Add(new LayoutStation { Name = "ST1", Position = new Point2(7, 8) });
        l.Assets.Add(new LayoutAsset { Kind = AssetKinds.Rack, X = 1, Y = 2, RotDeg = 30, Sx = 2, Sy = 0.5 });
        l.Assets.Add(new LayoutAsset { Kind = AssetKinds.Wall, WidthM = 0.2, Segments = { new LineSeg(new Point2(0, 0), new Point2(5, 0)), new ArcSeg(new Point2(5, 1), new Point2(5, 0), 180) } });
        l.Conveyors.Add(new LayoutConveyor { Name = "CV001", WidthM = 0.6, Id = "v1", ConsumeAtM = 1.25, Segments = { new LineSeg(new Point2(0, 9), new Point2(4, 9)) } });
        l.Conveyors.Add(new LayoutConveyor { Name = "", GraphicOnly = true, Id = "v2", ConsumeAtM = null, Segments = { new LineSeg(new Point2(0, 10), new Point2(4, 10)) } });
        return l;
    }

    // every field: a property-wise dump, Segment and Point2 by record equality
    static string Dump(ASH.Core.Layout.Layout l) => string.Join("\n", new[] { "tol " + l.Tolerance.ToString("R") }
        .Concat(l.Paths.Select(p => $"path {p.Name} {p.Id} {p.IsSpur} {p.IsTwoWay} {p.IsCrab} {string.Join(";", p.Segments)}"))
        .Concat(l.Cps.Select(c => $"cp {c.Name} {c.Type} {c.Group} {c.Cap} {c.Position} {c.Id} [{c.RouteId}]"))
        .Concat(l.Blocks.Select(b => $"block {b.Name} {b.Cap} {b.Rect} {b.Id} [{b.TagId}]"))
        .Concat(l.Stations.Select(s => $"station {s.Name} {s.Position}"))
        .Concat(l.Assets.Select(a => $"asset {a.Kind} {a.X} {a.Y} {a.RotDeg} {a.Sx} {a.Sy} {a.WidthM} {string.Join(";", a.Segments)}"))
        .Concat(l.Conveyors.Select(v => $"conv [{v.Name}] {v.GraphicOnly} {v.WidthM} {v.Id} {v.ConsumeAtM?.ToString("R") ?? "null"} {string.Join(";", v.Segments)}")));

    [Fact]
    public void Round_Trip_Keeps_Every_Field()
    {
        var path = Tmp();
        try
        {
            var l = Sample();
            LayoutSnapshot.Write(l, path);
            var back = LayoutSnapshot.Read(path);
            Assert.Equal(Dump(l), Dump(back));
            Assert.Equal(l.Tolerance, back.Tolerance);
            Assert.Equal(l.Paths[0].Segments, back.Paths[0].Segments); // record equality, exact doubles
            Assert.Equal(l.Assets[1].Segments, back.Assets[1].Segments);
            Assert.Null(back.Conveyors[1].ConsumeAtM);
            Assert.Equal(1.25, back.Conveyors[0].ConsumeAtM);
            Assert.Equal("", back.Cps[0].RouteId);
            Assert.StartsWith("ash-layout\t1\n", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Empty_Layout_And_Tabs_In_Names()
    {
        var path = Tmp();
        try
        {
            LayoutSnapshot.Write(new ASH.Core.Layout.Layout(), path);
            var e = LayoutSnapshot.Read(path);
            Assert.Empty(e.Paths); Assert.Empty(e.Cps); Assert.Equal(0.001, e.Tolerance);
            var l = new ASH.Core.Layout.Layout();
            l.Stations.Add(new LayoutStation { Name = "a\tb\nc" });
            LayoutSnapshot.Write(l, path);
            Assert.Equal("a b c", LayoutSnapshot.Read(path).Stations[0].Name);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("cp\tX\tload\t\tone\t0\t0\tc\t")]          // cap not a number
    [InlineData("cp\tX\tload\t\t1\t0\t0\tc")]              // one field short
    [InlineData("path\tp\th\tq\tL\t0\t0\t1\t1")]           // unknown flag
    [InlineData("path\tp\th\t\tL\t0\t0\t1")]               // segment cut short
    [InlineData("conv\tc\t2\t0\tv\t\tL\t0\t0\t1\t1")]      // graphicOnly not 0/1
    [InlineData("zone\tz")]                                 // unknown kind
    public void Bad_Line_Throws_With_Its_Line_Number(string bad)
    {
        var path = Tmp();
        try
        {
            File.WriteAllText(path, "ash-layout\t1\ntol\t0.001\n" + bad + "\n");
            var ex = Assert.Throws<FormatException>(() => LayoutSnapshot.Read(path));
            Assert.Contains("3행", ex.Message);
            File.WriteAllText(path, "something else\n");
            Assert.Contains("1행", Assert.Throws<FormatException>(() => LayoutSnapshot.Read(path)).Message);
        }
        finally { File.Delete(path); }
    }
}
