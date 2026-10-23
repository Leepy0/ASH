using System;
using System.Globalization;
using System.IO;
using System.Linq;
using ASH.Core.Cel;
using ASH.Core.Export;
using Xunit;

namespace ASH.Tests.Cel;

public class CelWriterTests
{
    static Box[] Two() => new[] { new Box("body", 0, 0, 0, 0.8, 1.2, 0.5), new Box("wheel", 0.4, -0.4, 0, 0.05, 0.1, 0.1, 0) };

    static string[] Template()
    {
        using var s = typeof(LayoutExporter).Assembly.GetManifestResourceStream("ASH.Core.Export.Templates.agvs_vehseg.txt")!;
        return new StreamReader(s).ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
    }

    static string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    [Fact]
    public void Cel_Has_Header_And_Child_Count()
    {
        var l = CelWriter.Cel("agv_cell", Two()).Split('\n');
        Assert.Equal("10000 21", l[0]); // cube.cel / gate C-G1: the top group is "10000 21"
        Assert.Contains(" agv_cell", l[1]);
        Assert.Equal("7000 21", l[6]);
        Assert.Equal("700 17", l[12]);
        Assert.Equal("2", l[14]);
        Assert.Equal(2, l.Count(x => x == "310 5"));
    }

    [Fact]
    public void Cel_Size_Line_Is_Y_First_In_Feet()
    {
        var l = CelWriter.Cel("c", Two()).Split('\n');
        var size = l[Array.IndexOf(l, "310 5") + 5].Split(' ');
        Assert.Equal(1.2 * 3.280839895, double.Parse(size[0], CultureInfo.InvariantCulture), 4);
        Assert.Equal(0.8 * 3.280839895, double.Parse(size[1], CultureInfo.InvariantCulture), 4);
    }

    [Fact]
    public void VehSeg_Child_Count_And_Container_Top()
    {
        var l = CelWriter.VehSeg(Template(), Two()).Split('\n');
        int g = Array.FindIndex(l, x => x.EndsWith(" Geom1"));
        Assert.Equal("3", l[g + 1]); // 2 boxes + Lift_Joint
        Assert.Contains(l, x => x.Contains("trz " + F(0.5 * 3.280839895)));
        Assert.StartsWith("AGVVEHSEG name DefSegment", l[0]);
    }

    [Fact]
    public void Reader_Round_Trips_Metres()
    {
        var b = CelReader.ReadBoxes(CelWriter.Cel("c", Two()));
        Assert.Equal(2, b.Count);
        Assert.Equal(1.2, b[0].Dy, 6);
        Assert.Equal(-0.4, b[1].Y, 6);
        Assert.Equal(0, b[1].Color);
    }

    [Fact]
    public void Reader_Throws_On_Short_Block()
    {
        var l = CelWriter.Cel("c", Two()).Split('\n');
        Assert.Throws<FormatException>(() => CelReader.ReadBoxes(string.Join("\n", l.Take(l.Length - 2))));
    }
}
