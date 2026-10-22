using ASH.Core.AutoMod;
using ASH.Core.Dxf;
using ASH.Core.Export;
using ASH.Core.Film;
using ASH.Core.Geometry;
using ASH.Core.Model;
using ASH.Tests.Layout;
using ASH.Tests.Model;

namespace ASH.Tests.AutoMod;

public class FilmIntegrationTests
{
    static string? AmHome => Environment.GetEnvironmentVariable("ASH_AM_HOME");
    const string Model = "ashfilm";

    [SkippableFact]
    public void Drawing_Background_Builds()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("S001", 5, 0, "standby", "G1")); l.Cps.Add(L.Cp("L001", 10, 5, "load")); l.Cps.Add(L.Cp("B001", 6, 10, "buffer")); l.Cps.Add(L.Cp("U001", 2, 10, "unload"));
        var work = Path.Combine(Path.GetTempPath(), "ash_film_" + Guid.NewGuid().ToString("N"));
        var gen = ModelBuilder.Build(new ModelBuildRequest(work, Model, input, l, new ExportOptions(Groups: ModelBuilder.VehicleGroups(input)), AutoView: true), null, TimeSpan.FromMinutes(5)); // generate only
        Assert.Null(gen.Build);

        // a wall box around the loop (one chained run) + a hidden dimension layer + a free line
        var segs = new List<DxfSeg>
        {
            new(-2, -2, 12, -2, "WALL"), new(12, -2, 12, 12, "WALL"), new(12, 12, -2, 12, "WALL"), new(-2, 12, -2, -2, "WALL"),
            new(0, 5, 10, 5, "DIM"), new(3, 3, 7, 7, "WALL"),
        };
        StaticDrawing.AddTo(gen.ArcDir, new DxfDrawing(segs, new DxfText[0], new[] { "WALL", "DIM" }), "Meters", new[] { "DIM" });
        Assert.Contains("STATIC name ash_drw", File.ReadAllText(Path.Combine(gen.ArcDir, "model.amo")));

        ModelBuilder.PrepareBuildDir(work, Model);
        var b = AmBuilder.Build(AmHome!, work, Model, TimeSpan.FromMinutes(5));
        Console.WriteLine($"{work} dialogs [{string.Join("|", b.ClosedDialogTitles)}]");
        Assert.True(b.Success, work + "\n" + b.MakeErrText + b.Am2ErrText);
        Assert.True(File.Exists(Path.Combine(work, Model + ".dir", Model + "64.dll")));
        var o = ASH.Core.Runner.ModelRunner.Run(new ASH.Core.Runner.RunRequest(work, Model, input, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 1 }), TimeSpan.FromMinutes(2), "Student Version"));
        Assert.True(o.Success, o.Error); // the model with the drawing still runs (and records the .avw when AutoView is on)
        if (Environment.GetEnvironmentVariable("ASH_KEEP_TEMP") != "1") Directory.Delete(work, true);
    }

    static string? Ffprobe(string file, string entries)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("ffprobe", $"-v error -select_streams v:0 -show_entries {entries} -of default=nw=1:nk=1 \"{file}\"") { RedirectStandardOutput = true, UseShellExecute = false };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
        return o.Trim();
    }

    // a recorded window only (CONTROL split), one Hold cut through AutoView, then the phone/PPT re-encode with a caption: ASH_AM_HOME and ffmpeg/ffprobe on PATH
    [SkippableFact]
    public void One_Hold_Cut_Makes_A_Playable_Mp4()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var input = LogicGeneratorTests.Sample();
        var l = L.Of(L.Line("p1", 0, 0, 10, 0), L.Line("p2", 10, 0, 10, 10), L.Line("p3", 10, 10, 0, 10), L.Line("p4", 0, 10, 0, 0));
        l.Cps.Add(L.Cp("S001", 5, 0, "standby", "G1")); l.Cps.Add(L.Cp("L001", 10, 5, "load")); l.Cps.Add(L.Cp("B001", 6, 10, "buffer")); l.Cps.Add(L.Cp("U001", 2, 10, "unload"));
        var work = Path.Combine(Path.GetTempPath(), "ash_film_" + Guid.NewGuid().ToString("N"));
        var gen = ModelBuilder.Build(new ModelBuildRequest(work, Model, input, l, new ExportOptions(Groups: ModelBuilder.VehicleGroups(input)), AutoView: true), null, TimeSpan.FromMinutes(5));
        var amo = Path.Combine(gen.ArcDir, "model.amo");
        File.WriteAllText(amo, ModelAmoWriter.WithRecording(File.ReadAllText(amo), input.Settings.SimLengthSec, 1190, 1510));
        ModelBuilder.PrepareBuildDir(work, Model);
        Assert.True(AmBuilder.Build(AmHome!, work, Model, TimeSpan.FromMinutes(5)).Success, work);
        var o = ASH.Core.Runner.ModelRunner.Run(new ASH.Core.Runner.RunRequest(work, Model, input, new RunScenario(1, new Dictionary<string, int> { ["G1"] = 2 }), TimeSpan.FromMinutes(2), "Student Version", AmHome: AmHome));
        Assert.True(o.Success, o.Error); // the split CONTROL lines still end with a report and result.tsv

        var cut = new CutSpec("c1", "고정", CutKind.Hold, 1200, 300, new Rect(-1, -1, 12, 12), null, 0, 0, 0, 30, 0, 0, false, true); // 300 s / 30x = 10 s
        var r = new AutoViewRenderer(Path.Combine(AmHome!, "bin", "aview64.exe"), AmHome, "ffmpeg");
        var res = r.Render(work, Model, CameraScript.Build(cut, 18, 1280, 720, 15, 80, Path.Combine(work, "cut01")), TimeSpan.FromMinutes(5), CancellationToken.None);
        Console.WriteLine($"{work} dialogs [{string.Join("|", res.Dialogs)}]");
        Assert.True(res.Ok, res.Error);
        var final = r.Finish(new[] { res.Mp4! }, new[] { "분석 · A안 v1 · 고정\n00:20:00 · 권장 2대 · 가동률 68% · 결품 0초" }, Path.Combine(work, "final.mp4"), false);
        Assert.Equal("yuv420p", Ffprobe(final, "stream=pix_fmt"));
        var sec = double.Parse(Ffprobe(final, "format=duration")!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(sec, 9, 11);
        var joined = r.Finish(new[] { final, final }, Array.Empty<string>(), Path.Combine(work, "joined.mp4"), true);
        Assert.InRange(double.Parse(Ffprobe(joined, "format=duration")!, System.Globalization.CultureInfo.InvariantCulture), 18, 22);
        if (Environment.GetEnvironmentVariable("ASH_KEEP_TEMP") != "1") Directory.Delete(work, true);
    }
}
