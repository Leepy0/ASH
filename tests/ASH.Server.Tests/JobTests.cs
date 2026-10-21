using System.IO.Compression;
using System.Net;
using System.Text.Json;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ASH.Server.Tests;

/// <summary>A runner that writes a believable summary instead of running AutoMod.</summary>
public sealed class FakeRunner : IJobRunner
{
    public ManualResetEventSlim Gate { get; } = new(true);
    public JobOutcome Run(JobInfo job, string projectDir, TextWriter log, Action<string> phase, IProgress<RunOutcome> progress, CancellationToken ct)
    {
        phase("대수 탐색");
        Gate.Wait(ct);
        var runs = Path.Combine(projectDir, "runs");
        foreach (var (n, util) in new[] { (3, 0.4), (2, 0.6), (1, 0.95) })
        {
            var id = $"n{n}_s1";
            SummaryTsv.Append(Path.Combine(runs, "summary.tsv"), new SummaryRow(id, "G1", n, 1, util < 0.75, util < 0.75 ? "" : "가동률 초과", util, 2, 10, 10, 0, 0, 5, "G1=" + n));
            progress.Report(new RunOutcome(id, true, Path.Combine(runs, id), TimeSpan.FromSeconds(5), null));
        }
        log.WriteLine("fake done");
        return new JobOutcome(0, null, new Dictionary<string, int> { ["G1"] = 2 }, new[] { new[] { "G1" } }, new[] { "n2_s1" }, new[] { "샘플 경고" }, Array.Empty<string>());
    }
}

public sealed class JobEnv : IDisposable
{
    public readonly string Dir = Path.Combine(Path.GetTempPath(), "ash-jobs-test-" + Guid.NewGuid().ToString("N"));
    public readonly WebApplicationFactory<Program> Factory;
    public readonly HttpClient Http;
    public readonly FakeRunner Runner = new();
    public JobStore Store => Factory.Services.GetRequiredService<JobStore>();

    public JobEnv(bool worker = true, string? amHome = null)
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("Releases:Dir", Path.Combine(Dir, "releases"));
            b.UseSetting("Jobs:Dir", Path.Combine(Dir, "jobs"));
            b.UseSetting("Jobs:WorkerEnabled", worker ? "true" : "false");
            b.UseSetting("GitHub:Enabled", "false");
            b.UseSetting("Update:PublicKeyPem", "");
            if (amHome == null) b.ConfigureServices(s => { s.RemoveAll<IJobRunner>(); s.AddSingleton<IJobRunner>(Runner); });
            else { b.UseSetting("Jobs:AmHome", amHome); b.UseSetting("Jobs:DialogButton", "Student Version"); b.UseSetting("Jobs:Parallel", "2"); }
        });
        Http = Factory.CreateClient();
    }

    /// <summary>A project folder as AGVMODEL leaves it (model files + cell snapshot), packed the way the plugin packs it.</summary>
    public static string MakePackage(string root, string model = "m")
    {
        var proj = Path.Combine(root, "proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(proj);
        var l = new ASH.Core.Layout.Layout();
        void Line(string n, double x0, double y0, double x1, double y1) { var p = new LayoutPath(n); p.Segments.Add(new LineSeg(new Point2(x0, y0), new Point2(x1, y1))); l.Paths.Add(p); }
        Line("p1", 0, 0, 10, 0); Line("p2", 10, 0, 10, 10); Line("p3", 10, 10, 0, 10); Line("p4", 0, 10, 0, 0);
        l.Cps.Add(new LayoutCp { Name = "S001", Type = "standby", Group = "G1", Position = new Point2(5, 0), Cap = 1 });
        l.Cps.Add(new LayoutCp { Name = "L001", Type = "load", Group = "G1", Position = new Point2(10, 5), Cap = 1 });
        l.Cps.Add(new LayoutCp { Name = "U001", Type = "unload", Group = "G1", Position = new Point2(2, 10), Cap = 1 });
        l.Blocks.Add(new LayoutBlock { Name = "B_x1", Cap = 1, Rect = new Rect2(new Point2(8, -1), new Point2(11, 2)) });
        var xlsx = Path.Combine(proj, "input.xlsx");
        ExcelTemplate.Write(xlsx, l);
        using (var wb = ExcelReader.Open(xlsx)) CellDump.Write(wb, Path.Combine(proj, CellDump.FileName));
        var input = ExcelReader.Read(xlsx);
        var groups = input.Groups.Select(g => new VehicleGroupSpec(g.Group, g.Type, g.MaxCount, 1.0, 1.2, 0.8)).ToList();
        var r = ModelBuilder.Build(new ModelBuildRequest(proj, model, input, l, new ExportOptions(Groups: groups)), null, TimeSpan.Zero);
        Assert.DoesNotContain(r.Findings, f => f.Code == FindingCode.InputError);
        File.Delete(xlsx); // the plugin does not send the workbook
        var zip = Path.Combine(root, "pkg-" + Guid.NewGuid().ToString("N") + ".zip");
        JobPackage.Create(proj, model, zip);
        return zip;
    }

    public async Task<HttpResponseMessage> Send(string zip, string model = "m")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("Line A"), "project" }, { new StringContent("plan 1"), "plan" }, { new StringContent(model), "model" },
            { new StringContent("tester"), "submitter" }, { new StringContent("1"), "impact" },
            { new ByteArrayContent(await File.ReadAllBytesAsync(zip)), "package", "package.zip" },
        };
        return await Http.PostAsync("/api/jobs", form);
    }

    public async Task<JobInfo> WaitFinished(string id, int seconds = 10)
    {
        for (int i = 0; i < seconds * 20; i++)
        {
            var j = Store.Get(id)!;
            if (j.Finished) return j;
            await Task.Delay(50);
        }
        throw new TimeoutException("job did not finish");
    }

    public void Dispose()
    {
        Http.Dispose(); Factory.Dispose();
        try { Directory.Delete(Dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

public class JobTests
{
    static string Id(string json) => JsonDocument.Parse(json).RootElement.GetProperty("id").GetString()!;

    [Fact]
    public async Task Upload_Queues_A_Job_And_The_Pages_Show_It()
    {
        using var env = new JobEnv(worker: false);
        var zip = JobEnv.MakePackage(env.Dir);
        var res = await env.Send(zip);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var id = Id(body);
        Assert.Contains("/jobs/" + id, body);
        var j = env.Store.Get(id)!;
        Assert.Equal(JobState.Queued, j.State);
        Assert.Contains(j.Files, f => f.Path == "m.arc/agv.asy" && f.Sha256.Length == 64);
        Assert.True(File.Exists(Path.Combine(env.Store.ProjectDir(id), "m.arc", "agv.asy")));

        var q = JsonDocument.Parse(await env.Http.GetStringAsync("/api/jobs/queue")).RootElement;
        Assert.Equal(1, q.GetProperty("ahead").GetInt32());
        Assert.False(q.GetProperty("enabled").GetBoolean()); // worker off

        var list = await env.Http.GetStringAsync("/jobs");
        Assert.Contains(id, list); Assert.Contains("Line A", list); Assert.Contains("대기", list);
        var page = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.Contains("다음 차례", page);
    }

    [Fact]
    public async Task Rejects_A_Package_Without_The_Model_Or_With_A_Bad_Path()
    {
        using var env = new JobEnv(worker: false);
        var bad = Path.Combine(env.Dir, "bad.zip");
        Directory.CreateDirectory(env.Dir);
        using (var z = ZipFile.Open(bad, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("../evil.txt").Open())) w.Write("x");
        }
        var res = await env.Send(bad);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("경로", await res.Content.ReadAsStringAsync());
        Assert.Empty(env.Store.All());

        var zip = JobEnv.MakePackage(env.Dir);
        var wrong = await env.Send(zip, model: "other");
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("필수 파일", await wrong.Content.ReadAsStringAsync());
        var name = await env.Send(zip, model: "../x");
        Assert.Equal(HttpStatusCode.BadRequest, name.StatusCode);
    }

    [Fact]
    public async Task Worker_Runs_The_Job_And_The_Result_Pages_Show_The_Recommendation()
    {
        using var env = new JobEnv();
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        var j = await env.WaitFinished(id);
        Assert.Equal(JobState.Done, j.State);
        Assert.Equal(3, j.RunsDone);
        Assert.Equal(2, j.Decided!["G1"]);

        var summary = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.Contains("권장 AGV 총 대수", summary);
        Assert.Contains("2대", summary);
        Assert.Contains("<svg", summary);         // the block maps
        Assert.Contains("샘플 경고", summary);
        var detail = await env.Http.GetStringAsync($"/jobs/{id}?tab=detail");
        Assert.Contains("권장 2", detail);         // the decided marker on the curve
        Assert.Contains("n1_s1", detail);          // run list
        var input = await env.Http.GetStringAsync($"/jobs/{id}?tab=input");
        Assert.Contains("m.arc/agv.asy", input);
        var files = await env.Http.GetStringAsync($"/jobs/{id}?tab=files");
        Assert.Contains("fake done", files);

        var tsv = await env.Http.GetStringAsync($"/jobs/{id}/files/summary.tsv");
        Assert.StartsWith("runId\tgroup", tsv);
        var runsZip = await env.Http.GetAsync($"/jobs/{id}/files/runs.zip");
        Assert.Equal(HttpStatusCode.OK, runsZip.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync($"/jobs/{id}/files/..%2Fjob.json")).StatusCode);
    }

    // the plugin's client against the real endpoints: same JSON and multipart contract on both sides
    [Fact]
    public async Task The_Plugin_Client_Talks_To_The_Server()
    {
        using var env = new JobEnv(worker: false);
        using var client = new JobServerClient(env.Factory.Server.BaseAddress, env.Factory.Server.CreateHandler());
        var q = await client.GetQueueAsync();
        Assert.Equal(0, q.Ahead);
        var receipt = await client.SubmitAsync(JobEnv.MakePackage(env.Dir), new JobSubmission("Line A", "plan 1", "m", "tester"));
        Assert.False(string.IsNullOrEmpty(receipt.Id));
        Assert.Contains("/jobs/" + receipt.Id, receipt.Url);
        Assert.Equal(JobState.Queued, env.Store.Get(receipt.Id)!.State);
        var q2 = await client.GetQueueAsync();
        Assert.Equal(1, q2.Ahead);
        var ex = await Assert.ThrowsAsync<JobServerException>(() => client.SubmitAsync(JobEnv.MakePackage(env.Dir), new JobSubmission("x", "y", "other", "z")));
        Assert.Contains("필수 파일", ex.Message);
    }

    [Fact]
    public async Task A_Queued_Job_Can_Be_Cancelled_By_The_Sender()
    {
        using var env = new JobEnv(worker: false);
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        var res = await env.Http.PostAsync($"/jobs/{id}/cancel", new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.True(res.IsSuccessStatusCode || res.StatusCode == HttpStatusCode.Redirect);
        Assert.Equal(JobState.Cancelled, env.Store.Get(id)!.State);
    }

    [Fact]
    public async Task A_Running_Job_Is_Marked_Failed_When_The_Server_Restarts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash-jobs-restart-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
            var s1 = new JobStore(dir, log);
            s1.Add(new JobInfo { Id = JobStore.NewId(), Model = "m", CreatedUtc = DateTime.UtcNow });
            var running = s1.TakeNext()!;
            var s2 = new JobStore(dir, log);
            var j = s2.Get(running.Id)!;
            Assert.Equal(JobState.Failed, j.State);
            Assert.Contains("다시 시작", j.Error);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Queue_Estimate_Uses_Finished_Job_Lengths()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash-jobs-eta-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new JobStore(dir, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            var now = DateTime.UtcNow;
            s.Add(new JobInfo { Id = "20260101-000000-aaaa", State = JobState.Done, CreatedUtc = now.AddHours(-2), StartedUtc = now.AddHours(-2), FinishedUtc = now.AddHours(-1) });
            s.Add(new JobInfo { Id = "20260101-000001-bbbb", CreatedUtc = now });
            var (ahead, start, typical) = s.QueueInfo();
            Assert.Equal(1, ahead);
            Assert.Equal(60, typical!.Value.TotalMinutes, 3);
            Assert.True(start > now.AddMinutes(59));
        }
        finally { Directory.Delete(dir, true); }
    }

    // the whole path with AutoMod: upload, server-side build, search, result pages (set ASH_AM_HOME, e.g. C:\AutoMod\12_6)
    [SkippableFact]
    public async Task Real_AutoMod_Job_Builds_Searches_And_Shows_A_Result()
    {
        var am = Environment.GetEnvironmentVariable("ASH_AM_HOME");
        Skip.If(am is null, "ASH_AM_HOME not set");
        using var env = new JobEnv(amHome: am);
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        var j = await env.WaitFinished(id, 900);
        var log = File.ReadAllText(env.Store.LogPath(id));
        Assert.True(j.State == JobState.Done, j.Error + "\n" + log);
        Assert.NotNull(j.Decided);
        Assert.True(j.RunsDone > 0);
        Assert.NotEmpty(j.ConfirmRunIds);
        var page = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.Contains("권장 AGV 총 대수", page);
        Assert.Contains("B_x1", page); // the block on the map
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "ash-job-page.html"), page);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "ash-job-detail.html"), await env.Http.GetStringAsync("/jobs/" + id + "?tab=detail"));
    }
}
