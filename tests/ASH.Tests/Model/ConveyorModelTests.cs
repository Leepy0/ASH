using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Tests.Layout;

namespace ASH.Tests.Model;

public class ConveyorModelTests
{
    /// <summary>CV001: 8 m east, a counter-clockwise quarter arc (radius 2 m), 5 m north (16.14 m); plus a picture-only conveyor.</summary>
    static List<LayoutConveyor> Convs() => new()
    {
        new LayoutConveyor { Name = "CV001", Id = "1A", Segments = { new LineSeg(new Point2(0, 15), new Point2(8, 15)),
            new ArcSeg(new Point2(8, 17), new Point2(8, 15), 90), new LineSeg(new Point2(10, 17), new Point2(10, 22)) } },
        new LayoutConveyor { GraphicOnly = true, WidthM = 1.0, Segments = { new LineSeg(new Point2(0, 25), new Point2(6, 25)), new ArcSeg(new Point2(6, 23), new Point2(6, 25), -90) } },
    };

    static ProjectInput Input(double? consumeAt = 10)
    {
        var p = LogicGeneratorTests.Sample();
        p.Materials[0] = p.Materials[0] with { TaktTime = 5, ConveyorFed = true };
        p.Conveyors.Add(new ConveyorInfo("CV001", "M1", 5, 1, 0.3, null, consumeAt, Row: 2));
        return p;
    }

    [Fact]
    public void Sections_Arcs_Transfers_Stations_And_Template()
    {
        var asy = ConveyorAsyWriter.Render(Convs(), Input().Conveyors, new ExportOptions());
        Assert.StartsWith("SYSTYPE Conveyor\nUNITS Meters Seconds\n", asy);
        Assert.Contains("CONVSECTION name CV001_s1 type T_CV001 width 0.6 Meters motor M_CV001 piece begx 0 begy 15 begz 0.76 endx 8 endy 15 endz 0.76 upz 1\n", asy);
        // counter-clockwise = positive tenths of a degree (the demo's -900 is clockwise); checked in AutoMod by the picture
        Assert.Contains("CONVSECTION name CV001_s2 type T_CV001 width 0.6 Meters motor M_CV001 piece cenx 8 ceny 17 cenz 0.76 begx 8 begy 15 begz 0.76 upz 1 angle 900\n", asy);
        Assert.Contains("piece cenx 6 ceny 23 cenz 0.76 begx 6 begy 25 begz 0.76 upz 1 angle -900\n", asy);
        Assert.Contains("CONVSECTION name G1_s1 type T_G1 width 1 Meters motor M_G1 ", asy);
        Assert.Contains("CONVTRANSFER name CV001_t1 type DefaultTransfer from CV001_s1 8 to CV001_s2 0\n", asy);
        Assert.Contains("CONVTRANSFER name CV001_t2 type DefaultTransfer from CV001_s2 3.14159265358979 to CV001_s3 0\n", asy);
        Assert.Contains("CONVTRANSFER name G1_t1 type DefaultTransfer from G1_s1 6 to G1_s2 0\n", asy);
        // entry one product length in, use point 10 m = 2 m into the arc, out at the end; the picture-only one has none
        Assert.Contains("CONVSTATION name CV001_in type DefaultStation at CV001_s1 0.5\n", asy);
        Assert.Contains("CONVSTATION name CV001_use type DefaultStation at CV001_s2 2\n", asy);
        Assert.Contains("CONVSTATION name CV001_out type DefaultStation at CV001_s3 5\n", asy);
        Assert.DoesNotContain("G1_in", asy);
        Assert.Contains("vel 0.3 Meters Seconds", asy);
        Assert.Contains("accum load stopsize 1 0.1 Meters movesize 1 0.1 Meters inductsize 1 0.1 Meters", asy);
        Assert.Contains("<GRAPHICS_TEMPLATE name=\"ASH_Roller_Green\">", asy);
        Assert.DoesNotContain("class1", asy);
        Assert.Equal(5, asy.Split('\n').Count(x => x == "\tATTACH_GT ASH_Roller_Green"));
        Assert.Contains("PATH_GT name CV001_s2\n\tATTACH_GT ASH_Roller_Green\n", asy);
    }

    [Fact]
    public void Feet_Model_And_Empty_ConsumeAt_Use_The_End()
    {
        var asy = ConveyorAsyWriter.Render(Convs(), Input(consumeAt: null).Conveyors, new ExportOptions(Units: "Feet"));
        Assert.Contains("CONVSECTION name CV001_s1 type T_CV001 width 1.968503937 Feet ", asy);
        Assert.Contains("piece begx 0 begy 49.212598425 begz 2.4934383202 endx 26.24671916 endy 49.212598425 endz 2.4934383202 upz 1", asy);
        Assert.Contains("vel 0.9842519685 Feet Seconds", asy);
        Assert.Contains("CONVSTATION name CV001_in type DefaultStation at CV001_s1 1.6404199475\n", asy);
        Assert.Contains("CONVSTATION name CV001_use type DefaultStation at CV001_s3 16.404199475\n", asy);
        Assert.Contains("CONVSTATION name CV001_out type DefaultStation at CV001_s3 16.404199475\n", asy);
    }

    // layout coordinates are drawing units (mm here): Origin and Scale convert them as LayoutExporter does for paths; WidthM is already metres
    [Fact]
    public void Drawing_Units_Go_Through_Origin_And_Scale()
    {
        var c = new LayoutConveyor { Name = "CV001", WidthM = 0.8, Segments = { new LineSeg(new Point2(1000, 15000), new Point2(9000, 15000)),
            new ArcSeg(new Point2(9000, 17000), new Point2(9000, 15000), 90) } };
        var o = new ExportOptions(Origin: new Point2(1000, 10000), Scale: 0.001);
        var asy = ConveyorAsyWriter.Render(new[] { c }, Input().Conveyors, o);
        Assert.Contains("width 0.8 Meters motor M_CV001 piece begx 0 begy 5 begz 0.76 endx 8 endy 5 endz 0.76 upz 1\n", asy);
        Assert.Contains("piece cenx 8 ceny 7 cenz 0.76 begx 8 begy 5 begz 0.76 upz 1 angle 900\n", asy);
        Assert.Contains("CONVSTATION name CV001_use type DefaultStation at CV001_s2 2\n", asy);
        Assert.Equal(8 + Math.PI, ConveyorAsyWriter.LengthM(c, 0.001), 6);
    }

    [Fact]
    public void Check_Rows_Against_Drawing()
    {
        var l = new ASH.Core.Layout.Layout { Conveyors = Convs() };
        l.Conveyors.Add(new LayoutConveyor { Name = "CV002", Segments = { new LineSeg(new Point2(0, 30), new Point2(5, 30)) } });
        l.Conveyors.Add(new LayoutConveyor { Segments = { new LineSeg(new Point2(0, 35), new Point2(5, 35)) } });
        var input = Input(consumeAt: 20);
        input.Conveyors.Add(new ConveyorInfo("CV009", "M2", 60, 1, 0.3, null, null, Row: 3));
        var f = ConveyorCheck.Check(input, l, 1.0);
        Assert.Contains(f, x => x.Code == FindingCode.InputError && x.Message.StartsWith("Conveyors 2행: ConsumeAt 20 m"));
        Assert.Contains(f, x => x.Code == FindingCode.InputError && x.Message.StartsWith("Conveyors 3행: 컨베이어 'CV009'이(가) 도면에 없습니다"));
        Assert.Contains(f, x => x.Code == FindingCode.InputWarning && x.Message.Contains("'CV002'") && x.EntityId == "");
        Assert.Contains(f, x => x.Code == FindingCode.InputWarning && x.Message.StartsWith("이름 없는 AGV_CONV 폴리선 1개"));
        Assert.Empty(ConveyorCheck.Check(Input(), new ASH.Core.Layout.Layout { Conveyors = Convs() }, 1.0));
    }

    // the drawing marker (AGVCONVUSE) wins over the Excel ConsumeAt; without it the Excel value, without both the end
    [Fact]
    public void Marker_Wins_Over_Excel_ConsumeAt()
    {
        var c = Convs();
        c[0].ConsumeAtM = 3;
        var asy = ConveyorAsyWriter.Render(c, Input(consumeAt: 10).Conveyors, new ExportOptions());
        Assert.Contains("CONVSTATION name CV001_use type DefaultStation at CV001_s1 3\n", asy);
        Assert.Contains("CONVSTATION name CV001_out type DefaultStation at CV001_s3 5\n", asy);
        asy = ConveyorAsyWriter.Render(c, Input(consumeAt: null).Conveyors, new ExportOptions());
        Assert.Contains("CONVSTATION name CV001_use type DefaultStation at CV001_s1 3\n", asy);
        c[0].ConsumeAtM = null;
        asy = ConveyorAsyWriter.Render(c, Input(consumeAt: 10).Conveyors, new ExportOptions());
        Assert.Contains("CONVSTATION name CV001_use type DefaultStation at CV001_s2 2\n", asy);
        asy = ConveyorAsyWriter.Render(c, Input(consumeAt: null).Conveyors, new ExportOptions());
        Assert.Contains("CONVSTATION name CV001_use type DefaultStation at CV001_s3 5\n", asy);
        // a marker past the end falls back to the end
        c[0].ConsumeAtM = 99;
        Assert.Equal(c[0].Segments.Sum(s => s.Length), ConveyorAsyWriter.UseM(c[0], Input().Conveyors[0], c[0].Segments.Sum(s => s.Length)), 9);
    }

    [Fact]
    public void Check_Marker_Differs_From_Excel_Is_Information_And_Marker_Is_Validated()
    {
        var c = Convs();
        c[0].ConsumeAtM = 12;
        var l = new ASH.Core.Layout.Layout { Conveyors = c };
        var f = ConveyorCheck.Check(Input(consumeAt: 10), l, 1.0);
        Assert.DoesNotContain(f, x => x.Code == FindingCode.InputError);
        Assert.Contains(f, x => x.Code == FindingCode.InputWarning && x.Message.Contains("도면 소비 지점 12 m") && x.Message.Contains("ConsumeAt 10 m"));
        // same value or no Excel value: nothing to say
        Assert.Empty(ConveyorCheck.Check(Input(consumeAt: 12), l, 1.0));
        Assert.Empty(ConveyorCheck.Check(Input(consumeAt: null), l, 1.0));
        // a marker too close to the start (inside the entry box) is an error, even when Excel is fine
        c[0].ConsumeAtM = 0.2;
        f = ConveyorCheck.Check(Input(consumeAt: 10), l, 1.0);
        Assert.Contains(f, x => x.Code == FindingCode.InputError && x.Message.StartsWith("Conveyors 2행: 소비 지점 마커 0.2 m"));
    }

    [Fact]
    public void Nearest_Projects_A_Point_Onto_The_Polyline()
    {
        var c = Convs()[0];
        var (d, p, a) = c.Nearest(new Point2(5, 16.2));          // beside the first line
        Assert.Equal(5, d, 6); Assert.Equal(5, p.X, 6); Assert.Equal(15, p.Y, 6); Assert.Equal(0, a, 3);
        (d, p, a) = c.Nearest(new Point2(11, 20));                // beside the last (north-going) line
        Assert.Equal(8 + Math.PI + 3, d, 6); Assert.Equal(1, Math.Abs(Math.Sin(a)), 3);
        (d, _, _) = c.Nearest(new Point2(-4, 15));                // before the start clamps to 0
        Assert.Equal(0, d, 6);
    }

    [Fact]
    public void Logic_Consumes_A_Conveyor_Material_By_Products_Only()
    {
        var input = Input();
        var src = LogicGenerator.Render(input, LogicGeneratorTests.Layout);
        Assert.DoesNotContain("dec V_stock(1) by V_need(1)\n\t\t\tif ac >= V_warmup", src); // not by the takt clock
        Assert.Contains("\t\t\tdec V_stock(2) by V_need(2)\n", src);                      // M2 still is
        Assert.Contains("begin P_cv_1 arriving procedure", src);
        Assert.Contains("move into ash_conv:CV001_in", src);
        Assert.Contains("travel to ash_conv:CV001_use", src);
        Assert.Contains("\tdec V_stock(1) by V_need(1)\n", src);
        // the line clock reads the takt from params.txt (M1's TaktTime holds the ProductTakt), so a run can change it without a rebuild
        Assert.Contains("\twait for V_takt(1) sec\n\tcreate 1 load of type L_prod to P_cv_1", src);
        Assert.Contains("\tcreate 1 load of type L_prod to P_cv_1\n", src.Substring(src.IndexOf("begin model initialization function")));
        Assert.Contains("print \"conveyor\\tCV001\\tM1\\t\" V_cvPass(1) \"\\t\" V_cvStop(1) \"\\t\" V_cvLate(1) to V_out", src);
        var pm = LogicGenerator.BuildProcessModel(input, LogicGeneratorTests.Layout, "Meters");
        Assert.Contains("L_prod", pm.LoadTypes);
        Assert.Contains(pm.Procs, p => p.Name == "P_cvsrc_1");
        Assert.Contains("13 13 0 1 1 Product", pm.LoadGraphics["L_prod"]);
        // without conveyors nothing of it
        var plain = LogicGenerator.BuildProcessModel(LogicGeneratorTests.Sample(), LogicGeneratorTests.Layout, "Meters");
        Assert.DoesNotContain("L_prod", plain.LoadTypes);
        Assert.DoesNotContain("P_cv_", plain.LogicSource);
    }

    [Fact]
    public void Builder_Adds_The_Conveyor_System_Only_With_Conveyors()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_cv_" + Guid.NewGuid().ToString("N"));
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("S001", 5, 0, "standby", "G1")); l.Cps.Add(L.Cp("L001", 10, 5, "load"));
        l.Cps.Add(L.Cp("B001", 6, 10, "buffer")); l.Cps.Add(L.Cp("U001", 2, 10, "unload"));
        l.Conveyors = Convs();
        var o = new ExportOptions(Groups: new[] { new VehicleGroupSpec("G1", "G1", 3, 1.0, 1, 1) });
        var r = ModelBuilder.Build(new ModelBuildRequest(dir, "m", Input(), l, o), null, TimeSpan.FromSeconds(1));
        Assert.DoesNotContain(r.Findings, f => f.Code == FindingCode.InputError);
        Assert.True(File.Exists(Path.Combine(r.ArcDir, "ash_conv.asy")));
        Assert.Contains("MOVESYS name agv\n\tSYSPOS endx 1\nMOVESYS name ash_conv\n", File.ReadAllText(Path.Combine(r.ArcDir, "model.amo")));
        Assert.Contains("maxy 27.5 ", File.ReadAllText(Path.Combine(r.ArcDir, "model.amo"))); // the drawing area takes the conveyors in

        l.Conveyors = new();
        r = ModelBuilder.Build(new ModelBuildRequest(dir, "m", LogicGeneratorTests.Sample(), l, o), null, TimeSpan.FromSeconds(1));
        Assert.False(File.Exists(Path.Combine(r.ArcDir, "ash_conv.asy")));
        Assert.DoesNotContain("ash_conv", File.ReadAllText(Path.Combine(r.ArcDir, "model.amo")));

        // a Conveyors row without its polyline stops the build
        r = ModelBuilder.Build(new ModelBuildRequest(dir, "m", Input(), l, o), null, TimeSpan.FromSeconds(1));
        Assert.Contains(r.Findings, f => f.Code == FindingCode.InputError && f.Message.Contains("CV001"));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Result_Conveyor_Rows_Parse()
    {
        var tsv = Path.GetTempFileName();
        File.WriteAllText(tsv, "kind\tname\tgroup\tvalue1\tvalue2\tvalue3\nconveyor\tCV001\tM1\t240\t2840.5\t2040\nclock\tclock\t\t0\t3600\t0\n");
        var r = ResultParser.Parse(tsv);
        Assert.Equal(new ConveyorResult("CV001", "M1", 240, 2840.5, 2040), Assert.Single(r.Conveyors!));
        File.Delete(tsv);
    }
}
