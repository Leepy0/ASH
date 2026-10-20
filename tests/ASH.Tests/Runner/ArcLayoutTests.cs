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
