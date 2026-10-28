using System.IO.Compression;
using ASH.Core.Dxf;
using ASH.Core.Runner;

namespace ASH.Tests.Runner;

public class JobPackageTests
{
    [Fact]
    public void Create_Leaves_Out_The_Asy_Backups_And_Keeps_The_Settings()
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
            Assert.Contains(z.Entries, e => e.FullName == "ash.settings.tsv"); // carried for continue.zip; the server does not use it
        }
        finally { Directory.Delete(proj, true); }
    }

    static string Project(bool dxf, bool frame)
    {
        var proj = Path.Combine(Path.GetTempPath(), "ash_pkg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(proj, "m.arc"));
        foreach (var p in JobPackage.Required("m")) File.WriteAllText(Path.Combine(proj, p.Replace('/', Path.DirectorySeparatorChar)), "x");
        if (dxf) File.WriteAllText(Path.Combine(proj, JobPackage.LocalDxf),"0\nEOF\n");
        if (frame) new ModelFrame("Meters", 1, 2, 0.001).Write(Path.Combine(proj, ModelFrame.FileName));
        return proj;
    }

    [Fact]
    public void Package_Includes_Dwg_And_Settings_When_Given()
    {
        var proj = Project(dxf: false, frame: false);
        try
        {
            File.WriteAllText(Path.Combine(proj, "ash.settings.tsv"), "ModelName	m");
            var dwg = Path.Combine(proj, "sub", "플랜트.dwg"); Directory.CreateDirectory(Path.GetDirectoryName(dwg)!); File.WriteAllBytes(dwg, new byte[16]);
            var zip = Path.Combine(proj, "p.zip");
            var files = JobPackage.Create(proj, "m", zip, dwgPath: dwg);
            using var z = ZipFile.OpenRead(zip);
            Assert.Contains(z.Entries, e => e.FullName == "drawing.dwg");
            Assert.Contains(z.Entries, e => e.FullName == "ash.settings.tsv");
            Assert.Contains(files, f => f.Path == "drawing.dwg" && f.Size == 16);
            Assert.Contains("drawing.dwg", JobPackage.OptionalFiles); Assert.Contains("ash.settings.tsv", JobPackage.OptionalFiles);
        }
        finally { Directory.Delete(proj, true); }
    }

    [Fact]
    public void Package_Skips_Missing_Dwg()
    {
        var proj = Project(dxf: false, frame: false);
        try
        {
            var zip = Path.Combine(proj, "p.zip");
            JobPackage.Create(proj, "m", zip, dwgPath: Path.Combine(proj, "nope.dwg"));
            JobPackage.Create(proj, "m", zip, dwgPath: null);
            using var z = ZipFile.OpenRead(zip);
            Assert.DoesNotContain(z.Entries, e => e.FullName == "drawing.dwg");
        }
        finally { Directory.Delete(proj, true); }
    }

    [Fact]
    public void Package_Includes_Dxf_And_Frame_When_Present()
    {
        var proj = Project(true, true);
        try
        {
            var zip = Path.Combine(proj, "pkg.zip");
            var files = JobPackage.Create(proj, "m", zip);
            Assert.Contains(files, f => f.Path == "drawing.dxf"); // AGVSEND's ash.drawing.dxf travels under the name the server reads
            Assert.DoesNotContain(files, f => f.Path == JobPackage.LocalDxf);
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
    public void Users_Own_Drawing_Dxf_Is_Not_Sent()
    {
        var proj = Project(false, false);
        try
        {
            File.WriteAllText(Path.Combine(proj, "drawing.dxf"), "the user's own file");
            Assert.DoesNotContain(JobPackage.Create(proj, "m", Path.Combine(proj, "pkg.zip")), f => f.Path == "drawing.dxf");
            Assert.Equal("the user's own file", File.ReadAllText(Path.Combine(proj, "drawing.dxf")));
        }
        finally { Directory.Delete(proj, true); }
    }

    static string SourceProject()
    {
        var proj = Path.Combine(Path.GetTempPath(), "ash_pkg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(proj);
        foreach (var p in JobPackage.RequiredSource) File.WriteAllText(Path.Combine(proj, p), "x");
        return proj;
    }

    [Fact]
    public void CreateSource_Sends_Layout_Cells_Settings_And_The_Drawing()
    {
        var proj = SourceProject();
        try
        {
            Assert.Equal(new[] { "layout.tsv", "input.cells.tsv", "ash.settings.tsv" }, JobPackage.RequiredSource);
            Assert.True(JobPackage.IsSource(proj, "m"));
            File.WriteAllText(Path.Combine(proj, JobPackage.LocalDxf), "0\nEOF\n");
            new ModelFrame("Meters", 1, 2, 0.001).Write(Path.Combine(proj, ModelFrame.FileName));
            var dwg = Path.Combine(proj, "a.dwg"); File.WriteAllBytes(dwg, new byte[4]);
            var zip = Path.Combine(proj, "p.zip");
            var files = JobPackage.CreateSource(proj, zip, dwg);
            Assert.Equal(new[] { "layout.tsv", "input.cells.tsv", "ash.settings.tsv", "drawing.dxf", "drawing.dwg", ModelFrame.FileName }, files.Select(f => f.Path));
            using (var z = ZipFile.OpenRead(zip)) Assert.Equal(files.Select(f => f.Path), z.Entries.Select(e => e.FullName));
            Assert.Equal(files.Count, JobPackage.Extract(zip, Path.Combine(proj, "out"), "m").Count); // the source form passes the server's check
            Assert.True(JobPackage.IsSource(Path.Combine(proj, "out"), "m"));
            File.Delete(Path.Combine(proj, "layout.tsv"));
            Assert.False(JobPackage.IsSource(proj, "m"));
            Assert.Contains("layout.tsv", Assert.Throws<InvalidOperationException>(() => JobPackage.CreateSource(proj, zip)).Message);
        }
        finally { Directory.Delete(proj, true); }
    }

    [Fact]
    public void Model_Folder_Is_Not_Source()
    {
        var proj = Project(false, false);
        try
        {
            foreach (var p in JobPackage.RequiredSource) File.WriteAllText(Path.Combine(proj, p), "x");
            Assert.False(JobPackage.IsSource(proj, "m")); // the .arc is there: nothing to make
        }
        finally { Directory.Delete(proj, true); }
    }

    [Fact]
    public void Extract_Rejects_A_Package_With_Neither_Form()
    {
        var proj = Path.Combine(Path.GetTempPath(), "ash_pkg_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(proj);
        try
        {
            var zip = Path.Combine(proj, "p.zip");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (var n in new[] { "input.cells.tsv", "layout.tsv", "m.arc/agv.asy" })
                    using (var w = new StreamWriter(z.CreateEntry(n).Open())) w.Write("x");
            var ex = Assert.Throws<InvalidDataException>(() => JobPackage.Extract(zip, Path.Combine(proj, "out"), "m"));
            Assert.Contains("m.arc/model.amo", ex.Message);
            Assert.Contains("ash.settings.tsv", ex.Message);
            Assert.False(Directory.Exists(Path.Combine(proj, "out")));
        }
        finally { Directory.Delete(proj, true); }
    }

    [Fact]
    public void Package_Leaves_Out_A_Dxf_Over_The_Limit()
    {
        var proj = Project(true, false);
        try
        {
            using (var fs = new FileStream(Path.Combine(proj, JobPackage.LocalDxf), FileMode.Open)) fs.SetLength(JobPackage.MaxDxfBytes + 1);
            Assert.DoesNotContain(JobPackage.Create(proj, "m", Path.Combine(proj, "pkg.zip")), f => f.Path == "drawing.dxf");
        }
        finally { Directory.Delete(proj, true); }
    }
}
