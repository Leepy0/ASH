using ASH.Core.Asy;
using ASH.Core.AutoMod;
using ASH.Core.Export;
using ASH.Core.Layout;
using ASH.Tests.Layout;

namespace ASH.Tests.AutoMod;

public class AmBuildIntegrationTests
{
    static string? AmHome => Environment.GetEnvironmentVariable("ASH_AM_HOME"); // e.g. C:\AutoMod\12_6

    [SkippableFact]
    public void Rewritten_Demo_Builds()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var work = Path.Combine(Path.GetTempPath(), "ash_build_" + Guid.NewGuid().ToString("N"));
        CopyDir(Path.Combine(AmHome!, @"demos\amoddemos\demos\agv\agvdemo.arc"), Path.Combine(work, "agvdemo.arc"));
        var asy = Path.Combine(work, "agvdemo.arc", "agv.asy");
        AsyWriter.WriteFile(AsyReader.ReadFile(asy), asy);
        var r = AmBuilder.Build(AmHome!, work, "agvdemo", TimeSpan.FromMinutes(5));
        Console.WriteLine("ClosedDialogTitles: " + string.Join(" | ", r.ClosedDialogTitles));
        Assert.True(r.Success, r.MakeErrText + r.Am2ErrText);
        Assert.True(File.Exists(Path.Combine(work, "agvdemo.dir", "agvdemo64.dll")));
        Assert.Contains(r.ClosedDialogTitles, t => t.Contains("Entity Limit"));
    }

    [SkippableFact]
    public void Exported_Cross_Layout_Builds()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var l = L.Of(L.Line("a", -10, 0, 0, 0), L.Line("b", 0, 0, 10, 0), L.Line("c", 0, -10, 0, 0), L.Line("d", 0, 0, 0, 10));
        for (int i = 1; i <= 5; i++) l.Cps.Add(L.Cp("p_" + i, i * 1.5, 0, "load"));
        for (int i = 1; i <= 6; i++) l.Cps.Add(L.Cp("d_" + i, 0, i * 1.5, "load"));
        l.Cps.Add(L.Cp("workcheck", -5, 0, "standby", "work"));
        var sys = LayoutExporter.ToAgvs(l, new ExportOptions(Units: "Feet", Groups: new[] { new VehicleGroupSpec("work", "DefVehicle", 10, 1.0, 2, 1) }));
        sys.NameLists["nlwork"] = sys.NameLists["nl_work"]; sys.NameLists.Remove("nl_work");
        for (int i = 0; i < sys.VehicleLines.Count; i++) sys.VehicleLines[i] = sys.VehicleLines[i].Replace("start nl_work", "start nlwork");

        var work = Path.Combine(Path.GetTempPath(), "ash_exp_build_" + Guid.NewGuid().ToString("N"));
        CopyDir(Path.Combine(AmHome!, @"demos\amoddemos\demos\agv\agvdemo.arc"), Path.Combine(work, "agvdemo.arc"));
        AsyWriter.WriteFile(sys, Path.Combine(work, "agvdemo.arc", "agv.asy"));
        var r = AmBuilder.Build(AmHome!, work, "agvdemo", TimeSpan.FromMinutes(5));
        Assert.True(r.Success, r.MakeErrText + r.Am2ErrText);
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
