using System.Text.RegularExpressions;
using ASH.Core.Dxf;
using ASH.Core.Film;
using ASH.Core.Model;
using ASH.Core.Geometry;

namespace ASH.Tests.Film;

public class StaticDrawingTests
{
    static string Arc(string units = "Meters")
    {
        var d = Path.Combine(Path.GetTempPath(), "ash_sd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "model.amo"), ModelAmoWriter.Render("m", units, 1, new[] { "agv" }, new Rect2(new Point2(0, 0), new Point2(1, 1))));
        return d;
    }
    static DxfSeg S(double a, double b, double c, double d, string l) => new(a, b, c, d, l);
    static DxfDrawing Dr(params DxfSeg[] s) => new(s, new DxfText[0], s.Select(x => x.Layer).Distinct().ToList());
    static string Asy(string arc) => File.ReadAllText(Path.Combine(arc, "ash_drw.asy"));
    static int Polylines(string asy) => Regex.Matches(asy, @"\(Polyline ").Count;

    [Fact]
    public void Writes_One_Polyline_Per_Disconnected_Run_And_Links_Model()
    {
        var arc = Arc();
        // 3 chained segments (one polyline) + 1 separate
        StaticDrawing.AddTo(arc, Dr(S(0, 0, 1, 0, "A"), S(1, 0, 1, 1, "A"), S(1, 1, 0, 1, "A"), S(5, 5, 6, 5, "A")), "Meters", null);
        var asy = Asy(arc);
        Assert.Equal(2, Polylines(asy));
        Assert.Contains("(Polyline ((0 0 0) (1 0 0) (1 1 0) (0 1 0)))", asy);
        Assert.Contains("511 81\r\n0 6 0 5 0 0\r\n", asy);
        Assert.Contains("template Meters", asy);
        var amo = File.ReadAllText(Path.Combine(arc, "model.amo"));
        Assert.Contains("STATIC name ash_drw\n\tSYSPOS endx 1\nPROCSYS", amo);
    }

    [Fact]
    public void Primitive_Count_Equals_Segment_Count_When_Unconnected()
    {
        var arc = Arc();
        StaticDrawing.AddTo(arc, Dr(Enumerable.Range(0, 50).Select(i => new DxfSeg(i * 3, 0, i * 3 + 1, 0, "A")).ToArray()), "Meters", null);
        Assert.Equal(50, Polylines(Asy(arc)));
    }

    [Fact]
    public void Converts_Metres_To_Model_Units()
    {
        var arc = Arc("Millimeters");
        StaticDrawing.AddTo(arc, Dr(S(0, 0, 1.5, 2, "A")), "Millimeters", null);
        var asy = Asy(arc);
        Assert.Contains("(Polyline ((0 0 0) (1500 2000 0)))", asy);
        Assert.Contains("UNITS Millimeters Seconds", asy);
        Assert.Contains("template Millimeters", asy);
    }

    [Fact]
    public void Hidden_Layers_Are_Left_Out_And_Nothing_Left_Means_No_Files()
    {
        var arc = Arc();
        StaticDrawing.AddTo(arc, Dr(S(0, 0, 1, 0, "WALL"), S(0, 5, 1, 5, "DIM")), "Meters", new HashSet<string> { "dim" });
        var asy = Asy(arc);
        Assert.Equal(1, Polylines(asy));
        Assert.DoesNotContain("(0 5 0)", asy);

        var arc2 = Arc(); var before = File.ReadAllText(Path.Combine(arc2, "model.amo"));
        StaticDrawing.AddTo(arc2, Dr(S(0, 0, 1, 0, "DIM")), "Meters", new HashSet<string> { "DIM" });
        Assert.False(File.Exists(Path.Combine(arc2, "ash_drw.asy")));
        Assert.Equal(before, File.ReadAllText(Path.Combine(arc2, "model.amo")));
    }

    [Fact]
    public void Second_Call_Does_Not_Duplicate_The_Model_Line_And_Unknown_Units_Throw()
    {
        var arc = Arc();
        var d = Dr(S(0, 0, 1, 0, "A"));
        StaticDrawing.AddTo(arc, d, "Meters", null); StaticDrawing.AddTo(arc, d, "Meters", null);
        Assert.Single(Regex.Matches(File.ReadAllText(Path.Combine(arc, "model.amo")), "STATIC name ash_drw"));
        Assert.Throws<ArgumentException>(() => StaticDrawing.AddTo(Arc(), d, "Parsecs", null));
    }

    [Fact]
    public void Ten_Thousand_Segments_File_Size_Is_Reasonable()
    {
        var arc = Arc();
        StaticDrawing.AddTo(arc, Dr(Enumerable.Range(0, 10000).Select(i => new DxfSeg(i * 2.5, i % 7, i * 2.5 + 1.25, i % 7 + 0.5, "A")).ToArray()), "Millimeters", null);
        var len = new FileInfo(Path.Combine(arc, "ash_drw.asy")).Length;
        Console.WriteLine("10k unconnected segments: " + len + " bytes");
        Assert.InRange(len, 1, 1_500_000);
    }
}
