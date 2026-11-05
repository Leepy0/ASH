using System.Text;
using ASH.Core.Lite;

namespace ASH.Tests.Lite;

public class LiteStatTests
{
    const double M = 1000;

    [Fact]
    public void Histogram_Two_Wall_Lines_In_One_Cell_Is_About_2000()
    {
        var h = LiteStat.Histogram(new[] { (0.0, 100.0, 1000.0, 100.0), (0.0, 200.0, 1000.0, 200.0) }, M);
        Assert.Equal(1, h.Cells);
        Assert.Equal(2000, h.Max, 6);
        Assert.Equal(2000, h.P50, 6);
    }

    [Fact]
    public void Histogram_Twenty_Lines_At_50mm_Is_20000()
    {
        var segs = Enumerable.Range(0, 20).Select(i => (0.0, 25.0 + 50 * i, 1000.0, 25.0 + 50 * i));
        var h = LiteStat.Histogram(segs, M);
        Assert.Equal(1, h.Cells);
        Assert.Equal(20000, h.Max, 6);
        Assert.Equal(1, h.Bins[3]); // bins: [0,1k) [1k,5k) [5k,20k) [20k,100k) [100k,)
    }

    [Fact]
    public void Histogram_Splits_Length_Across_Cells_And_Total_Is_Kept()
    {
        // diagonal across 3 cells: lengths per cell sum to the full length
        var seg = (500.0, 500.0, 2500.0, 1500.0);
        double len = Math.Sqrt(2000 * 2000 + 1000 * 1000), sum = 0;
        int n = 0;
        LiteStat.WalkCells(seg.Item1, seg.Item2, seg.Item3, seg.Item4, M, (cx, cy, l) => { sum += l; n++; });
        Assert.Equal(len, sum, 6);
        Assert.True(n >= 3);
    }

    [Fact]
    public void WalkCells_Zero_Length_Segment_Reports_Nothing()
    {
        int n = 0;
        LiteStat.WalkCells(10, 10, 10, 10, M, (_, _, _) => n++);
        Assert.Equal(0, n);
    }

    [Fact]
    public void Percentiles_Use_Occupied_Cells_Only()
    {
        // ten cells with 100, 200 ... 1000 mm
        var segs = Enumerable.Range(1, 10).Select(i => (i * M * 1.0 + 0, 10.0, i * M + 100.0 * i, 10.0));
        var h = LiteStat.Histogram(segs, M);
        Assert.Equal(10, h.Cells);
        Assert.Equal(1000, h.Max, 6);
        Assert.Equal(500, h.P50, 6);
        Assert.Equal(900, h.P90, 6);
        Assert.Equal(1000, h.P99, 6);
        Assert.Equal(9, h.Bins[0]);
        Assert.Equal(1, h.Bins[1]); // 1000 mm sits on the edge: bins are [lo, hi)
    }

    [Fact]
    public void Segments_Of_Circle_And_Polyline_Are_Chords()
    {
        var circ = LiteStat.Segments(new LiteEntity[] { new LiteCircle("0", 0, 0, 1000) }, 1).ToList();
        double perim = circ.Sum(s => Math.Sqrt((s.Item3 - s.Item1) * (s.Item3 - s.Item1) + (s.Item4 - s.Item2) * (s.Item4 - s.Item2)));
        Assert.InRange(perim, 2 * Math.PI * 1000 * 0.999, 2 * Math.PI * 1000);
        var sq = LiteStat.Segments(new LiteEntity[] { new LitePolyline("0", new[] { new LiteVertex(0, 0), new LiteVertex(10, 0), new LiteVertex(10, 10) }, true) }, 1).ToList();
        Assert.Equal(3, sq.Count);
    }

    [Fact]
    public void BlockTable_Orders_By_Instances_Times_Lines_And_Caps_At_30()
    {
        var defs = Enumerable.Range(0, 40).Select(i => new LiteStat.BlockRow("B" + i, i + 1, 10, 100, 0)).ToList();
        defs.Add(new LiteStat.BlockRow("Big", 2, 1000, 5000, 3));
        var t = LiteStat.BlockTable(defs);
        Assert.Equal(30, t.Count);
        Assert.Equal("Big", t[0].Name);
        Assert.Equal("B39", t[1].Name);
        Assert.Equal(3, t[0].Solids3d);
    }

    [Fact]
    public void Write_Has_All_Sections()
    {
        var h = LiteStat.Histogram(new[] { (0.0, 100.0, 1000.0, 100.0) }, M);
        var sw = new StringWriter();
        LiteStat.Write(sw, "a.dwg", 12.5, 3, 4, true, 100, h, new[] { new LiteStat.BlockRow("B", 2, 5, 300, 0) }, new Dictionary<string, int> { ["Line"] = 7 });
        var s = sw.ToString();
        Assert.Contains("잘림	예", s);
        foreach (var k in new[] { "[도면]", "[시간]", "[밀도 히스토그램]", "[블록 상위 30]", "[종류별 개수]" }) Assert.Contains(k, s);
        Assert.Contains("Line\t7", s);
    }

    [Fact]
    public void TallyBlock_Keeps_Per_Definition_Values_And_Counts_Instances()
    {
        var src = new LiteSource();
        for (int i = 0; i < 3; i++) src.TallyBlock("B", 50, 2, 700);
        var t = src.Blocks["B"];
        Assert.Equal(3, t.Instances);
        Assert.Equal(50, t.Lines);
        Assert.Equal(2, t.Solids);
        var row = LiteStat.BlockTable(new[] { new LiteStat.BlockRow("B", t.Instances, t.Lines, t.Diagonal, t.Solids) })[0];
        Assert.Equal(150, (long)row.Instances * row.Lines);
        Assert.Equal(2, row.Solids3d);
    }

    [Fact]
    public void WalkCells_Extreme_Coordinates_Return_At_Once()
    {
        int n = 0;
        LiteStat.WalkCells(-1e300, 0, 1e300, 0, M, (_, _, _) => n++);
        LiteStat.WalkCells(0, 0, 1e15, 1, M, (_, _, _) => n++);
        Assert.Equal(0, n);
    }

    [Fact]
    public void Measured_Excludes_Xref_Hidden_And_Footprint_Items()
    {
        var e = new LiteLine("0", 0, 0, 10, 0);
        Assert.True(LiteSource.Measured(new LiteItem(e, LiteFlags.None)));
        foreach (var f in new[] { LiteFlags.Xref, LiteFlags.Hidden, LiteFlags.Footprint }) Assert.False(LiteSource.Measured(new LiteItem(e, f)));
        var src = new LiteSource { Tolerance = 1 };
        src.Items.Add(new LiteItem(e, LiteFlags.None)); src.Items.Add(new LiteItem(e, LiteFlags.Hidden));
        Assert.Equal(1, src.Chords(0));
    }
}
