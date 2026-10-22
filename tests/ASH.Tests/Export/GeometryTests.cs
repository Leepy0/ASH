using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
    public void Only_The_Ant_Has_A_Top_Plate_For_The_Trolley()
    {
        var ant = VehicleGeometry.Build(Demo(), "Ant", 1.2, 0.8, "Meters");
        Assert.Contains(ant, x => x.EndsWith(" TopPlate"));
        Assert.DoesNotContain(VehicleGeometry.Build(Demo(), "Standard", 1.2, 0.8, "Meters"), x => x.EndsWith(" TopPlate"));
        double plateTop = ZOf(ant, "TopPlate") + Num(ant[ant.ToList().FindIndex(x => x.EndsWith(" TopPlate")) + 4], 4);
        Assert.Equal(-2.5 + 0.15 * FtPerM, plateTop, 5);                          // 150 mm body height
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
        Assert.Contains("1 1 0 1 1 LampFR", g); Assert.Contains("3 3 0 1 1 LampFY", g); Assert.Contains("2 2 0 1 1 LampFG", g); // display 1: solid
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
        Assert.DoesNotContain(mag, x => x.EndsWith(" MagnetF")); Assert.Contains(mag, x => x.EndsWith(" MagnetR")); // magnet module at the rear only
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
            if ((g[i] == "310 21") && g[i + 1].Contains(" 0 1 1 ") && !g[i + 1].Contains("Lamp") && !g[i + 1].Contains("Bump") && !g[i + 1].Contains("one") && !g[i + 1].Contains("two") && !g[i + 1].Contains("three") && !g[i + 1].Contains("four"))
                high = System.Math.Max(high, Num(g[i + 2], 2) + Num(g[i + 5], 4));
        Assert.Equal(topZ, high, 4);
        // loads are attached on that surface
        Assert.Contains(g, x => x.Trim() == "0  0  " + (floor + 0.6 * FtPerM).ToString("0.######", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Ant_Wheels_Hang_Below_The_Lower_Plate_And_The_Trolley_Stands_On_The_Top_Plate()
    {
        var g = VehicleGeometry.Build(Demo(), "Ant", 1.0, 0.7, "Meters");
        int w = g.ToList().FindIndex(x => x.EndsWith(" WheelFR")), b = g.ToList().FindIndex(x => x.EndsWith(" Base2"));
        Assert.Equal(-2.5, ZOf(g, "WheelFR"), 6);                                              // the wheels stand on the floor line
        double wheelTop = ZOf(g, "WheelFR") + Num(g[w + 4], 4), plateBottom = ZOf(g, "Base2");
        Assert.True(wheelTop <= plateBottom + 1e-6, "wheels below the lower plate");
        string attach = "0  0  " + (-2.5 + 0.15 * FtPerM).ToString("0.######", CultureInfo.InvariantCulture);
        Assert.Contains(g, x => x.Trim() == attach);                                           // the trolley's origin (its casters) on the top plate
        var trolley = Load("Trolley", false, 1.0, 0.7, 0.25);
        Assert.Equal(0, Num(trolley[trolley.ToList().FindIndex(x => x.EndsWith(" Caster1")) + 1], 2), 6);
    }

    [Fact]
    public void Magnet_Body_Is_600_Mm_And_Its_Trolley_Stands_On_The_Floor_Behind_The_Rear_Magnet()
    {
        var g = VehicleGeometry.Build(Demo(), "Magnet", 1.2, 0.8, "Meters");
        double top = ZOf(g, "Base2") + Num(g[g.ToList().FindIndex(x => x.EndsWith(" Base2")) + 4], 4);
        Assert.Equal(-2.5 + 0.6 * FtPerM, top, 5);
        string attach = "0  " + (-(1.2 / 2 + 0.10 + 0.5) * FtPerM).ToString("0.######", CultureInfo.InvariantCulture) + "  -2.5";
        Assert.Contains(g, x => x.Trim() == attach);
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
        Assert.Contains("0.5 0.9 0.5 0.9 0.4 0 0", full); // width across (x), length along travel (y)
    }

    [Fact]
    public void Load_Sizes_Convert_To_The_Model_Unit_And_Parts_Keep_Their_Own_Size()
    {
        var m = Load("Pallet", false, 1.1, 1.1, 0.15, "Meters"); var mm = Load("Pallet", false, 1.1, 1.1, 0.15, "Millimeters");
        Assert.Contains("100 1100 100 1100 120 0 0", mm);
        var big = Load("Pallet", false, 2.2, 1.1, 0.15);
        Assert.Contains("0.1 2.2 0.1 2.2 0.12 0 0", big);          // stringers longer, not thicker
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

    [Fact]
    public void Load_Shows_From_Each_Pickup_To_Its_Set_Down_Only()
    {
        var t = ASH.Core.Model.LogicGenerator.Render(LogicGeneratorTests(), ASH.Tests.Model.LogicGeneratorTests.Layout);
        var s = t.Substring(t.IndexOf("begin P_supply_1"), t.IndexOf("begin P_consume_2") - t.IndexOf("begin P_supply_1"));
        const string hide = @"scale to x 0\.001, y 0\.001, z 0\.001", show = @"scale to x 1, y 1, z 1";
        // hidden while the call waits and the vehicle runs to L001; full from L001 to U001; empty from the return pickup (U001) to the set-down (L001)
        Assert.Matches(hide + @"[\s\S]*?move into agv:S001[\s\S]*?travel to agv:L001[\s\S]*?" + show + @"\n\twait for[\s\S]*?travel to agv:U001[\s\S]*?" +
            @"set load type to L_m1e\n\t" + hide + @"[\s\S]*?travel to agv:U001[\s\S]*?" + show + @"\n\twait for[\s\S]*?travel to agv:L001[\s\S]*?wait for [\d.]+ sec\n\t" + hide +
            @"[\s\S]*?travel to agv:S001", s);
        Assert.Equal(2, Regex.Matches(s, show).Count); Assert.Equal(3, Regex.Matches(s, hide).Count);
        // no return pickup: the empty container rides on from U001 to ReturnTo, shown until it is set down there
        var p = LogicGeneratorTests(); p.Routes[0] = p.Routes[0] with { ReturnFromCP = null };
        var c = ASH.Core.Model.LogicGenerator.Render(p, ASH.Tests.Model.LogicGeneratorTests.Layout);
        c = c.Substring(c.IndexOf("begin P_supply_1"), c.IndexOf("begin P_consume_2") - c.IndexOf("begin P_supply_1"));
        Assert.Matches(@"set load type to L_m1e\n(?:(?!scale to)[\s\S])*?travel to agv:L001[\s\S]*?wait for [\d.]+ sec\n\t" + hide, c);
        Assert.Equal(2, Regex.Matches(c, hide).Count);
    }

    [Theory]
    [InlineData("Pallet")] [InlineData("Trolley")]
    public void Pallet_And_Trolley_Go_Back_Grey(string form)
    {
        var full = Load(form, false, 1.0, 0.7, 0.25); var empty = Load(form, true, 1.0, 0.7, 0.25);
        Assert.Equal(full.Count, empty.Count);
        Assert.DoesNotContain(full, x => x.StartsWith("8 8 ")); Assert.Contains(empty, x => x.StartsWith("8 8 0 1 1 ")); // light grey
        var p = LogicGeneratorTests(); p.Materials[0] = p.Materials[0] with { Form = form };
        var m = ASH.Core.Model.LogicGenerator.BuildProcessModel(p, ASH.Tests.Model.LogicGeneratorTests.Layout, "Meters");
        Assert.True(m.LoadGraphics.ContainsKey("L_m1e"));
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
