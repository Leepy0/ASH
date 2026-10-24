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
        new(dir, "m", input, Loop(), new ExportOptions(Groups: new[] { new VehicleGroupSpec("G1", "G1", 3, 1.0, 1, 1) }));

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

    // the start-only CPs hold a vehicle each at time 0 and send it home (park list to the standby CP); they are no pickup, park or search target
    [Fact]
    public void Start_Cps_Get_Home_Park_Lists_But_Stay_Out_Of_Dispatch_Targets()
    {
        var dir = Tmp();
        var r = ModelBuilder.Build(new ModelBuildRequest(dir, "m", LogicGeneratorTests.Sample(), Loop(), new ExportOptions(Groups: ModelBuilder.VehicleGroups(LogicGeneratorTests.Sample()))), null, TimeSpan.FromSeconds(1));
        var s = ASH.Core.Asy.AsyReader.ReadFile(Path.Combine(r.ArcDir, "agv.asy"));
        Assert.Equal(new[] { "S001", "SP_G1_2", "SP_G1_3" }, s.NameLists["nls_G1"]); // Sample: 3 vehicles of 2 m, S001 at x 5 of the loop
        Assert.Equal(new[] { 2.5, 0.0 }, new[] { "SP_G1_2", "SP_G1_3" }.Select(n => Math.Round(s.ControlPoints.Single(c => c.Name == n).Dist, 6)));
        Assert.Equal(new[] { "S001" }, s.NameLists["nl_G1"]); Assert.Equal(new[] { "S001" }, s.NameLists["nl_pick_G1"]);
        Assert.Contains("PARKLST name SP_G1_2 Sequential item S001 ", s.ParkListLines);
        Assert.DoesNotContain(s.SearchListLines, x => x.Contains("SP_"));
        Assert.DoesNotContain(s.ParkListLines.Concat(s.WorkListLines), x => x.Contains("item SP_"));
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Result_Carries_The_Export_Diff_And_Backup()
    {
        var dir = Tmp();
        var first = ModelBuilder.Build(Req(dir, LogicGeneratorTests.Sample()), null, TimeSpan.Zero).Export!;
        Assert.Equal((4, 4, (string?)null), (first.Summary.PathsAdded, first.Summary.CpsAdded, first.BackupPath));
        var again = ModelBuilder.Build(Req(dir, LogicGeneratorTests.Sample()), null, TimeSpan.Zero).Export!;
        Assert.Equal((0, 0, 0), (again.Summary.PathsAdded, again.Summary.PathsRemoved, again.Summary.PathsMoved));
        Assert.True(File.Exists(again.BackupPath));
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
        Assert.Contains("scx 1.5 scy 1.5", proc); // AutoMod's default block box is 4 x 4
        Assert.True(File.Exists(Path.Combine(r.ArcDir, "user_ext.m")));
        Assert.Contains("1 1\n", File.ReadAllText(Path.Combine(r.ArcDir, "params.txt"))); // G1 MinCount 1
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Millimeters_Model_Scales_Blocks_View_Loads_And_Logic_Speed()
    {
        var dir = Tmp();
        var input = LogicGeneratorTests.Sample();
        input.AgvTypes[0] = input.AgvTypes[0] with { SpeedEmpty = 1.5 };
        var req = Req(dir, input);
        req = req with { AgvsOptions = req.AgvsOptions with { Units = "Millimeters", Scale = 1 } }; // the test layout is drawn in meters
        var r = ModelBuilder.Build(req, null, TimeSpan.Zero);
        var proc = File.ReadAllText(Path.Combine(r.ArcDir, "m~.asy"));
        Assert.Contains("UNITS Millimeters Seconds", proc);
        Assert.Contains("begx 9500 begy 500 ", proc);                      // block rect (8,-1)-(11,2) m in mm
        Assert.Contains("scx 750 scy 750", proc);
        Assert.Contains("set A_veh defined velocity to 1500", proc);       // SpeedEmpty 1.5 m/s
        Assert.Contains(" template Millimeters", proc);
        Assert.Contains(proc.Split('\n'), x => x == "400 600 400 600 300 0 0");  // the default 0.6 x 0.4 x 0.3 m box in mm (size line: y, then x)
        Assert.Matches(@"DRAWPOS minx -\d{4,}(\.\d+)? maxx \d{5,}", File.ReadAllText(Path.Combine(r.ArcDir, "model.amo")));
        Assert.Contains(r.Warnings, w => w.Contains("Units(Meters)"));    // the Excel's Units differs from the export's
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
        new(Groups: new[] { new VehicleGroupSpec("G1", "G1", 3, 1.0, 1, 1) }, VehicleOverrides: ov == null ? null : new Dictionary<string, VehicleTemplateOverride> { ["G1"] = ov });

    [Fact]
    public void Export_Uses_VehSeg_Override_Verbatim_With_Renamed_Header()
    {
        var s = ASH.Core.Export.LayoutExporter.ToAgvs(Loop(), Opt(new VehicleTemplateOverride(VehSegText: Seg)));
        Assert.Equal(new[] { "AGVVEHSEG name Seg_G1 cap 2 pickup 0 Seconds setdown 0 Seconds", "\tfigcurspeed 100", "\tPAPATTACH", "\tPAP ind 0 " }, s.VehicleSegmentLines);
        Assert.Contains("vehsegs item Seg_G1", string.Join("\n", s.VehicleLines)); // vehicle block stays default
    }

    [Fact]
    public void Export_Forces_Type_NumVeh_Segs_Start_In_Vehicle_Override()
    {
        var s = ASH.Core.Export.LayoutExporter.ToAgvs(Loop(), Opt(new VehicleTemplateOverride(VehicleBlockText: Veh)));
        Assert.Equal(new[] { "AGVSVEH type G1 numveh 3", "\tvehsegs item Seg_G1 ", "\tstart nls_G1", "\tload Default ", "\t\tvel\t0 7 Feet Seconds" }, s.VehicleLines);
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
        try
        {
            var r = ModelBuilder.Build(new ModelBuildRequest(dir, "m", input, Loop(), Opt(null), MaterialOverrides: mats), null, TimeSpan.Zero);
            Assert.Contains("1 7 9 8 ", File.ReadAllText(Path.Combine(r.ArcDir, "params.txt")));
            // thresholds and stocks are run-time values: the overrides leave the build fingerprint unchanged
            Assert.Equal(InputSignature.Compute(input), InputSignature.Read(r.ArcDir));
            Assert.Equal(4, input.Materials[0].CallThreshold); // original untouched
            Directory.Delete(dir, true);

            var r2 = ModelBuilder.Build(new ModelBuildRequest(dir, "m", input, Loop(), Opt(null), MaterialOverrides: mats.Take(1).ToList()), null, TimeSpan.Zero);
            Assert.Contains(r2.Findings, f => f.Code == ASH.Core.Layout.FindingCode.InputError);
            Assert.False(Directory.Exists(r2.ArcDir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData("dupGroup")]
    [InlineData("blankRoute")]
    public void Builder_Reports_Bad_Input_As_Findings_Without_Throwing(string which)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_bad_" + Guid.NewGuid().ToString("N"));
        var input = LogicGeneratorTests.Sample();
        if (which == "dupGroup") input.Groups.Add(input.Groups[0]);
        else input.Routes[0] = input.Routes[0] with { SupplyFromCP = "", SupplyToCP = "", BufferCP = null };
        try
        {
            var r = ModelBuilder.Build(new ModelBuildRequest(dir, "m", input, Loop(), Opt(null)), null, TimeSpan.Zero);
            Assert.Contains(r.Findings, f => f.Code == ASH.Core.Layout.FindingCode.InputError);
            Assert.Null(r.Build);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Signature_Formats_Doubles_Stably()
    {
        var a = LogicGeneratorTests.Sample(); var b = LogicGeneratorTests.Sample();
        a.AgvTypes[0] = a.AgvTypes[0] with { SpeedLoaded = 0.1 * 3 };   // 0.30000000000000004
        b.AgvTypes[0] = b.AgvTypes[0] with { SpeedLoaded = 0.3 };
        Assert.Equal(InputSignature.Compute(b), InputSignature.Compute(a)); // G15 drops last-bit noise the runtimes may print differently
        b.AgvTypes[0] = b.AgvTypes[0] with { SpeedLoaded = 0.31 };
        Assert.NotEqual(InputSignature.Compute(b), InputSignature.Compute(a));
    }

    [Fact]
    public void Signature_Changes_With_Curve_Speed_Only_When_Set()
    {
        var a = LogicGeneratorTests.Sample(); var b = LogicGeneratorTests.Sample();
        Assert.Equal(InputSignature.Compute(a), InputSignature.Compute(b));
        b.AgvTypes[0] = b.AgvTypes[0] with { SpeedLoadedCurve = 0, SpeedEmptyCurve = 0 };   // 0 = same as straight: same model
        Assert.Equal(InputSignature.Compute(a), InputSignature.Compute(b));
        b.AgvTypes[0] = b.AgvTypes[0] with { SpeedLoadedCurve = 0.5 };
        Assert.NotEqual(InputSignature.Compute(a), InputSignature.Compute(b));
        Assert.Equal(0.5, ModelBuilder.VehicleGroups(b).First(g => b.Groups.First(x => x.Group == g.Group).Type == b.AgvTypes[0].Type).SpeedCurve);
    }

    [Fact]
    public void Vehicle_Override_Without_Start_Line_Gets_Start_Inserted()
    {
        var s = ASH.Core.Export.LayoutExporter.ToAgvs(Loop(), Opt(new VehicleTemplateOverride(VehicleBlockText: "AGVSVEH\n\tload Default \n")));
        Assert.Equal(new[] { "AGVSVEH type G1 numveh 3", "\tvehsegs item Seg_G1", "\tstart nls_G1", "\tload Default " }, s.VehicleLines);
    }

    [Fact]
    public void VehSeg_Override_Without_Name_Throws() =>
        Assert.Throws<ArgumentException>(() => ASH.Core.Export.LayoutExporter.ToAgvs(Loop(), Opt(new VehicleTemplateOverride(VehSegText: "AGVVEHSEG cap 1\n"))));

    [Fact]
    public void Override_For_Unknown_Type_Warns()
    {
        var w = new List<string>();
        ASH.Core.Export.LayoutExporter.ToAgvs(Loop(), Opt(null) with { VehicleOverrides = new Dictionary<string, VehicleTemplateOverride> { ["Nope"] = new(VehSegText: Seg) } }, w);
        Assert.Contains(w, x => x.Contains("Nope"));
    }

    [Fact]
    public void Request_VehicleOverrides_Win_And_AutoView_Reach_Files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_sa_" + Guid.NewGuid().ToString("N"));
        try
        {
            var req = new ModelBuildRequest(dir, "m", LogicGeneratorTests.Sample(), Loop(), Opt(new VehicleTemplateOverride(VehSegText: Seg.Replace("Custom", "Loser"))), AutoView: true,
                VehicleOverrides: new Dictionary<string, VehicleTemplateOverride> { ["G1"] = new(VehSegText: Seg.Replace("cap 2", "cap 7")) });
            var r = ModelBuilder.Build(req, null, TimeSpan.Zero);
            Assert.Contains("AGVVEHSEG name Seg_G1 cap 7 ", File.ReadAllText(Path.Combine(r.ArcDir, "agv.asy")));
            Assert.Contains("autorep autoview", File.ReadAllText(Path.Combine(r.ArcDir, "model.amo")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Amo_AutoView_Token_Toggle()
    {
        var d = new Rect2(new Point2(0, 0), new Point2(1, 1));
        Assert.EndsWith("autorep\n", ModelAmoWriter.Render("x", "Meters", 1, new[] { "agv" }, d));
        Assert.Contains("autorep autoview", ModelAmoWriter.Render("x", "Meters", 1, new[] { "agv" }, d, autoView: true));
    }
}
