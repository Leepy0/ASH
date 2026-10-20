using ASH.Core.AutoMod;
using ASH.Core.Geometry;
using ASH.Core.Model;

namespace ASH.Tests.Model;

public class ProcessAsyBuildIntegrationTests
{
    static string? AmHome => Environment.GetEnvironmentVariable("ASH_AM_HOME");

    static ProcessModel DemoLike(string demoAsy)
    {
        var lines = File.ReadAllLines(demoAsy).ToList();
        int b = lines.FindIndex(l => l.StartsWith("SFileBegin")), e = lines.FindIndex(l => l.StartsWith("#@!"));
        // the demo's Roper resource is not generated yet, so its `use Roper` statements are dropped
        var code = string.Join("\n", lines.Skip(b + 1).Take(e - b - 1).Where(l => !l.Contains("Roper")));
        var m = new ProcessModel { LogicSource = code };
        foreach (var p in new[] { "Pinit", "Phour", "Proute" }) m.Procs.Add(new(p));
        m.LoadTypes.AddRange(new[] { "Linit", "Lpallet" });
        m.Queues.Add(new("Qtemp")); m.Queues.Add(new("Qpallet", 4, 8));
        m.Labels.AddRange(new[] { "Lhour", "Ldeliver", "Ltotal", "Lnumveh" });
        m.Orders.Add("OLforever"); m.Tables.Add("Tthroughput");
        foreach (var a in new[] { "Alane", "Aout", "Aindex" }) m.Atts.Add(new(a, "Integer"));
        m.Vars.Add(new("Vrepfile", "FilePtr")); m.Vars.Add(new("Vnumvehicles", "Integer", 0, "5"));
        foreach (var v in new[] { "Vnumonconv", "Vhour", "Vtotaldone", "Vcountveh", "Vdontset" }) m.Vars.Add(new(v, "Integer"));
        m.Vars.Add(new("Vdone", "Integer", 1000)); m.Vars.Add(new("Vaveinterval", "Real"));
        m.RnStreams.Add(new("stream0", 12345));
        m.Funcs.AddRange(new[] { "Fincveh", "Fdecveh" });
        return m;
    }

    static string Prepare(out string work)
    {
        work = Path.Combine(Path.GetTempPath(), "ash_proc_" + Guid.NewGuid().ToString("N"));
        CopyDir(Path.Combine(AmHome!, @"demos\amoddemos\demos\agv\agvdemo.arc"), Path.Combine(work, "agvdemo.arc"));
        return Path.Combine(work, "agvdemo.arc", "agvdemo~.asy");
    }

    [SkippableFact]
    public void Process_Asy_Mirroring_Demo_Builds()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var asy = Prepare(out var work);
        ProcessAsyWriter.WriteFile(DemoLike(asy), asy);
        var r = AmBuilder.Build(AmHome!, work, "agvdemo", TimeSpan.FromMinutes(5));
        Assert.True(r.Success, work + " " + r.MakeErrText + r.Am2ErrText);
    }

    [SkippableFact]
    public void Process_Asy_With_Block_Builds()
    {
        Skip.If(AmHome is null, "ASH_AM_HOME not set");
        var asy = Prepare(out var work);
        var m = DemoLike(asy);
        m.Blocks.Add(new("B001", 1, new Rect2(new Point2(0, 0), new Point2(4, 2))));
        ProcessAsyWriter.WriteFile(m, asy);
        var r = AmBuilder.Build(AmHome!, work, "agvdemo", TimeSpan.FromMinutes(5));
        Assert.True(r.Success, work + " " + r.MakeErrText + r.Am2ErrText);
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }
}
