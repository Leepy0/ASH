using ASH.Core.Dxf;

namespace ASH.Tests.Dxf;

public class DxfLiteTests
{
    static DxfDrawing Load(string name)
    {
        using var r = new StreamReader(Path.Combine(AppContext.BaseDirectory, "Dxf", "Fixtures", name));
        return DxfLite.Read(r);
    }

    [Fact]
    public void Line_And_Lwpolyline_Read()
    {
        var d = Load("lines.dxf");
        Assert.Contains(new DxfSeg(0, 0, 100, 0, "WALL"), d.Segs);
        // closed 3-vertex LWPOLYLINE -> 3 segments, the last one closing back to the start
        Assert.Contains(new DxfSeg(10, 10, 0, 0, "COL"), d.Segs);
        Assert.Contains(new DxfSeg(0, 0, 10, 0, "COL"), d.Segs);
        Assert.Equal(1 + 3 + 36, d.Segs.Count); // line + polyline + circle in 36 parts
        Assert.Equal(new[] { "WALL", "COL" }, d.Layers);
    }

    [Fact]
    public void Bulge_Arc_Is_Approximated_Within_1Percent()
    {
        var d = Load("bulge.dxf"); // semicircle from (0,0) to (10,0): centre (5,0) r 5, bulge +1 = counter-clockwise side... below the chord
        Assert.True(d.Segs.Count >= 8);
        var pts = d.Segs.Select(s => (X: s.X1, Y: s.Y1)).Append((X: d.Segs.Last().X2, Y: d.Segs.Last().Y2)).ToList();
        Assert.Equal((0.0, 0.0), pts[0]); Assert.Equal(10, pts.Last().X, 9);
        foreach (var (x, y) in pts) Assert.InRange(Math.Sqrt((x - 5) * (x - 5) + y * y), 5 * 0.99, 5 * 1.01);
        // mid-segment points must also stay near the circle (sagitta)
        foreach (var s in d.Segs) { double mx = (s.X1 + s.X2) / 2 - 5, my = (s.Y1 + s.Y2) / 2; Assert.InRange(Math.Sqrt(mx * mx + my * my), 5 * 0.99, 5.0001); }
        Assert.True(pts.All(p => p.Y <= 1e-9), "positive bulge runs counter-clockwise, so the arc bulges to the right of the travel direction (below)");
    }

    [Fact]
    public void Insert_Expands_Block_With_Scale_And_Rotation()
    {
        var d = Load("insert.dxf");
        var s = Assert.Single(d.Segs);
        Assert.Equal(100, s.X1, 9); Assert.Equal(200, s.Y1, 9); Assert.Equal(100, s.X2, 9); Assert.Equal(204, s.Y2, 9);
        Assert.Equal("EQ", s.Layer); // block layer "0" takes the INSERT's layer
    }

    [Fact]
    public void Mtext_Formatting_Is_Stripped()
    {
        var d = Load("mtext.dxf");
        Assert.Equal(new DxfText(5, 6, 2.5, @"창고 A B\C", "TXT"), d.Texts[0]);
        Assert.Equal("P°", d.Texts[1].Text);
    }

    [Fact]
    public void ToModel_Applies_Origin_And_Scale()
    {
        var d = new DxfDrawing(new[] { new DxfSeg(1000, 2000, 3000, 2000, "L") }, new[] { new DxfText(1500, 2500, 250, "t", "L") }, new[] { "L" });
        var m = DxfLite.ToModel(d, new ModelFrame("Millimeters", 1000, 2000, 0.001));
        Assert.Equal(new DxfSeg(0, 0, 2, 0, "L"), m.Segs[0]);
        Assert.Equal(new DxfText(0.5, 0.5, 0.25, "t", "L"), m.Texts[0]);
    }

    [Fact]
    public void Binary_Dxf_Throws()
    {
        var e = Assert.Throws<InvalidDataException>(() => DxfLite.Read(new StringReader("AutoCAD Binary DXF\r\n\u001a\0")));
        Assert.Equal("binary DXF", e.Message);
    }
}
