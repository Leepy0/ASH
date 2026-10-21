using System.Globalization;
using System.IO;
using System.Linq;
using ASH.Core.Export;
using ASH.Core.Input;
using ASH.Core.Model;
using Xunit;

namespace ASH.Tests.Export;

public class GeometryTests
{
    static string[] Demo()
    {
        using var s = typeof(LayoutExporter).Assembly.GetManifestResourceStream("ASH.Core.Export.Templates.agvs_vehseg.txt")!;
        return new StreamReader(s).ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
    }

    static double[] Dims(System.Collections.Generic.IReadOnlyList<string> lines, string name)
    {
        int i = lines.ToList().FindIndex(x => x.EndsWith(" " + name));
        Assert.True(i > 0, name);
        // name line, then position, rotation, scale, then the dimensions
        return lines[i + 4].Split(' ').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
    }

    [Fact]
    public void Body_Takes_Length_And_Width_In_Feet_With_Length_Along_Y()
    {
        var g = VehicleGeometry.Build(Demo(), "Standard", 1.2, 0.8, "Meters");
        var b = Dims(g, "Base2");
        Assert.Equal(0.8 * 3.280839895, b[0], 4); // width across (x)
        Assert.Equal(1.2 * 3.280839895, b[1], 4); // length along travel (y)
    }

    [Fact]
    public void A_Longer_Vehicle_Is_Not_A_Stretched_One()
    {
        var a = VehicleGeometry.Build(Demo(), "Conveyor", 1.2, 0.8, "Meters");
        var b = VehicleGeometry.Build(Demo(), "Conveyor", 2.4, 0.8, "Meters");
        Assert.Equal(Dims(a, "CV_Roller1")[1], Dims(b, "CV_Roller1")[1]);        // roller thickness stays
        Assert.Equal(Dims(a, "CV_Roller1")[4], Dims(b, "CV_Roller1")[4]);        // and height
        Assert.True(b.Count(x => x.Contains(" CV_Roller")) > a.Count(x => x.Contains(" CV_Roller"))); // more rollers instead
        Assert.Equal(Dims(a, "Base2")[4], Dims(b, "Base2")[4]);                   // chassis height stays
        Assert.Equal(Dims(a, "Base2")[0], Dims(b, "Base2")[0]);                   // width untouched
    }

    [Theory]
    [InlineData("Standard")] [InlineData("Conveyor")] [InlineData("Ant")] [InlineData("Magnet")]
    public void Child_Counts_Match_And_Bumpers_And_Link_Are_Kept(string kind)
    {
        var g = VehicleGeometry.Build(Demo(), kind, 1.4, 0.9, "Meters").ToList();
        int geom = g.FindIndex(x => x.EndsWith(" Geom1"));
        int declared = int.Parse(g[geom + 1]);
        // Geom1 children: every "310 21" / "310 5" primitive and every "700 21"/"700 51" assembly that sits directly under it
        // (the bumper assemblies and the lift joint count one each, their own parts are below them)
        int prims = g.Skip(geom + 2).Count(x => x == "310 21" || x == "310 5");
        int bumperParts = 8; // one, two, three, four in each of Bump1 and Bump2
        Assert.Equal(declared, prims - bumperParts + 2 /* Bump1, Bump2 */ + 1 /* Lift_Joint */);
        Assert.Contains(g, x => x.EndsWith(" Bump1")); Assert.Contains(g, x => x.EndsWith(" Bump2")); Assert.Contains(g, x => x.EndsWith(" Link1"));
        Assert.Equal("end", g.Single(x => x == "end"));
    }

    [Fact]
    public void Only_The_Ant_Gets_A_Lift_Plate()
    {
        var ant = VehicleGeometry.Build(Demo(), "Ant", 1.2, 0.8, "Meters").ToList();
        var std = VehicleGeometry.Build(Demo(), "Standard", 1.2, 0.8, "Meters").ToList();
        double Top(System.Collections.Generic.List<string> g) { int i = g.FindIndex(x => x.EndsWith(" Top")); return double.Parse(g[i + 1].Split(' ')[0], CultureInfo.InvariantCulture); }
        Assert.True(Top(ant) > 1.0); Assert.True(Top(std) < 0.2);
    }

    [Fact]
    public void No_Kind_Unknown_Unit_Or_Bad_Size_Keep_The_Demo_Picture()
    {
        var demo = Demo();
        Assert.Same(demo, VehicleGeometry.Build(demo, "", 1.2, 0.8, "Meters"));
        Assert.Same(demo, VehicleGeometry.Build(demo, "Standard", 1.2, 0.8, "Furlongs"));
        Assert.Same(demo, VehicleGeometry.Build(demo, "Standard", 0, 0.8, "Meters"));
        Assert.Same(demo, VehicleGeometry.Build(demo, "Hover", 1.2, 0.8, "Meters"));
    }

    const double FtPerM = 3.280839895;

    static double Num(string line, int field = 0) => double.Parse(line.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries)[field], CultureInfo.InvariantCulture);
    static double ZOf(System.Collections.Generic.IReadOnlyList<string> g, string name) { int i = g.ToList().FindIndex(x => x.EndsWith(" " + name)); Assert.True(i > 0, name); return Num(g[i + 1], 2); }

    [Theory]
    [InlineData("Standard")] [InlineData("Conveyor")] [InlineData("Ant")] [InlineData("Magnet")]
    public void Every_Kind_Has_A_Tri_Colour_Lamp_On_Both_End_Faces(string kind)
    {
        var g = VehicleGeometry.Build(Demo(), kind, 1.4, 0.9, "Meters");
        foreach (var end in new[] { "F", "R" })
        {
            Assert.Contains(g, x => x.EndsWith(" Lamp" + end + "R")); Assert.Contains(g, x => x.EndsWith(" Lamp" + end + "Y")); Assert.Contains(g, x => x.EndsWith(" Lamp" + end + "G"));
        }
        // red 1, yellow 3, green 2
        Assert.Contains("1 1 0 1 0 LampFR", g); Assert.Contains("3 3 0 1 0 LampFY", g); Assert.Contains("2 2 0 1 0 LampFG", g);
        // the two faces are mirror images: same height, opposite sides along the travel axis
        double yF = Num(g[g.ToList().FindIndex(x => x.EndsWith(" LampFY")) + 1], 1), yR = Num(g[g.ToList().FindIndex(x => x.EndsWith(" LampRY")) + 1], 1);
        Assert.Equal(-yF, yR, 6); Assert.True(yF > 0);
        Assert.Equal(ZOf(g, "LampFY"), ZOf(g, "LampRY"), 6);
    }

    [Fact]
    public void The_Vehicle_Is_Symmetric_Front_To_Back()
    {
        var std = VehicleGeometry.Build(Demo(), "Standard", 1.4, 0.9, "Meters"); var mag = VehicleGeometry.Build(Demo(), "Magnet", 1.4, 0.9, "Meters");
        Assert.Contains(std, x => x.EndsWith(" BumperF")); Assert.Contains(std, x => x.EndsWith(" BumperR"));
        Assert.Contains(mag, x => x.EndsWith(" MagnetF")); Assert.Contains(mag, x => x.EndsWith(" MagnetR"));
        foreach (var (a, b) in new[] { ("BumperF", "BumperR"), ("WheelFR", "WheelRR"), ("WheelFL", "WheelRL") })
            Assert.Equal(ZOf(std, a), ZOf(std, b), 6);
    }

    [Theory]
    [InlineData("Standard")] [InlineData("Conveyor")]
    public void Standard_And_Conveyor_Are_600_Mm_High(string kind)
    {
        var g = VehicleGeometry.Build(Demo(), kind, 1.2, 0.8, "Meters").ToList();
        double floor = -2.5, topZ = floor + 0.6 * FtPerM;
        // the highest surface of any part is the load surface at 600 mm
        double high = double.NegativeInfinity;
        for (int i = 0; i < g.Count - 5; i++)
            if ((g[i] == "310 21") && g[i + 1].Contains(" 0 1 0 ") && !g[i + 1].Contains("Lamp") && !g[i + 1].Contains("Bump") && !g[i + 1].Contains("one") && !g[i + 1].Contains("two") && !g[i + 1].Contains("three") && !g[i + 1].Contains("four"))
                high = System.Math.Max(high, Num(g[i + 2], 2) + Num(g[i + 5], 4));
        Assert.Equal(topZ, high, 4);
        // loads are attached on that surface
        Assert.Contains(g, x => x.Trim() == "0  0  " + (floor + 0.6 * FtPerM).ToString("0.######", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Ant")] [InlineData("Magnet")]
    public void Ant_And_Magnet_Attach_Loads_At_The_Floor_So_Trolley_Casters_Share_The_Wheel_Line(string kind)
    {
        var g = VehicleGeometry.Build(Demo(), kind, 1.0, 0.7, "Meters").ToList();
        Assert.Contains(g, x => x.Trim() == "0  0  -2.5");
        Assert.Equal(-2.5, ZOf(g, "WheelFR"), 6);                                // the wheels stand on the floor line (z = -2.5)
        var trolley = Load("Trolley", false, 1.0, 0.7, 0.25);
        Assert.Equal(0, Num(trolley[trolley.ToList().FindIndex(x => x.EndsWith(" Caster1")) + 1], 2), 6); // casters at the load origin = the attach point
    }

    static System.Collections.Generic.IReadOnlyList<string> Load(string form, bool empty, double l, double w, double h, string units = "Meters") =>
        LoadGeometry.Build(form, empty, l, w, h, units)!;

    [Theory]
    [InlineData("Box", false)] [InlineData("Box", true)] [InlineData("Pallet", false)] [InlineData("Trolley", false)]
    public void Load_Child_Count_Matches(string form, bool empty)
    {
        var g = Load(form, empty, 0.8, 0.6, 0.3);
        Assert.Equal("700 17", g[0]);
        Assert.Equal(g.Count(x => x == "310 21"), int.Parse(g[2]));
    }

    [Fact]
    public void Box_Empty_Differs_From_Full_And_Uses_Its_Own_Size()
    {
        var full = Load("Box", false, 0.9, 0.5, 0.4); var empty = Load("Box", true, 0.9, 0.5, 0.4);
        Assert.NotEqual(full.Count, empty.Count);
        Assert.Contains("0.9 0.5 0.9 0.5 0.4 0 0", full);
    }

    [Fact]
    public void Load_Sizes_Convert_To_The_Model_Unit_And_Parts_Keep_Their_Own_Size()
    {
        var m = Load("Pallet", false, 1.1, 1.1, 0.15, "Meters"); var mm = Load("Pallet", false, 1.1, 1.1, 0.15, "Millimeters");
        Assert.Contains("1100 100 1100 100 120 0 0", mm);
        var big = Load("Pallet", false, 2.2, 1.1, 0.15);
        Assert.Contains("2.2 0.1 2.2 0.1 0.12 0 0", big);          // stringers longer, not thicker
        Assert.Equal(m.Count, big.Count);
        Assert.Null(LoadGeometry.Build("Box", false, 1, 1, 1, "Furlongs"));
    }

    [Fact]
    public void Logic_Sets_The_Load_Type_And_Box_Goes_Back_Empty()
    {
        var p = LogicGeneratorTests();
        var m = ASH.Core.Model.LogicGenerator.BuildProcessModel(p, ASH.Tests.Model.LogicGeneratorTests.Layout, "Meters");
        Assert.Contains("L_m1", m.LoadTypes); Assert.Contains("L_m1e", m.LoadTypes);
        Assert.True(m.LoadGraphics.ContainsKey("L_m1") && m.LoadGraphics.ContainsKey("L_m1e"));
        Assert.Contains("set load type to L_m1" + (char)10, m.LogicSource);
        Assert.Contains("set load type to L_m1e", m.LogicSource);
    }

    static ProjectInput LogicGeneratorTests()
    {
        var p = ASH.Tests.Model.LogicGeneratorTests.Sample();
        for (int i = 0; i < p.Materials.Count; i++) p.Materials[i] = p.Materials[i] with { Form = "Box", EmptyReturn = true };
        for (int i = 0; i < p.Routes.Count; i++) p.Routes[i] = p.Routes[i] with { ReturnFromCP = "U001", ReturnToCP = "L001" };
        return p;
    }
}
