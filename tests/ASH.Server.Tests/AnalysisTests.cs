using ASH.Server;
using Microsoft.Extensions.Logging.Abstractions;

namespace ASH.Server.Tests;

public class AnalysisTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "ash-analysis-test-" + Guid.NewGuid().ToString("N"));
    JobStore Jobs() => new(_dir, NullLogger.Instance);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    static string AddJob(JobStore js, string id)
    {
        js.Add(new JobInfo { Id = id, CreatedUtc = DateTime.UtcNow });
        return id;
    }

    [Fact]
    public void Assign_Gives_Next_Version_Per_Plan()
    {
        var js = Jobs(); var st = new AnalysisStore(_dir);
        var a = st.CreateAnalysis("분석", ""); var pa = st.CreatePlan(a.Id, "A안", ""); var pb = st.CreatePlan(a.Id, "B안", "");
        Assert.Matches("^a-[0-9a-f]{8}$", a.Id); Assert.Matches("^p-[0-9a-f]{8}$", pa.Id);
        var v = new[] { "j0000000001", "j0000000002", "j0000000003" }.Select(id => st.Assign(js, AddJob(js, id), a.Id, pa.Id, "메모").Version).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, v);
        Assert.Equal(1, st.Assign(js, AddJob(js, "j0000000004"), a.Id, pb.Id, "").Version);
        Assert.Equal("메모", js.Get("j0000000001")!.VersionNote);
        st.Move(js, "j0000000001", a.Id, pb.Id);
        Assert.Equal(2, js.Get("j0000000001")!.Version);
        Assert.Equal(pb.Id, js.Get("j0000000001")!.PlanId);
    }

    [Fact]
    public void Assign_Is_Unique_Under_Parallel_Calls()
    {
        var js = Jobs(); var st = new AnalysisStore(_dir);
        var a = st.CreateAnalysis("분석", ""); var p = st.CreatePlan(a.Id, "A안", "");
        var ids = Enumerable.Range(0, 20).Select(i => AddJob(js, $"j{i:D10}")).ToArray();
        var got = new System.Collections.Concurrent.ConcurrentBag<int>();
        Parallel.For(0, 20, i => got.Add(st.Assign(js, ids[i], a.Id, p.Id, "").Version));
        Assert.Equal(Enumerable.Range(1, 20), got.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void Survives_Reload()
    {
        var st = new AnalysisStore(_dir);
        var a = st.CreateAnalysis("분석1", "설명"); var p = st.CreatePlan(a.Id, "A안", "n");
        st.Rename(a.Id, p.Id, "바뀐 안");
        var again = new AnalysisStore(_dir).Get(a.Id)!;
        Assert.Equal("분석1", again.Name); Assert.Equal("설명", again.Note);
        Assert.Equal(p.Id, again.Plans.Single().Id); Assert.Equal("바뀐 안", again.Plans.Single().Name);
    }

    [Fact]
    public void DeleteIfEmpty_Refuses_When_A_Job_Points_At_It()
    {
        var js = Jobs(); var st = new AnalysisStore(_dir);
        var a = st.CreateAnalysis("분석", ""); var p = st.CreatePlan(a.Id, "A안", "");
        st.Assign(js, AddJob(js, "j0000000001"), a.Id, p.Id, "");
        Assert.False(st.DeleteIfEmpty(a.Id, js));
        Assert.NotNull(st.Get(a.Id));
        var b = st.CreateAnalysis("빈 분석", "");
        Assert.True(st.DeleteIfEmpty(b.Id, js));
        Assert.Null(new AnalysisStore(_dir).Get(b.Id));
    }

    [Fact]
    public void Duplicate_Plan_Name_In_Analysis_Throws_ArgumentException()
    {
        var st = new AnalysisStore(_dir);
        var a = st.CreateAnalysis("분석", ""); var other = st.CreateAnalysis("다른 분석", "");
        st.CreatePlan(a.Id, "A안", "");
        Assert.Throws<ArgumentException>(() => st.CreatePlan(a.Id, "A안", ""));
        st.CreatePlan(other.Id, "A안", ""); // same name in another analysis is fine
    }

    [Fact]
    public void Corrupt_File_Is_Moved_Aside_And_Store_Starts_Empty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "analyses.json"), "{garbage");
        var st = new AnalysisStore(_dir, NullLogger.Instance);
        Assert.Empty(st.All());
        Assert.Equal("{garbage", File.ReadAllText(Directory.GetFiles(_dir, "analyses.json.bad-*").Single()));
        var a = st.CreateAnalysis("새 분석", "");
        Assert.Equal(a.Id, new AnalysisStore(_dir).All().Single().Id);
        Assert.Throws<ArgumentException>(() => st.CreatePlan("a-00000000", "A안", "")); // a job's dangling analysis id is simply not found
    }

    [Fact]
    public void Unreadable_But_Valid_File_Is_Not_Moved_Aside()
    {
        var st = new AnalysisStore(_dir);
        st.CreateAnalysis("분석", "");
        var path = Path.Combine(_dir, "analyses.json");
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => new AnalysisStore(_dir, NullLogger.Instance));
        Assert.Empty(Directory.GetFiles(_dir, "analyses.json.bad-*"));
        Assert.Single(new AnalysisStore(_dir).All());
    }

    [Fact]
    public void Old_Job_Json_Loads_As_Unclassified()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "20260101-000000-abcd"));
        File.WriteAllText(Path.Combine(_dir, "20260101-000000-abcd", "job.json"), "{\"Id\":\"20260101-000000-abcd\",\"State\":\"done\"}");
        var j = Jobs().Get("20260101-000000-abcd")!;
        Assert.Equal(0, j.Version); Assert.Null(j.AnalysisId); Assert.False(j.Selected);
    }
}
