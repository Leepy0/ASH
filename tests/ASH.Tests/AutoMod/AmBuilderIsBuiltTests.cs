using ASH.Core.AutoMod;

namespace ASH.Tests.AutoMod;

public class AmBuilderIsBuiltTests
{
    [Fact]
    public void Built_Only_With_Exe_And_A_Dll_Newer_Than_The_Arc_Files_Except_Params()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_built_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "m.arc"));
        Directory.CreateDirectory(Path.Combine(dir, "m.dir"));
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        void At(string rel, int minutes) { var p = Path.Combine(dir, rel); File.WriteAllText(p, ""); File.SetLastWriteTimeUtc(p, t0.AddMinutes(minutes)); }
        At(@"m.arc\agv.asy", 0);
        Assert.False(AmBuilder.IsBuilt(dir, "m")); // nothing built
        At("m.exe", -100000); // runModel64.exe copy keeps the install's date
        At(@"m.dir\m64.dll", 5);
        Assert.True(AmBuilder.IsBuilt(dir, "m"));
        At(@"m.arc\params.txt", 10); // ash-run rewrites it before each run: no rebuild
        Assert.True(AmBuilder.IsBuilt(dir, "m"));
        At(@"m.arc\model.amo", 10); // AGVMODEL wrote the model again
        Assert.False(AmBuilder.IsBuilt(dir, "m"));
        File.Delete(Path.Combine(dir, "m.exe"));
        At(@"m.dir\m64.dll", 20);
        Assert.False(AmBuilder.IsBuilt(dir, "m"));
        Directory.Delete(dir, true);
    }
}
