using ASH.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace ASH.Server.Tests;

public class FileIndexTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "fidx-" + Guid.NewGuid().ToString("N"));
    public FileIndexTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Materials_Come_From_The_Materials_Sheet_Only()
    {
        var p = Path.Combine(_dir, "input.cells.tsv");
        File.WriteAllText(p, "#ASH-CELLS 1\nMaterials\t1\t1\tS\tMaterial\nMaterials\t1\t2\tS\tStation\nMaterials\t2\t1\tS\t자재 코드(설명)\nbad\nMaterials\tx\t1\tS\tM-BAD\nMaterials\t3\t1\tS\tM-100\nMaterials\t4\t1\tS\tM-200\nRoutes\t2\t1\tS\tM-999\n");
        Assert.Equal(new[] { "M-100", "M-200" }, FileIndex.MaterialsFromCells(p));
        Assert.Empty(FileIndex.MaterialsFromCells(Path.Combine(_dir, "none.tsv")));
    }

    [Fact, System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Ensure_Survives_An_Unreadable_Index_Json()
    {
        var js = new JobStore(Path.Combine(_dir, "jobs"), NullLogger.Instance);
        const string id = "20260101-000000-cccc";
        js.Add(new JobInfo { Id = id, State = JobState.Done, CreatedUtc = DateTime.UtcNow });
        Directory.CreateDirectory(js.ProjectDir(id));
        File.WriteAllText(Path.Combine(js.ProjectDir(id), "input.cells.tsv"), "#ASH-CELLS 1\nMaterials\t1\t1\tS\tMaterial\nMaterials\t2\t1\tS\tdesc\nMaterials\t3\t1\tS\tM7\n");
        var path = Path.Combine(js.JobDir(id), FileIndex.FileName);
        File.WriteAllText(path, "{}");
        var fi = new FileInfo(path);
        var sec = fi.GetAccessControl();
        var deny = new System.Security.AccessControl.FileSystemAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User!, System.Security.AccessControl.FileSystemRights.Read, System.Security.AccessControl.AccessControlType.Deny);
        sec.AddAccessRule(deny); fi.SetAccessControl(sec);
        try { Assert.Equal(new[] { "M7" }, FileIndex.Ensure(js, id)); } // read denied: falls back to the cells instead of throwing
        finally { sec.RemoveAccessRule(deny); fi.SetAccessControl(sec); }
    }

    [Fact]
    public void Ensure_Writes_Index_Once_And_BuildMissing_Fills_Old_Jobs()
    {
        var js = new JobStore(Path.Combine(_dir, "jobs"), NullLogger.Instance);
        string a = "20260101-000000-aaaa", b = "20260101-000001-bbbb";
        foreach (var id in new[] { a, b }) js.Add(new JobInfo { Id = id, State = JobState.Done, CreatedUtc = DateTime.UtcNow });
        Directory.CreateDirectory(js.ProjectDir(a));
        File.WriteAllText(Path.Combine(js.ProjectDir(a), "input.cells.tsv"), "#ASH-CELLS 1\nMaterials\t1\t1\tS\tMaterial\nMaterials\t2\t1\tS\tdesc\nMaterials\t3\t1\tS\tM1\n");

        FileIndex.BuildMissing(js, NullLogger.Instance);
        Assert.True(File.Exists(Path.Combine(js.JobDir(a), FileIndex.FileName)));
        Assert.True(File.Exists(Path.Combine(js.JobDir(b), FileIndex.FileName)));
        Assert.Equal(new[] { "M1" }, FileIndex.Ensure(js, a));
        Assert.Empty(FileIndex.Ensure(js, b));
        File.Delete(Path.Combine(js.ProjectDir(a), "input.cells.tsv"));
        Assert.Equal(new[] { "M1" }, FileIndex.Ensure(js, a)); // cached, not rebuilt
    }
}
