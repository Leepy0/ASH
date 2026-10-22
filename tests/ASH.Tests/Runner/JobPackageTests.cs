using System.IO.Compression;
using ASH.Core.Runner;

namespace ASH.Tests.Runner;

public class JobPackageTests
{
    [Fact]
    public void Create_Leaves_Out_The_Asy_Backups()
    {
        var proj = Path.Combine(Path.GetTempPath(), "ash_pkg_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(proj, "m.arc"));
            foreach (var p in JobPackage.Required("m").Concat(new[] { "m.arc/agv.asy.bak.20261022_120000_000", "m.arc/agv.asy.bak.20261022_120000_000_1" }))
                File.WriteAllText(Path.Combine(proj, p.Replace('/', Path.DirectorySeparatorChar)), "x");
            var zip = Path.Combine(proj, "pkg.zip");
            var files = JobPackage.Create(proj, "m", zip);
            using var z = ZipFile.OpenRead(zip);
            Assert.Contains(z.Entries, e => e.FullName == "m.arc/agv.asy");
            Assert.DoesNotContain(z.Entries, e => e.FullName.Contains(".bak"));
            Assert.DoesNotContain(files, f => f.Path.Contains(".bak"));
        }
        finally { Directory.Delete(proj, true); }
    }
}
