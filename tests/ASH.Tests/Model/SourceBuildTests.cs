using ASH.Core.Cel;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Core.Settings;
using ASH.Tests.Layout;

namespace ASH.Tests.Model;

[Collection("ExcelTemplateStatics")]
public class SourceBuildTests
{
    static string Tmp() => Path.Combine(Path.GetTempPath(), "ash_sb_" + Guid.NewGuid().ToString("N"));

    // the template's example rows: group G_EX standby CP_STBY, M001 from CP_LOAD to CP_UNLOAD
    static ASH.Core.Layout.Layout Loop()
    {
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("CP_STBY", 5, 0, "standby", "G_EX")); l.Cps.Add(L.Cp("CP_LOAD", 10, 5, "load")); l.Cps.Add(L.Cp("CP_UNLOAD", 2, 10, "unload"));
        l.Blocks.Add(L.Block("BLK1", 8, -1, 11, 2));
        l.Assets.Add(new LayoutAsset { Kind = AssetKinds.Rack, X = 4, Y = 4 });
        return l;
    }

    static string Cel() => CelWriter.Cel("c", new[] { new Box("body", 0, 0, 0, 0.8, 1.2, 0.5) });

    /// <summary>A source-form project folder: the template's cells (T_EX with asset agv_a), the layout and the settings.</summary>
    static string Source(ASH.Core.Layout.Layout l)
    {
        var dir = Tmp(); Directory.CreateDirectory(dir);
        var xlsx = Path.Combine(dir, "input.xlsx");
        ExcelTemplate.Write(xlsx);
        using (var wb = ExcelReader.Open(xlsx))
        {
            var ws = wb.Worksheet("AGV_Types");
            ws.Cell(3, ws.Row(1).CellsUsed().First(c => c.GetString() == "Asset").Address.ColumnNumber).Value = "agv_a";
            CellDump.Write(wb, Path.Combine(dir, CellDump.FileName));
        }
        File.Delete(xlsx); // the server never gets the workbook
        SettingsFile.Save(new ProjectSettings(Scale: 1, Origin: new Point2(1, 2), Groups: "X:Y:1:1:1:1"), Path.Combine(dir, JobPackage.SettingsInPackage));
        LayoutSnapshot.Write(l, Path.Combine(dir, LayoutSnapshot.FileName));
        return dir;
    }

    /// <summary>What the server makes from a source package builds in AutoMod (only with ASH_AM_HOME set, like the other AutoMod tests).</summary>
    [Fact]
    public void Server_Made_Model_Builds_In_AutoMod()
    {
        var am = Environment.GetEnvironmentVariable("ASH_AM_HOME");
        if (string.IsNullOrEmpty(am) || ASH.Core.AutoMod.AmBuilder.FindBuildExe(am) == null) return;
        var src = Source(Loop());
        try
        {
            var r = SourceBuild.Run(src, "m", (_, _) => Cel(), new List<string>());
            Assert.DoesNotContain(r.Findings, f => f.Code == FindingCode.InputError);
            ModelBuilder.PrepareBuildDir(src, "m");
            var b = ASH.Core.AutoMod.AmBuilder.Build(am, src, "m", TimeSpan.FromMinutes(10));
            Assert.True(b.Success, $"exit/dll 실패 · make.err [{b.MakeErrText}] · am2err [{b.Am2ErrText}] · 닫은 창 [{string.Join(",", b.ClosedDialogTitles)}]");
        }
        finally { Directory.Delete(src, true); }
    }

    [Fact]
    public void Makes_The_Same_Arc_Files_As_AgvModel()
    {
        var l = Loop();
        var src = Source(l); var refDir = Tmp();
        try
        {
            Assert.True(JobPackage.IsSource(src, "m"));
            var warnings = new List<string>();
            var asked = new List<(string, string)>();
            var r = SourceBuild.Run(src, "m", (ty, n) => { asked.Add((ty, n)); return Cel(); }, warnings);
            Assert.DoesNotContain(r.Findings, f => f.Code == FindingCode.InputError);
            Assert.Equal(new[] { ("T_EX", "agv_a") }, asked);
            Assert.True(File.Exists(Path.Combine(src, "m.arc", "assets", "agv_a.cel")));
            Assert.False(JobPackage.IsSource(src, "m"));

            // AGVMODEL's path written out: Excel groups win, settings give units/origin/scale, the asset picture by group
            using var wb = CellDump.Load(Path.Combine(src, CellDump.FileName));
            var input = ExcelReader.Read(wb, out _);
            var o = new ExportOptions(Units: "Meters", Origin: new Point2(1, 2), Scale: 1, Groups: ModelBuilder.VehicleGroups(input));
            var ov = AssetOverrides.ForGroups(AssetOverrides.Build(input.AgvTypes, AssetOverrides.VehSegTemplate(), (_, _) => Cel(), new List<string>()), input.Groups);
            Assert.NotEmpty(ov);
            var want = ModelBuilder.Build(new ModelBuildRequest(refDir, "m", input, l, o, VehicleOverrides: ov), null, TimeSpan.Zero);
            Assert.Equal(want.Warnings, r.Warnings);
            foreach (var f in new[] { "agv.asy", "model.amo", "m~.asy", "params.txt", "ash_3d.asy", "traffic.tsv" })
                Assert.Equal(File.ReadAllText(Path.Combine(want.ArcDir, f)), File.ReadAllText(Path.Combine(r.ArcDir, f)));
        }
        finally { Directory.Delete(src, true); if (Directory.Exists(refDir)) Directory.Delete(refDir, true); }
    }

    [Fact]
    public void Missing_Asset_And_No_Store_Are_Warnings()
    {
        var src = Source(Loop());
        try
        {
            var w = new List<string>();
            var r = SourceBuild.Run(src, "m", (_, _) => null, w);
            Assert.DoesNotContain(r.Findings, f => f.Code == FindingCode.InputError);
            Assert.Contains(w, x => x.Contains("agv_a") && x.Contains("카탈로그에 없는 이름입니다") && x.Contains("기본 그림"));
            Assert.False(Directory.Exists(Path.Combine(src, "m.arc", "assets")));
            var w2 = new List<string>();
            SourceBuild.Run(src, "m", (_, _) => "junk", w2);
            Assert.Contains(w2, x => x.Contains("에셋 파일"));
            var w3 = new List<string>();
            SourceBuild.Run(src, "m", null, w3);
            Assert.Contains(w3, x => x.Contains("에셋 저장소가 없어"));
        }
        finally { Directory.Delete(src, true); }
    }

    [Fact]
    public void Input_Error_Writes_No_Model()
    {
        var l = Loop(); l.Cps.RemoveAll(c => c.Name == "CP_UNLOAD");
        var src = Source(l);
        try
        {
            var r = SourceBuild.Run(src, "m", null, new List<string>());
            Assert.Contains(r.Findings, f => f.Code == FindingCode.InputError);
            Assert.False(File.Exists(Path.Combine(r.ArcDir, "agv.asy")));
        }
        finally { Directory.Delete(src, true); }
    }
}
