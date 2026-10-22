using System.IO.Compression;
using ASH.Core.Dxf;
using ASH.Core.Runner;

namespace ASH.Tests.Runner;

public class JobPackageTests
{
    [Fact]
    public void Create_Leaves_Out_The_Asy_Backups_And_The_Local_Settings()
    {
        var proj = Path.Combine(Path.GetTempPath(), "ash_pkg_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(proj, "m.arc"));
            foreach (var p in JobPackage.Required("m").Concat(new[] { "m.arc/agv.asy.bak.20261022_120000_000", "m.arc/agv.asy.bak.20261022_120000_000_1", "ash.settings.tsv" }))
                File.WriteAllText(Path.Combine(proj, p.Replace('/', Path.DirectorySeparatorChar)), "x");
            var zip = Path.Combine(proj, "pkg.zip");
            var files = JobPackage.Create(proj, "m", zip);
            using var z = ZipFile.OpenRead(zip);
            Assert.Contains(z.Entries, e => e.FullName == "m.arc/agv.asy");
            Assert.DoesNotContain(z.Entries, e => e.FullName.Contains(".bak"));
            Assert.DoesNotContain(files, f => f.Path.Contains(".bak"));
            Assert.DoesNotContain(z.Entries, e => e.FullName == "ash.settings.tsv"); // its AmHome is this PC's: the server uses its own
        }
        finally { Directory.Delete(proj, true); }
    }

    static string Project(bool dxf, bool frame)
    {
        var proj = Path.Combine(Path.GetTempPath(), "ash_pkg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(proj, "m.arc"));
        foreach (var p in JobPackage.Required("m")) File.WriteAllText(Path.Combine(proj, p.Replace('/', Path.DirectorySeparatorChar)), "x");
        if (dxf) File.WriteAllText(Path.Combine(proj, "drawing.dxf"), "0\nEOF\n");
        if (frame) new ModelFrame("Meters", 1, 2, 0.001).Write(Path.Combine(proj, ModelFrame.FileName));
        return proj;
    }

    [Fact]
    public void Package_Includes_Dxf_And_Frame_When_Present()
    {
        var proj = Project(true, true);
        try
        {
            var zip = Path.Combine(proj, "pkg.zip");
            var files = JobPackage.Create(proj, "m", zip);
            Assert.Contains(files, f => f.Path == "drawing.dxf");
            Assert.Contains(files, f => f.Path == ModelFrame.FileName);
            var dest = Path.Combine(proj, "out");
            Assert.Contains(JobPackage.Extract(zip, dest, "m"), f => f.Path == "drawing.dxf");
            Assert.Equal(new ModelFrame("Meters", 1, 2, 0.001), ModelFrame.Read(Path.Combine(dest, ModelFrame.FileName)));
        }
        finally { Directory.Delete(proj, true); }
    }

    [Fact]
    public void Package_Without_Dxf_Still_Valid()
    {
        var proj = Project(false, false);
        try
        {
            var zip = Path.Combine(proj, "pkg.zip");
            var files = JobPackage.Create(proj, "m", zip);
            Assert.DoesNotContain(files, f => JobPackage.OptionalFiles.Contains(f.Path));
            Assert.Equal(files.Count, JobPackage.Extract(zip, Path.Combine(proj, "out"), "m").Count);
        }
        finally { Directory.Delete(proj, true); }
    }

    [Fact]
    public void Package_Leaves_Out_A_Dxf_Over_The_Limit()
    {
        var proj = Project(true, false);
        try
        {
            using (var fs = new FileStream(Path.Combine(proj, "drawing.dxf"), FileMode.Open)) fs.SetLength(JobPackage.MaxDxfBytes + 1);
            Assert.DoesNotContain(JobPackage.Create(proj, "m", Path.Combine(proj, "pkg.zip")), f => f.Path == "drawing.dxf");
        }
        finally { Directory.Delete(proj, true); }
    }
}
