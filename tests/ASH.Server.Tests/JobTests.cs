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

/// <summary>A result.tsv in the row layout the model prints: G1 with 3 vehicles (2 active), TAB1 with downtime, and one matstat per material.</summary>
public static class FakeResult
{
    public const double Window = 10000;

    public static string Tsv(IEnumerable<string> materials)
    {
        var sb = new System.Text.StringBuilder("kind\tname\tgroup\tv1\tv2\tv3\tv4\tv5\tv6\tv7\tv8\n");
        sb.Append("group\tG1\tG1\t2\t0\t0\t0\t0\t0\t0\t0\n");
        sb.Append($"vehicle\tTAB1\tG1\t6000\t1200\t20\t{Window}\t1\t30\t0\t0\n");
        sb.Append($"vehicle\tTAB2\tG1\t4000\t800\t10\t{Window}\t2\t0\t0\t0\n");
        sb.Append($"vehicle\tTAB3\tG1\t9999\t9999\t99\t{Window}\t3\t0\t0\t0\n"); // not active: must not count
        foreach (var m in materials)
        {
            sb.Append($"material\t{m}\tG1\t3\t4.5\t12\t15\t45\t12\t0\t0\n");
            sb.Append($"matstat\t{m}\tG1\t20.5\t60\t100.5\t200\t3\t1.5\t1\t6\n");
        }
        sb.Append("clock\t-\t-\t0\t28800\n");
        return sb.ToString();
    }
}

/// <summary>A runner that writes a believable summary instead of running AutoMod.</summary>
public sealed class FakeRunner : IJobRunner
{
    public ManualResetEventSlim Gate { get; } = new(true);
    /// <summary>Replaces the default run (after the phase is set) so a test can script progress, failure or a wait.</summary>
    public Func<IProgress<RunOutcome>, Action<SearchProgress>, CancellationToken, JobOutcome>? Script { get; set; }
    public JobOutcome Run(JobInfo job, string projectDir, TextWriter log, Action<string> phase, IProgress<RunOutcome> progress, Action<SearchProgress> search, CancellationToken ct)
    {
        phase("대수 탐색");
        if (Script != null) return Script(progress, search, ct);
        search(new SearchProgress("탐색 시작", new Dictionary<string, int>(), new Dictionary<string, int>(), 1, 3));
        Gate.Wait(ct);
        var runs = Path.Combine(projectDir, "runs");
        foreach (var (n, util) in new[] { (3, 0.4), (2, 0.6), (1, 0.95) })
        {
            var id = $"n{n}_s1";
            SummaryTsv.Append(Path.Combine(runs, "summary.tsv"), new SummaryRow(id, "G1", n, 1, util < 0.75, util < 0.75 ? "" : "가동률 초과", util, 2, 10, 10, 0, 0, 5, "G1=" + n));
            progress.Report(new RunOutcome(id, true, Path.Combine(runs, id), TimeSpan.FromSeconds(5), null));
        }
        search(new SearchProgress("확정 G1", new Dictionary<string, int> { ["G1"] = 2 }, new Dictionary<string, int>(), 0, 3));
        var input = SearchJob.ReadInput(projectDir, TextWriter.Null, new List<string>(), out _)!;
        Directory.CreateDirectory(Path.Combine(runs, "n2_s1"));
        File.WriteAllText(Path.Combine(runs, "n2_s1", "result.tsv"), FakeResult.Tsv(input.Materials.Select(m => m.Material)));
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
    public readonly FakeFilm Film = new();
    public JobStore Store => Factory.Services.GetRequiredService<JobStore>();

    public JobEnv(bool worker = true, string? amHome = null, string? adminToken = null)
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            if (adminToken != null) b.UseSetting("Admin:Token", adminToken);
            b.UseSetting("Releases:Dir", Path.Combine(Dir, "releases"));
            b.UseSetting("Jobs:Dir", Path.Combine(Dir, "jobs"));
            b.UseSetting("Jobs:WorkerEnabled", worker ? "true" : "false");
            b.UseSetting("GitHub:Enabled", "false");
            b.UseSetting("Update:PublicKeyPem", "");
            if (amHome == null)
            {
                b.UseSetting("Film:CutTimeoutMin", "0.02"); // 1.2 s: the fake hangs that long for a "hung" cut
                b.UseSetting("Film:CalibrationTimeoutMin", "0.05"); // 3 s: only checked to reach the calibration renders
                b.ConfigureServices(s =>
                {
                    s.RemoveAll<IJobRunner>(); s.AddSingleton<IJobRunner>(Runner);
                    s.RemoveAll<IFilmAutoMod>(); s.AddSingleton<IFilmAutoMod>(Film);
                    s.RemoveAll<ASH.Core.Film.IFilmRenderer>(); s.AddSingleton<ASH.Core.Film.IFilmRenderer>(Film);
                });
            }
            else { b.UseSetting("Jobs:AmHome", amHome); b.UseSetting("Jobs:DialogButton", "Student Version"); b.UseSetting("Jobs:Parallel", "2"); }
        });
        Http = Factory.CreateClient();
    }

    /// <summary>A project folder as AGVMODEL leaves it (model files + cell snapshot), packed the way the plugin packs it.</summary>
    /// <param name="dx">shifts the whole layout along X (metres), for versions whose layouts sit in different places</param>
    /// <param name="addFiles">writes extra files (ash.drawing.dxf as AGVSEND exports it, the frame) into the project folder before it is packed</param>
    public static string MakePackage(string root, string model = "m", double dx = 0, Action<string>? addFiles = null)
    {
        var proj = Path.Combine(root, "proj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(proj);
        var l = new ASH.Core.Layout.Layout();
        void Line(string n, double x0, double y0, double x1, double y1) { var p = new LayoutPath(n); p.Segments.Add(new LineSeg(new Point2(x0 + dx, y0), new Point2(x1 + dx, y1))); l.Paths.Add(p); }
        Line("p1", 0, 0, 10, 0); Line("p2", 10, 0, 10, 10); Line("p3", 10, 10, 0, 10); Line("p4", 0, 10, 0, 0);
        l.Cps.Add(new LayoutCp { Name = "S001", Type = "standby", Group = "G1", Position = new Point2(5 + dx, 0), Cap = ASH.Core.Traffic.CpTypes.DefaultCap("standby") });
        l.Cps.Add(new LayoutCp { Name = "L001", Type = "load", Group = "G1", Position = new Point2(10 + dx, 5), Cap = ASH.Core.Traffic.CpTypes.DefaultCap("load") });
        l.Cps.Add(new LayoutCp { Name = "U001", Type = "unload", Group = "G1", Position = new Point2(2 + dx, 10), Cap = ASH.Core.Traffic.CpTypes.DefaultCap("unload") });
        l.Blocks.Add(new LayoutBlock { Name = "B_x1", Cap = 1, Rect = new Rect2(new Point2(8 + dx, -1), new Point2(11 + dx, 2)) });
        var xlsx = Path.Combine(proj, "input.xlsx");
        ExcelTemplate.Write(xlsx, l);
        using (var wb = ExcelReader.Open(xlsx)) CellDump.Write(wb, Path.Combine(proj, CellDump.FileName));
        var input = ExcelReader.Read(xlsx);
        var groups = input.Groups.Select(g => new VehicleGroupSpec(g.Group, g.Group, g.MaxCount, 1.0, 1.2, 0.8)).ToList();
        var r = ModelBuilder.Build(new ModelBuildRequest(proj, model, input, l, new ExportOptions(Groups: groups)), null, TimeSpan.Zero);
        Assert.DoesNotContain(r.Findings, f => f.Code == FindingCode.InputError);
        File.Delete(xlsx); // the plugin does not send the workbook
        addFiles?.Invoke(proj);
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
    [Fact] public async Task Api_Job_Carries_Expected_Progress_And_Partial()
    {
        using var env = new JobEnv();
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        var j = await env.WaitFinished(id);
        Assert.Equal(3, j.ExpectedRuns); Assert.Equal(2, j.Partial!["G1"]); Assert.Equal(15.0, j.RunSeconds);
        var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/jobs/" + id)).RootElement;
        Assert.Equal(1.0, api.GetProperty("progress").GetDouble()); Assert.Equal(3, api.GetProperty("expectedRuns").GetInt32());
        Assert.Equal(2, api.GetProperty("partial").GetProperty("G1").GetInt32());
    }
    static SearchProgress Sp(string phase, int groupsLeft, int expected, Dictionary<string, int>? confirmed = null) =>
        new(phase, confirmed ?? new Dictionary<string, int>(), new Dictionary<string, int>(), groupsLeft, expected);
    static void Run(IProgress<RunOutcome> p, bool ok, double sec) => p.Report(new RunOutcome("r", ok, "", TimeSpan.FromSeconds(sec), null));
    static readonly JobOutcome Ok = new(0, null, null, null, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    // Per the brief a Done job reads 100 % whatever it knows (ExpectedRuns is null for old jobs); a legacy Failed job shows no bar.
    [Fact] public async Task Api_Legacy_Done_Job_Reads_Full_Progress_Without_Eta_And_Failed_Reads_Null()
    {
        using var env = new JobEnv(worker: false);
        var id = "20250101-000000-abcd";
        var j = JsonSerializer.Deserialize<JobInfo>("{\"Id\":\"" + id + "\",\"State\":\"done\",\"RunsDone\":7}")!;
        Assert.Null(j.ExpectedRuns); Assert.Null(j.Partial); Assert.Equal(0, j.RunSeconds);
        env.Store.Add(j);
        var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/jobs/" + id)).RootElement;
        Assert.Equal(1.0, api.GetProperty("progress").GetDouble());
        Assert.Equal(JsonValueKind.Null, api.GetProperty("expectedRuns").ValueKind); Assert.Equal(JsonValueKind.Null, api.GetProperty("partial").ValueKind);
        Assert.Equal(JsonValueKind.Null, api.GetProperty("eta").ValueKind);
        env.Store.Update(id, x => x.State = JobState.Failed);
        api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/jobs/" + id)).RootElement;
        Assert.Equal(JsonValueKind.Null, api.GetProperty("progress").ValueKind);
    }

    [Fact] public async Task A_Failed_Run_Counts_Its_Seconds_And_A_Thrown_Job_Is_Failed_Below_Full_With_No_Running_Phase()
    {
        using var env = new JobEnv();
        env.Runner.Script = (p, s, ct) => { s(Sp("탐색 시작", 1, 3)); Run(p, true, 3); Run(p, false, 2); throw new InvalidOperationException("boom"); };
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        var j = await env.WaitFinished(id);
        Assert.Equal(JobState.Failed, j.State); Assert.Null(j.Phase);
        Assert.Equal(5.0, j.RunSeconds); Assert.Equal(2, j.RunsDone); Assert.Equal(1, j.RunsFailed);
        Assert.True(JobProgress.Ratio(j) < 1.0);
        var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/jobs/" + id)).RootElement;
        Assert.True(api.GetProperty("progress").GetDouble() < 1.0); Assert.Equal(JsonValueKind.Null, api.GetProperty("eta").ValueKind);
    }

    [Fact] public async Task A_Cancelled_Running_Job_Is_Cancelled_Below_Full_With_No_Running_Phase()
    {
        using var env = new JobEnv();
        env.Runner.Script = (p, s, ct) => { s(Sp("탐색 시작", 1, 3)); Run(p, true, 1); ct.WaitHandle.WaitOne(); ct.ThrowIfCancellationRequested(); return Ok; };
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        for (int i = 0; i < 200 && env.Store.Get(id)!.RunsDone < 1; i++) await Task.Delay(50);
        await env.Http.PostAsync($"/jobs/{id}/cancel", new FormUrlEncodedContent(new Dictionary<string, string>()));
        var j = await env.WaitFinished(id);
        Assert.Equal(JobState.Cancelled, j.State); Assert.Null(j.Phase);
        Assert.True(JobProgress.Ratio(j) < 1.0);
    }

    [Fact] public async Task Runs_Past_The_Estimate_Raise_It_To_Runs_Plus_Groups_Left()
    {
        using var env = new JobEnv();
        env.Runner.Script = (p, s, ct) => { s(Sp("탐색 시작", 2, 3)); for (int i = 0; i < 3; i++) Run(p, true, 1); return Ok; };
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        Assert.Equal(3, (await env.WaitFinished(id)).ExpectedRuns); // reaching the estimate does not raise it
        using var env2 = new JobEnv();
        env2.Runner.Script = (p, s, ct) => { s(Sp("탐색 시작", 2, 3)); for (int i = 0; i < 4; i++) Run(p, true, 1); return Ok; };
        var id2 = Id(await (await env2.Send(JobEnv.MakePackage(env2.Dir))).Content.ReadAsStringAsync());
        Assert.Equal(4 + 2, (await env2.WaitFinished(id2)).ExpectedRuns);
    }

    [Fact] public async Task A_Throwing_Progress_Handler_Is_Logged_And_The_Job_Still_Finishes()
    {
        using var env = new JobEnv();
        env.Runner.Script = (p, s, ct) =>
        {
            s(new SearchProgress("깨진 보고", null!, null!, 1, 3)); // Confirmed.Count throws inside JobStore.Update
            Run(p, true, 1); return Ok;
        };
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        var j = await env.WaitFinished(id);
        Assert.Equal(JobState.Done, j.State); Assert.Equal(1, j.RunsDone);
    }

    [Fact] public async Task Api_Running_Job_Has_Eta_And_Average_Run_Seconds()
    {
        using var env = new JobEnv();
        var hold = new ManualResetEventSlim(false);
        env.Runner.Script = (p, s, ct) => { s(Sp("탐색 시작", 1, 4)); Run(p, true, 10); Run(p, true, 10); hold.Wait(ct); return Ok; };
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        for (int i = 0; i < 200 && env.Store.Get(id)!.RunsDone < 2; i++) await Task.Delay(50);
        var api = JsonDocument.Parse(await env.Http.GetStringAsync("/api/jobs/" + id)).RootElement;
        hold.Set(); await env.WaitFinished(id);
        Assert.Equal(0.5, api.GetProperty("progress").GetDouble(), 3); Assert.Equal(10.0, api.GetProperty("avgRunSec").GetDouble(), 3);
        var eta = DateTime.Parse(api.GetProperty("eta").GetString()!, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        Assert.InRange((eta - DateTime.UtcNow).TotalSeconds, 5, 15); // (4-2) x 10 s / 2 (default Jobs:Parallel)
    }

    static string Id(string json) => JsonDocument.Parse(json).RootElement.GetProperty("id").GetString()!;

    [Fact]
    public async Task Upload_Queues_A_Job_And_The_Pages_Show_It()
    {
        using var env = new JobEnv(worker: false, adminToken: "test-token-1234567890");
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

        var lreq = new HttpRequestMessage(HttpMethod.Get, "/admin/jobs"); lreq.Headers.Add("X-ASH-Admin-Token", "test-token-1234567890");
        var list = await (await env.Http.SendAsync(lreq)).Content.ReadAsStringAsync();
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

        // the new statistics, from the fake confirm run (FakeResult): TAB1 60 % / TAB2 40 % busy, TAB3 is not active
        var mats = JobView.For(env.Store, j).Materials;
        Assert.NotEmpty(mats);
        Assert.Contains("<h2>차량</h2>", summary);
        Assert.Contains("<tr><td>G1</td><td class=\"num\">2</td><td class=\"num\">50.0</td><td class=\"num\">15</td><td class=\"num\">1000</td><td class=\"num\">66.7</td><td class=\"num\">333.3</td></tr>", summary);
        Assert.Contains("<h2>자재별 리드타임·재고</h2>", summary);
        Assert.Contains($"<td>{mats[0].Name}</td><td>G1</td><td class=\"num\">12 / 15</td><td class=\"num\">45</td><td class=\"num\">20.5 / 60.0</td><td class=\"num\">100.5 / 200.0</td>" +
            "<td class=\"num\">3.0</td><td class=\"num\">1.5</td><td class=\"num\">1</td><td class=\"num\">6</td>", summary);
        Assert.True(mats[0].Warn);                  // 3 - 2*1.5 = 0 < the safety stock
        Assert.Contains("<b>주의</b>", summary);
        Assert.Contains("<tr><td>TAB1</td><td>G1</td><td class=\"num\">60.0</td><td class=\"num\">20</td><td class=\"num\">1200</td><td class=\"num\">60</td><td class=\"num\">300</td><td class=\"num\">30</td></tr>", detail);
        Assert.Contains("<th>고장 시간(초)</th>", detail);
        Assert.Contains("<td>TAB2</td><td>G1</td><td class=\"num\">40.0</td>", detail);
        Assert.DoesNotContain("TAB3", detail);
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

    [Fact]
    public async Task Run_List_Sorts_By_Every_Column_And_Each_Run_Downloads_Its_Model()
    {
        using var env = new JobEnv();
        var id = Id(await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync());
        await env.WaitFinished(id);
        var project = env.Store.ProjectDir(id);
        // the build output as the server leaves it (the fake runner does not build)
        File.WriteAllText(Path.Combine(project, "m.exe"), "exe");
        Directory.CreateDirectory(Path.Combine(project, "m.dir"));
        File.WriteAllText(Path.Combine(project, "m.dir", "m64.dll"), "dll");
        File.WriteAllText(Path.Combine(project, "m.arc", "params.txt"), "SEED 999\n");

        var detail = await env.Http.GetStringAsync($"/jobs/{id}?tab=detail");
        Assert.Contains("<table class=\"sort\"><thead><tr><th aria-sort=\"none\"><button type=\"button\">실행</button></th><th aria-sort=\"none\"><button type=\"button\">G1 대수(대)</button></th>", detail);
        Assert.Contains("<button type=\"button\">반복(회차)</button>", detail);
        Assert.Contains("table.sort", detail);    // the inline sort script, nothing loaded from another host
        Assert.DoesNotContain("<script src", detail);
        Assert.Contains($"<a href=\"/jobs/{id}/runs/n2_s1/model.zip\" download>받기</a>", detail);

        var res = await env.Http.GetAsync($"/jobs/{id}/runs/n2_s1/model.zip");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/zip", res.Content.Headers.ContentType!.MediaType);
        using (var zip = new ZipArchive(await res.Content.ReadAsStreamAsync()))
        {
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("m.exe", names); Assert.Contains("m.dir/m64.dll", names); Assert.Contains("m.arc/agv.asy", names);
            Assert.Single(names, n => n == "m.arc/params.txt");
            Assert.DoesNotContain(names, n => n.StartsWith("runs/") || n.StartsWith("work/"));
            string Read(string n) { using var rd = new StreamReader(zip.GetEntry(n)!.Open()); return rd.ReadToEnd(); }
            var p = Read("m.arc/params.txt");
            Assert.StartsWith("SEED 1\nGROUPS 1\n1 2\n", p); // this run's seed and count, not the file left in the project
            var bat = Read("run.bat");
            Assert.Contains("start \"\" \"m.exe\"", bat);
            Assert.DoesNotContain("-w", bat);
            Assert.True(bat.All(c => c < 128));       // cmd reads a .bat in the OEM code page
            Assert.Contains("G1: 2대", Read("README.txt"));
        }

        // the run id is looked up, never used as a path
        foreach (var bad in new[] { "n9_s1", "..%2Fjob.json", "n2_s1%5C..", "x.y" })
            Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync($"/jobs/{id}/runs/{bad}/model.zip")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync("/jobs/nope/runs/n2_s1/model.zip")).StatusCode);

        // build output purged (JobRetention): the .arc alone, run.bat builds it first
        File.Delete(Path.Combine(project, "m.exe"));
        using (var zip = new ZipArchive(await env.Http.GetStreamAsync($"/jobs/{id}/runs/n1_s1/model.zip")))
        {
            Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("m.dir/") || e.FullName == "m.exe");
            Assert.Contains(zip.Entries, e => e.FullName == "m.arc/params.txt");
        }
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
        var bytes = await env.Http.GetByteArrayAsync($"/jobs/{id}/runs/{j.ConfirmRunIds[0]}/model.zip");
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "ash-job-run.zip"), bytes);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Contains(zip.Entries, e => e.FullName == "m.exe");
        Assert.Contains(zip.Entries, e => e.FullName == "m.dir/m64.dll");
    }
}
