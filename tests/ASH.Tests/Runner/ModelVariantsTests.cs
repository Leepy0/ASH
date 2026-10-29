using ASH.Core.AutoMod;
using ASH.Core.Cel;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Core.Settings;
using ASH.Tests.Layout;
using ASH.Tests.Model;
using ClosedXML.Excel;

namespace ASH.Tests.Runner;

[Collection("ExcelTemplateStatics")]
public class ModelVariantsTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "ash_mv_" + Guid.NewGuid().ToString("N"));

    static RunScenario Sc(string group, int n, int seed = 1) => new(seed, new Dictionary<string, int> { [group] = n });

    /// <summary>TwoGroups() with G2 circling its material (and optionally G1 too).</summary>
    static ProjectInput Cycle(bool g1Too = false)
    {
        var p = LogicGeneratorTests.TwoGroups();
        p.Groups[1] = p.Groups[1] with { SupplyMode = "Cycle", MaxCount = 4 };
        if (g1Too) p.Groups[0] = p.Groups[0] with { SupplyMode = "Cycle" };
        return p;
    }

    [Fact]
    public void Name_Is_The_Model_For_Call_Groups_And_Carries_The_Cycle_Counts_In_Group_Order()
    {
        Assert.Equal("Agv", ModelVariants.NameFor(LogicGeneratorTests.TwoGroups(), "Agv", Sc("G2", 3)));
        Assert.Equal("Agv_c3", ModelVariants.NameFor(Cycle(), "Agv", new RunScenario(1, new Dictionary<string, int> { ["G1"] = 2, ["G2"] = 3 })));
        // a group missing from the scenario counts as its MinCount, as the run id does
        Assert.Equal("Agv_c2x3", ModelVariants.NameFor(Cycle(g1Too: true), "Agv", new RunScenario(1, new Dictionary<string, int> { ["G1"] = 2, ["G2"] = 3 })));
        Assert.Equal("Agv_c1x1", ModelVariants.NameFor(Cycle(g1Too: true), "Agv", new RunScenario(1, new Dictionary<string, int>())));
    }

    [Fact]
    public void Report_Is_The_Base_Models_Else_A_Count_Models_Else_None()
    {
        var dir = Tmp(); Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(ModelVariants.ReportPath(Path.Combine(dir, "gone"), "Agv")); // a purged run folder
            Assert.Null(ModelVariants.ReportPath(dir, "Agv"));
            File.WriteAllText(Path.Combine(dir, "Other_c2.report"), "x"); // another model's
            File.WriteAllText(Path.Combine(dir, "Agv.message"), "x");
            Assert.Null(ModelVariants.ReportPath(dir, "Agv"));
            File.WriteAllText(Path.Combine(dir, "Agv_c2.report"), "x");
            Assert.Equal(Path.Combine(dir, "Agv_c2.report"), ModelVariants.ReportPath(dir, "Agv"));
            File.WriteAllText(Path.Combine(dir, "Agv.report"), "x");
            Assert.Equal(Path.Combine(dir, "Agv.report"), ModelVariants.ReportPath(dir, "Agv")); // the base model's first
        }
        finally { Directory.Delete(dir, true); }
    }

    /// <summary>A project folder with fake model files for each name (never started: the runner is faked).</summary>
    static string Project(params string[] models)
    {
        var dir = Tmp(); Directory.CreateDirectory(dir);
        foreach (var m in models) { File.WriteAllText(Path.Combine(dir, m + ".exe"), "x"); Directory.CreateDirectory(Path.Combine(dir, m + ".arc")); }
        return dir;
    }

    static Func<RunRequest, RunOutcome> Failing(List<RunRequest> seen) => r =>
    {
        lock (seen) seen.Add(r);
        return new RunOutcome(ModelRunner.RunId(r.Input, r.Scenario), false, r.RunsDir!, TimeSpan.FromSeconds(1), "boom");
    };

    [Fact]
    public void Each_Cycle_Count_Runs_Its_Own_Model_In_Its_Own_Slot_Folder_Prepared_Once()
    {
        var dir = Project("Agv", "Agv_c3", "Agv_c4");
        try
        {
            var input = Cycle();
            var seen = new List<RunRequest>();
            var asked = new List<RunScenario>();
            var mx = new ModelExecutor(new ExecutorOptions(dir, "Agv", 2), input, Failing(seen),
                s => { lock (asked) asked.Add(s); return ModelVariants.NameFor(input, "Agv", s); });
            mx.Run(1, Sc("G2", 3)); mx.Run(1, Sc("G2", 4));
            Assert.Equal(new[] { "Agv_c3", "Agv_c4" }, seen.Select(r => r.ModelName));
            Assert.Equal(2, asked.Count);
            // one folder per (slot, model): the two models never share a slot folder
            Assert.NotEqual(seen[0].WorkDir, seen[1].WorkDir);
            Assert.True(File.Exists(Path.Combine(seen[0].WorkDir!, "Agv_c3.exe")) && File.Exists(Path.Combine(seen[1].WorkDir!, "Agv_c4.exe")));
            Assert.False(File.Exists(Path.Combine(seen[0].WorkDir!, "Agv_c4.exe")));

            // the same count again in the same slot: the slot is not prepared twice
            File.Delete(Path.Combine(seen[0].WorkDir!, "Agv_c3.exe"));
            mx.Run(1, Sc("G2", 3, seed: 2));
            Assert.Equal(seen[0].WorkDir, seen[2].WorkDir);
            Assert.False(File.Exists(Path.Combine(seen[0].WorkDir!, "Agv_c3.exe")));
            // another slot gets its own copy
            mx.Run(2, Sc("G2", 3, seed: 3));
            Assert.NotEqual(seen[0].WorkDir, seen[3].WorkDir);
            Assert.True(File.Exists(Path.Combine(seen[3].WorkDir!, "Agv_c3.exe")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Call_Only_Runs_The_Base_Model_In_The_Same_Slot_Folder_As_Before()
    {
        var dir = Project("Agv");
        try
        {
            var seen = new List<RunRequest>();
            var mx = new ModelExecutor(new ExecutorOptions(dir, "Agv", 2), LogicGeneratorTests.TwoGroups(), Failing(seen)); // the default: NameFor + Ensure
            mx.Run(1, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 2, ["G2"] = 1 }));
            var r = Assert.Single(seen);
            Assert.Equal("Agv", r.ModelName);
            Assert.Equal(Path.Combine(dir, "work", "w1"), r.WorkDir);
            Assert.True(File.Exists(Path.Combine(dir, "work", "w1", "Agv.exe")));
            Assert.Equal(new[] { "w1" }, Directory.GetDirectories(Path.Combine(dir, "work")).Select(Path.GetFileName)); // no other folder
            Assert.Equal(new[] { "Agv.arc" }, Directory.GetDirectories(dir).Select(Path.GetFileName).Where(n => n != "work" && n != "runs"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_Variant_That_Cannot_Be_Made_Fails_That_Run_Only()
    {
        var dir = Project("Agv");
        try
        {
            var seen = new List<RunRequest>();
            var mx = new ModelExecutor(new ExecutorOptions(dir, "Agv", 1), Cycle(), Failing(seen)); // no source files, no AutoMod
            var kpis = mx.Run(1, Sc("G2", 3));
            Assert.Empty(seen);
            Assert.Equal(1, mx.Failures);
            Assert.Contains("순환 그룹 대수별 모델", Assert.Single(mx.FailureMessages));
            Assert.All(kpis, k => Assert.True(double.IsNaN(k.Utilization)));
            Assert.False(Assert.Single(SummaryTsv.ReadAll(Path.Combine(dir, "runs", "summary.tsv")), x => x.Group == "G2").Ok);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ---- Ensure: the per-count model made from the source files ----

    /// <summary>The one-way 10 m loop: CP_LOAD up the east side at (10, 8) so three 1.2 m vehicles fit on the lane into it, the block in the far corner.</summary>
    internal static ASH.Core.Layout.Layout Loop()
    {
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("CP_STBY", 5, 0, "standby", "G_EX")); l.Cps.Add(L.Cp("CP_LOAD", 10, 8, "load")); l.Cps.Add(L.Cp("CP_UNLOAD", 2, 10, "unload"));
        l.Blocks.Add(L.Block("BLK1", -1, -1, 2, 2));
        return l;
    }

    static void Set(XLWorkbook wb, string sheet, string col, XLCellValue v)
    {
        var ws = wb.Worksheet(sheet);
        ws.Cell(3, ws.Row(1).CellsUsed().First(c => c.GetString() == col).Address.ColumnNumber).Value = v;
    }

    static void Setting(XLWorkbook wb, string key, XLCellValue v) => wb.Worksheet("Settings").Column(1).CellsUsed().First(c => c.GetString() == key).CellRight().Value = v;

    /// <summary>A source folder (input.cells.tsv, ash.settings.tsv, layout.tsv) as AGVMODEL leaves it: the template's G_EX made a Cycle group of 1..3
    /// T_EX vehicles serving M001 from CP_LOAD to CP_UNLOAD (never held at the station: its unload threshold is never reached), a 1 h run.</summary>
    internal static string CycleSource(ASH.Core.Layout.Layout l, string? asset = null)
    {
        var dir = Tmp(); Directory.CreateDirectory(dir);
        var xlsx = Path.Combine(dir, "input.xlsx");
        ExcelTemplate.Write(xlsx);
        using (var wb = ExcelReader.Open(xlsx))
        {
            Set(wb, "AGV_Groups", "SupplyMode", "Cycle"); Set(wb, "AGV_Groups", "StandbyCPs", Blank.Value);
            Set(wb, "Materials", "UnloadThreshold", 1000);
            if (asset != null) Set(wb, "AGV_Types", "Asset", asset);
            Setting(wb, "SimLength", 1); Setting(wb, "Warmup", 0.1);
            CellDump.Write(wb, Path.Combine(dir, CellDump.FileName));
        }
        File.Delete(xlsx);
        SettingsFile.Save(new ProjectSettings(Scale: 1), Path.Combine(dir, JobPackage.SettingsInPackage));
        LayoutSnapshot.Write(l, Path.Combine(dir, LayoutSnapshot.FileName));
        return dir;
    }

    static ProjectInput InputOf(string dir) { using var wb = CellDump.Load(Path.Combine(dir, CellDump.FileName)); return ExcelReader.Read(wb, out _); }

    static string Cel() => CelWriter.Cel("c", new[] { new Box("body", 0, 0, 0, 0.8, 1.2, 0.5) });

    /// <summary>A stand-in for AutoMod's build: records the model and leaves what a build leaves (the exe and a fresh dll).</summary>
    static Func<string, string, AmBuildResult> FakeBuild(List<string> built, bool ok = true) => (dir, model) =>
    {
        lock (built) built.Add(model);
        if (!ok) return new AmBuildResult(false, "make: error 1", "", Array.Empty<string>(), "종료 코드 2 · dll 없음");
        File.WriteAllText(Path.Combine(dir, model + ".exe"), "x");
        Directory.CreateDirectory(Path.Combine(dir, model + ".dir"));
        File.WriteAllText(Path.Combine(dir, model + ".dir", model + "64.dll"), "x");
        return new AmBuildResult(true, "", "", Array.Empty<string>());
    };

    [Fact]
    public void Ensure_Makes_The_Count_Into_The_Model_With_The_Base_Pictures_And_Builds_It_Once()
    {
        var dir = CycleSource(Loop(), asset: "agv_a");
        try
        {
            var w = new List<string>();
            var b = SourceBuild.Run(dir, "Agv", (_, _) => Cel(), w); // the base model, its picture kept in Agv.arc\assets
            Assert.DoesNotContain(b.Findings, f => f.Code == FindingCode.InputError);
            File.WriteAllText(Path.Combine(dir, "Agv.arc", "user_ext.m"), File.ReadAllText(Path.Combine(dir, "Agv.arc", "user_ext.m")) + "\n// project hook\n");
            var input = InputOf(dir);
            Assert.Equal((1, 3), (input.Groups[0].MinCount, input.Groups[0].MaxCount));
            var built = new List<string>();

            ModelVariants.Ensure(dir, "Agv", "Agv_c2", input, Sc("G_EX", 2), null, TimeSpan.Zero, FakeBuild(built));
            Assert.Equal(new[] { "Agv_c2" }, built);
            var asy = File.ReadAllText(Path.Combine(dir, "Agv_c2.arc", "agv.asy"));
            Assert.Contains("numveh 2", asy);
            Assert.Contains("SP_G_EX_2", asy); Assert.DoesNotContain("SP_G_EX_3", asy); // one start CP per vehicle of this count
            Assert.Contains("numveh 3", File.ReadAllText(Path.Combine(dir, "Agv.arc", "agv.asy"))); // the base model is not touched
            Assert.Equal(File.ReadAllText(Path.Combine(dir, "Agv.arc", "assets", "agv_a.cel")), File.ReadAllText(Path.Combine(dir, "Agv_c2.arc", "assets", "agv_a.cel")));
            Assert.Contains("// project hook", File.ReadAllText(Path.Combine(dir, "Agv_c2.arc", "user_ext.m")));

            // built and newer than the source: taken as it is, also by another thread waiting for it
            Parallel.For(0, 4, _ => ModelVariants.Ensure(dir, "Agv", "Agv_c2", input, Sc("G_EX", 2), null, TimeSpan.Zero, FakeBuild(built)));
            Assert.Single(built);
            // a source written again (AGVMODEL after a drawing change): made again
            File.SetLastWriteTimeUtc(Path.Combine(dir, LayoutSnapshot.FileName), DateTime.UtcNow.AddMinutes(1));
            ModelVariants.Ensure(dir, "Agv", "Agv_c2", input, Sc("G_EX", 2), null, TimeSpan.Zero, FakeBuild(built));
            Assert.Equal(2, built.Count);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Ensure_Says_What_Is_Missing_And_Why_A_Build_Failed()
    {
        var dir = CycleSource(Loop());
        try
        {
            var input = InputOf(dir);
            var built = new List<string>();
            var e = Assert.Throws<InvalidOperationException>(() => ModelVariants.Ensure(dir, "Agv", "Agv_c2", input, Sc("G_EX", 2), null, TimeSpan.Zero));
            Assert.Contains("AutoMod", e.Message); // no AutoMod given: never the base model instead

            e = Assert.Throws<InvalidOperationException>(() => ModelVariants.Ensure(dir, "Agv", "Agv_c2", input, Sc("G_EX", 2), null, TimeSpan.Zero, FakeBuild(built, ok: false)));
            Assert.Contains("종료 코드 2 · dll 없음", e.Message);
            Assert.Contains("make: error 1", e.Message);

            File.Delete(Path.Combine(dir, LayoutSnapshot.FileName));
            File.Delete(Path.Combine(dir, JobPackage.SettingsInPackage));
            e = Assert.Throws<InvalidOperationException>(() => ModelVariants.Ensure(dir, "Agv", "Agv_c3", input, Sc("G_EX", 3), null, TimeSpan.Zero, FakeBuild(built)));
            Assert.Equal("순환 그룹 대수별 모델을 만들 원본이 없습니다: layout.tsv, ash.settings.tsv", e.Message);
            Assert.Single(built);
        }
        finally { Directory.Delete(dir, true); }
    }
}
