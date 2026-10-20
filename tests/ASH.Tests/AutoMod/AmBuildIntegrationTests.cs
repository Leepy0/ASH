using ASH.Core.Asy;
using ASH.Core.AutoMod;

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

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
