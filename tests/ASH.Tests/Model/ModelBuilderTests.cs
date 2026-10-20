using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Model;
using ASH.Tests.Layout;

namespace ASH.Tests.Model;

public class ModelBuilderTests
{
    static ASH.Core.Layout.Layout Loop()
    {
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("S001", 5, 0, "standby", "G1")); l.Cps.Add(L.Cp("L001", 10, 5, "load"));
        l.Cps.Add(L.Cp("B001", 6, 10, "buffer")); l.Cps.Add(L.Cp("U001", 2, 10, "unload"));
        l.Blocks.Add(new ASH.Core.Layout.LayoutBlock { Name = "B001blk", Cap = 1, Rect = new Rect2(new Point2(8, -1), new Point2(11, 2)) });
        return l;
    }

    static ModelBuildRequest Req(string dir, ASH.Core.Input.ProjectInput input) =>
        new(dir, "m", input, Loop(), new ExportOptions(Groups: new[] { new VehicleGroupSpec("G1", "Tug", 3, 1.0, 1, 1) }));

    static string Tmp() => Path.Combine(Path.GetTempPath(), "ash_mb_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Validation_Error_Stops_Before_Any_File()
    {
        var dir = Tmp();
        var input = LogicGeneratorTests.Sample();
        input.Routes[0] = input.Routes[0] with { SupplyToCP = "NOPE" };
        var r = ModelBuilder.Build(Req(dir, input), null, TimeSpan.FromSeconds(1));
        Assert.Contains(r.Findings, f => f.Code == ASH.Core.Layout.FindingCode.InputError);
        Assert.Null(r.Build);
        Assert.False(Directory.Exists(r.ArcDir));
    }

    [Fact]
    public void Success_Writes_All_Files_And_Keeps_Existing_User_Ext()
    {
        var dir = Tmp();
        Directory.CreateDirectory(Path.Combine(dir, "m.arc"));
        File.WriteAllText(Path.Combine(dir, "m.arc", "user_ext.m"), "/* mine */\nbegin F_ext_on_call function\n\treturn true\nend\n");
        var r = ModelBuilder.Build(Req(dir, LogicGeneratorTests.Sample()), null, TimeSpan.FromSeconds(1));
        Assert.Null(r.Build);
        foreach (var f in new[] { "agv.asy", "traffic.tsv", "m~.asy", "model.amo", "params.txt", "user_ext.m" })
            Assert.True(File.Exists(Path.Combine(r.ArcDir, f)), f);
        var proc = File.ReadAllText(Path.Combine(r.ArcDir, "m~.asy"));
        Assert.Contains("BLOCK name B001blk", proc);
        Assert.Contains("/* mine */", proc);
        var amo = File.ReadAllText(Path.Combine(r.ArcDir, "model.amo"));
        Assert.Contains("autorep", amo);
        Assert.Contains("PROCSYS name m~", amo);
        Assert.Contains("OldBlockClaim", File.ReadAllText(Path.Combine(r.ArcDir, "agv.asy")));
        Assert.StartsWith("SEED 1\n", File.ReadAllText(Path.Combine(r.ArcDir, "params.txt")));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Amo_Render_Has_Control_And_Drawpos()
    {
        var s = ModelAmoWriter.Render("x", "Meters", 0.5, new[] { "agv" }, new Rect2(new Point2(-1, -2), new Point2(3, 4)));
        Assert.Contains("CONTROL snaplen 0.5 Hours counts 1 autorep", s);
        Assert.Contains("DRAWPOS minx -1 maxx 3 miny -2 maxy 4", s);
    }

    [Theory]
    [InlineData("")] [InlineData("a/b")] [InlineData("..")] [InlineData("a b")] [InlineData("a\\b")]
    public void Bad_Model_Name_Throws(string name) =>
        Assert.Throws<ArgumentException>(() => ModelBuilder.Build(Req(Tmp(), LogicGeneratorTests.Sample()) with { ModelName = name }, null, TimeSpan.Zero));

    [Fact]
    public void PrepareBuildDir_Deletes_Old_Dir()
    {
        var dir = Tmp(); Directory.CreateDirectory(Path.Combine(dir, "m.dir", "sub"));
        File.WriteAllText(Path.Combine(dir, "m.dir", "sub", "x"), "x");
        ModelBuilder.PrepareBuildDir(dir, "m");
        Assert.False(Directory.Exists(Path.Combine(dir, "m.dir")));
        ModelBuilder.PrepareBuildDir(dir, "m"); // missing dir is fine
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Block_Rect_Uses_Origin_And_Scale_And_Default_User_Ext_Is_Created()
    {
        var dir = Tmp();
        var req = Req(dir, LogicGeneratorTests.Sample());
        req = req with { AgvsOptions = req.AgvsOptions with { Origin = new Point2(1, 1), Scale = 2 } };
        var r = ModelBuilder.Build(req, null, TimeSpan.Zero);
        var proc = File.ReadAllText(Path.Combine(r.ArcDir, "m~.asy"));
        // rect (8,-1)-(11,2) -> (14,-4)-(20,2): centre 17,-1; size 6x6
        Assert.Contains("begx 17 begy -1 ", proc);
        Assert.Contains("scx 6 scy 6", proc);
        Assert.True(File.Exists(Path.Combine(r.ArcDir, "user_ext.m")));
        Assert.Contains("1 1\n", File.ReadAllText(Path.Combine(r.ArcDir, "params.txt"))); // G1 MinCount 1
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Input_Warnings_Are_Mirrored()
    {
        var input = LogicGeneratorTests.Sample();
        input.Materials[0] = input.Materials[0] with { CallThreshold = 50, UnloadThreshold = 1 };
        var r = ModelBuilder.Build(Req(Tmp(), input), null, TimeSpan.Zero);
        Assert.Equal(r.Findings.Count(f => f.Code == ASH.Core.Layout.FindingCode.InputWarning), r.Warnings.Count(w => r.Findings.Any(f => f.Message == w)));
    }
}

public class ServerAssemblyTests
{
    const string Seg = "AGVVEHSEG name Custom cap 2 pickup 0 Seconds setdown 0 Seconds\n\tfigcurspeed 100\n\tPAPATTACH\n\tPAP ind 0 \n";
    const string Veh = "AGVSVEH type Whatever numveh 99\n\tvehsegs item Other \n\tstart nlmine\n\tload Default \n\t\tvel\t0 7 Feet Seconds\n";

    static ASH.Core.Layout.Layout Loop()
    {
        var l = ASH.Tests.Layout.L.Of(ASH.Tests.Layout.L.Line("p1", 0, 0, 10, 0), ASH.Tests.Layout.L.Line("p2", 10, 0, 10, 10), ASH.Tests.Layout.L.Line("p3", 10, 10, 0, 10), ASH.Tests.Layout.L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(ASH.Tests.Layout.L.Cp("S001", 5, 0, "standby", "G1")); l.Cps.Add(ASH.Tests.Layout.L.Cp("L001", 10, 5, "load"));
        l.Cps.Add(ASH.Tests.Layout.L.Cp("B001", 6, 10, "buffer")); l.Cps.Add(ASH.Tests.Layout.L.Cp("U001", 2, 10, "unload"));
        return l;
    }

    static ExportOptions Opt(VehicleTemplateOverride? ov) =>
        new(Groups: new[] { new VehicleGroupSpec("G1", "Tug", 3, 1.0, 1, 1) }, VehicleOverrides: ov == null ? null : new Dictionary<string, VehicleTemplateOverride> { ["Tug"] = ov });

    [Fact]
    public void Export_Uses_VehSeg_Override_Verbatim_With_Renamed_Header()
    {
        var s = ASH.Core.Export.LayoutExporter.ToAgvs(Loop(), Opt(new VehicleTemplateOverride(VehSegText: Seg)));
        Assert.Equal(new[] { "AGVVEHSEG name Seg_Tug cap 2 pickup 0 Seconds setdown 0 Seconds", "\tfigcurspeed 100", "\tPAPATTACH", "\tPAP ind 0 " }, s.VehicleSegmentLines);
        Assert.Contains("vehsegs item Seg_Tug", string.Join("\n", s.VehicleLines)); // vehicle block stays default
    }

    [Fact]
    public void Export_Forces_Type_NumVeh_Segs_Start_In_Vehicle_Override()
    {
        var s = ASH.Core.Export.LayoutExporter.ToAgvs(Loop(), Opt(new VehicleTemplateOverride(VehicleBlockText: Veh)));
        Assert.Equal(new[] { "AGVSVEH type Tug numveh 3", "\tvehsegs item Seg_Tug ", "\tstart nl_G1", "\tload Default ", "\t\tvel\t0 7 Feet Seconds" }, s.VehicleLines);
    }

    [Theory]
    [InlineData("seg")] [InlineData("veh")]
    public void Override_Without_Keyword_Throws(string which)
    {
        var ov = which == "seg" ? new VehicleTemplateOverride(VehSegText: "junk\n") : new VehicleTemplateOverride(VehicleBlockText: Seg);
        Assert.Throws<ArgumentException>(() => ASH.Core.Export.LayoutExporter.ToAgvs(Loop(), Opt(ov)));
    }

    [Fact]
    public void Builder_Uses_Material_Overrides()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_sa_" + Guid.NewGuid().ToString("N"));
        var input = LogicGeneratorTests.Sample();
        var mats = input.Materials.Select(m => m with { CallThreshold = 7, UnloadThreshold = 9, InitStock = 8 }).ToList();
        var r = ModelBuilder.Build(new ModelBuildRequest(dir, "m", input, Loop(), Opt(null), MaterialOverrides: mats), null, TimeSpan.Zero);
        Assert.Contains("1 7 9 8 ", File.ReadAllText(Path.Combine(r.ArcDir, "params.txt")));
        Assert.Equal(2, input.Materials[0].CallThreshold); // original untouched
        Directory.Delete(dir, true);

        var r2 = ModelBuilder.Build(new ModelBuildRequest(dir, "m", input, Loop(), Opt(null), MaterialOverrides: mats.Take(1).ToList()), null, TimeSpan.Zero);
        Assert.Contains(r2.Findings, f => f.Code == ASH.Core.Layout.FindingCode.InputError);
        Assert.False(Directory.Exists(r2.ArcDir));
    }

    [Fact]
    public void Amo_AutoView_Token_Toggle()
    {
        var d = new Rect2(new Point2(0, 0), new Point2(1, 1));
        Assert.EndsWith("autorep\n", ModelAmoWriter.Render("x", "Meters", 1, new[] { "agv" }, d));
        Assert.Contains("autorep autoview", ModelAmoWriter.Render("x", "Meters", 1, new[] { "agv" }, d, autoView: true));
    }
}
