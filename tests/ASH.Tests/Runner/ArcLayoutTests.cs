using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Runner;
using ASH.Core.Traffic;
using ASH.Tests.Layout;

namespace ASH.Tests.Runner;

public class ArcLayoutTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "ash_arc_" + Guid.NewGuid().ToString("N"));

    /// <summary>Exports a square loop whose first side is two segments (so it becomes p1_s1/p1_s2) with one block on the p1/p2 corner.</summary>
    static string Export(out ASH.Core.Layout.Layout l)
    {
        var p1 = L.Line("p1", 0, 0, 5, 0);
        p1.Segments.Add(new LineSeg(new Point2(5, 0), new Point2(10, 0)));
        l = L.Of(p1, L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("S001", 7, 0, "standby", "G1"));
        l.Cps.Add(L.Cp("L001", 10, 5, "load"));
        l.Blocks.Add(L.Block("B_x1", 8, -1, 11, 2));
        var dir = Tmp();
        ExportRunner.Run(l, new ExportOptions(Groups: new[] { new VehicleGroupSpec("G1", "Tug", 1, 1.0, 1, 1) }), dir);
        return dir;
    }

    [Fact]
    public void Load_Rebuilds_Paths_Cps_Blocks_And_Traffic()
    {
        var dir = Export(out var src);
        var (l, t) = ArcLayout.Load(dir);
        Assert.Equal(new[] { "p1_s1", "p1_s2", "p2", "p3", "p4" }, l.Paths.Select(p => p.Name));
        Assert.All(l.Paths, p => Assert.Single(p.Segments));
        var s = l.Cps.Single(c => c.Name == "S001");
        Assert.Equal(7, s.Position.X, 6); Assert.Equal(0, s.Position.Y, 6);
        var b = l.Blocks.Single();
        Assert.Equal("B_x1", b.Name); Assert.Equal(1, b.Cap); Assert.Equal(8, b.Rect.Min.X, 6); Assert.Equal(2, b.Rect.Max.Y, 6);
        var srcTraffic = TrafficTable.Build(src);
        Assert.Contains("p1", srcTraffic.Entries.Select(e => e.PathName)); // exported name is the layout path, not the segment
        Assert.All(t.Entries, e => Assert.Contains(e.PathName, l.Paths.Select(p => p.Name)));
        Assert.Contains(t.Entries, e => e.PathName == "p1_s2" && e.BlockName == "B_x1");
        Directory.Delete(dir, true);
    }

    [Fact]
    public void A_Millimeters_Model_Of_A_Mm_Drawing_Reads_Back_In_Meters()
    {
        // the same loop drawn in mm and exported with Scale 0.001 into a Millimeters model: agv.asy holds mm, blocks.tsv / traffic.tsv meters
        var l = L.Of(L.Line("p1", 0, 0, 10000, 0), L.Line("p2", 10000, 0, 10000, 10000), L.Line("p3", 10000, 10000, 0, 10000), L.Line("p4", 0, 10000, 0, 0));
        l.Tolerance = 1;
        l.Cps.Add(L.Cp("S001", 7000, 0, "standby", "G1"));
        l.Blocks.Add(L.Block("B_x1", 8000, -1000, 11000, 2000));
        var dir = Tmp();
        ExportRunner.Run(l, new ExportOptions(Units: "Millimeters", Scale: 0.001, Groups: new[] { new VehicleGroupSpec("G1", "Tug", 1, 1.0, 1, 1) }), dir);
        Assert.Contains("GPATH name p1 ", File.ReadAllText(Path.Combine(dir, "agv.asy")));
        var (r, t) = ArcLayout.Load(dir);
        Assert.Equal(10, r.Paths.Single(p => p.Name == "p1").Length, 6);
        Assert.Equal(7, r.Cps.Single(c => c.Name == "S001").Position.X, 6);
        Assert.Equal(8, r.Blocks.Single().Rect.Min.X, 6);
        Assert.All(t.Entries, e => Assert.InRange(e.BoundaryDist, 0, 10));             // traffic boundaries on the same metre scale
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Arcs_RoundTrip_Through_Export_And_Load_Cw_And_Ccw()
    {
        var ccw = new ASH.Core.Layout.LayoutPath("a1") { Segments = { new ArcSeg(new Point2(0, 0), new Point2(5, 0), 90) } };
        var cw = new ASH.Core.Layout.LayoutPath("a2") { Segments = { new ArcSeg(new Point2(10, 0), new Point2(5, 0), -90) } };
        var l = L.Of(ccw, cw, L.Line("p3", 0, 5, 5, 5));
        l.Cps.Add(L.Cp("S001", 0, 5, "standby", "G1"));
        var dir = Tmp();
        ExportRunner.Run(l, new ExportOptions(Groups: new[] { new VehicleGroupSpec("G1", "Tug", 1, 1.0, 1, 1) }), dir);
        var (r, _) = ArcLayout.Load(dir);
        foreach (var src in new[] { ccw, cw })
        {
            var got = r.Paths.Single(p => p.Name == src.Name);
            Assert.Equal(src.End.X, got.End.X, 4); Assert.Equal(src.End.Y, got.End.Y, 4);
            Assert.Equal(src.Length, got.Length, 4);
            var a = src.PointAt(src.Length / 2); var b = got.PointAt(got.Length / 2);
            Assert.Equal(a.X, b.X, 4); Assert.Equal(a.Y, b.Y, 4);
        }
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Missing_Asy_Throws_And_Missing_Tsv_Is_Empty()
    {
        var dir = Tmp();
        Directory.CreateDirectory(dir);
        Assert.Throws<FileNotFoundException>(() => ArcLayout.Load(dir));
        Directory.Delete(dir, true);
        var d2 = Export(out _);
        File.Delete(Path.Combine(d2, "traffic.tsv")); File.Delete(Path.Combine(d2, "blocks.tsv"));
        var (l, t) = ArcLayout.Load(d2);
        Assert.NotEmpty(l.Paths); Assert.Empty(l.Blocks); Assert.Empty(t.Entries);
        Directory.Delete(d2, true);
    }

    [Fact]
    public void TrafficTable_ReadTsv_RoundTrips()
    {
        var t = new TrafficTable();
        t.Entries.Add(new BlockEntry("B1", "p1", 2.5, "T1"));
        t.Entries.Add(new BlockEntry("B1", "p2", 1, null));
        var f = Path.GetTempFileName();
        using (var w = new StreamWriter(f)) t.WriteTsv(w);
        var r = TrafficTable.ReadTsv(f);
        Assert.Equal(t.Entries, r.Entries);
        File.Delete(f);
    }

    [Fact]
    public void Loaded_Layout_Feeds_GroupClusters()
    {
        var dir = Export(out _);
        var (l, t) = ArcLayout.Load(dir);
        var input = ASH.Tests.Model.LogicGeneratorTests.Sample();
        input.Routes[0] = input.Routes[0] with { SupplyToCP = "S001", BufferCP = null };
        input.Routes[1] = input.Routes[0] with { Material = "M2" };
        var c = GroupClusters.Build(l, input, t);
        Assert.Single(c.Clusters); Assert.Empty(c.Warnings);
        Directory.Delete(dir, true);
    }
}
