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
        var gen = ModelBuilder.Build(new ModelBuildRequest(work, Model, input, l, new ExportOptions(Groups: ModelBuilder.VehicleGroups(input))), null, TimeSpan.FromMinutes(5)); // generate only
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
        if (Environment.GetEnvironmentVariable("ASH_KEEP_TEMP") != "1") Directory.Delete(work, true);
    }
}
