using System.IO;
using ASH.Core.Film;

namespace ASH.Tests.Film;

public class AutoViewExeTests
{
    // a throwaway AutoMod folder with the given program files in bin\
    static string AmHome(params string[] files)
    {
        var am = Path.Combine(Path.GetTempPath(), "ash_av_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(am, "bin"));
        foreach (var f in files) File.WriteAllText(Path.Combine(am, "bin", f), "x");
        return am;
    }

    [Fact]
    public void Finds_aview_Next_To_The_12_4_Build_Exe()
    {
        // AutoMod 12.4 ships aview.exe (no 64): the same folder layout as its am2.exe
        var am = AmHome("am2.exe", "aview.exe");
        try { Assert.Equal(Path.Combine(am, "bin", "aview.exe"), AutoViewExe.Find(am)); }
        finally { Directory.Delete(am, true); }
    }

    [Fact]
    public void Prefers_aview64_When_Both_Are_There()
    {
        var am = AmHome("aview.exe", "aview64.exe");
        try { Assert.Equal(Path.Combine(am, "bin", "aview64.exe"), AutoViewExe.Find(am)); }
        finally { Directory.Delete(am, true); }
    }

    [Fact]
    public void Finds_Nothing_And_Lists_Both_Candidates_When_Neither_Is_There()
    {
        var am = AmHome("am264.exe");
        try
        {
            Assert.Null(AutoViewExe.Find(am));
            var c = AutoViewExe.Candidates(am);
            Assert.Contains("aview64.exe", c);
            Assert.Contains("aview.exe", c);
        }
        finally { Directory.Delete(am, true); }
    }
}
