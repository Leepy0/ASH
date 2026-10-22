using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using ASH.Core.AutoMod;
using ASH.Core.Film;
using ASH.Core.Input;
using ASH.Core.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

/// <summary>AutoMod and AutoView stand-ins: builds succeed, runs write a result.tsv (and an events.tsv when asked), renders write a file named as the script says.
/// A cut starting at 4321 s hangs until the cut time limit. Everything asked is recorded for the asserts.</summary>
public sealed class FakeFilm : IFilmAutoMod, IFilmRenderer
{
    public readonly List<(string Dir, string Amo)> Builds = new();
    public readonly List<RunRequest> Runs = new();
    public readonly List<string> Scripts = new();
    public readonly List<(IReadOnlyList<string> Mp4s, IReadOnlyList<string> Captions, string Out, bool Join)> Finishes = new();
    public (double W, double H)? Mark = (640, 360);
    public bool WriteEvents = true;
    /// <summary>Every render hits the time limit at once (a hung AutoView).</summary>
    public bool AlwaysTimeout;
    /// <summary>A cut script with this text waits until the render is cancelled (an AutoView run that is still going).</summary>
    public string? BlockOn;

    public AmBuildResult Build(string projectDir, string model)
    {
        var amo = Path.Combine(projectDir, model + ".arc", "model.amo");
        lock (Builds) Builds.Add((projectDir, File.Exists(amo) ? File.ReadAllText(amo) : ""));
        return new AmBuildResult(true, "", "", Array.Empty<string>());
    }

    public RunOutcome Run(RunRequest r)
    {
        lock (Runs) Runs.Add(r);
        var id = ModelRunner.RunId(r.Input, r.Scenario);
        var dir = Path.Combine(r.RunsDir ?? Path.Combine(r.ProjectDir, "runs"), id);
        Directory.CreateDirectory(dir);
        var tsv = Path.Combine(dir, "result.tsv");
        File.WriteAllText(tsv, FakeResult.Tsv(r.Input.Materials.Select(m => m.Material)));
        var mat = r.Input.Materials[0].Material;
        if (r.Scenario.Events && WriteEvents) File.WriteAllText(Path.Combine(dir, "events.tsv"), $"t\tkind\tmat\tcp\tvalue\n1200\tstockout\t{mat}\t\t0\n2100\tstockin\t{mat}\t\t900\n");
        return new RunOutcome(id, true, dir, TimeSpan.FromSeconds(1), null, 0, null, tsv, null);
    }

    public RenderResult Render(string workDir, string modelName, string cameraScript, TimeSpan timeout, CancellationToken ct)
    {
        lock (Scripts) Scripts.Add(cameraScript);
        if (AlwaysTimeout) return new RenderResult(false, null, "시간 제한을 넘어 AutoView를 끊었습니다.", Array.Empty<string>(), true);
        if (BlockOn != null && cameraScript.Contains(BlockOn)) { ct.WaitHandle.WaitOne(TimeSpan.FromMinutes(1)); return new RenderResult(false, null, "취소되었습니다.", Array.Empty<string>()); }
        // the cut, not its model's calibration (which also reaches 4321 s)
        if (cameraScript.Contains("\ntime 4321\n") && !cameraScript.Contains("quality 100")) { ct.WaitHandle.WaitOne(timeout); return new RenderResult(false, null, "시간 제한을 넘어 AutoView를 끊었습니다.", Array.Empty<string>(), true); }
        var mp4 = AutoViewRenderer.MovieFile(cameraScript) + ".mp4";
        File.WriteAllText(mp4, "raw");
        return new RenderResult(true, mp4, null, Array.Empty<string>());
    }

    public string Finish(IReadOnlyList<string> mp4s, IReadOnlyList<string> captions, string outPath, bool join, CancellationToken ct = default)
    {
        lock (Finishes) Finishes.Add((mp4s.ToList(), captions.ToList(), outPath, join));
        Assert.All(mp4s, m => Assert.True(File.Exists(m), m));
        File.WriteAllText(outPath, join ? "joined" : "mp4");
        return outPath;
    }

    public (double W, double H)? MarkSize(string mp4With, string mp4Without, int width, int height, CancellationToken ct = default) => Mark;
}

public class FilmTests
{
    const string Ok = "http://localhost";

    static CutSpec Hold(string id, string name = "입구", double start = 7200) => new(id, name, CutKind.Hold, start, 600, new Rect(1, 2, 30, 18), null, 0, 0, 0, 10, 30, 0, true, true);
    static CutSpec Move(string id) => new(id, "L001 → U001", CutKind.Move, 9000, 0, new Rect(0, 0, 10, 10), new Rect(20, 5, 12, 8), 120, 60, 90, 20, 45, 90, false, true);

    static async Task<T> Until<T>(Func<T?> get, int seconds = 60) where T : class
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(seconds)) { if (get() is { } v) return v; await Task.Delay(100); }
        throw new TimeoutException("기다린 상태가 오지 않았습니다");
    }

    /// <summary>Sends a job filed under analysis/plan and waits until the fake search finished it.</summary>
    static async Task<string> Send(JobEnv env, string analysis, string plan, Action<string>? addFiles = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("Line A"), "project" }, { new StringContent("m"), "model" }, { new StringContent("tester"), "submitter" },
            { new StringContent(analysis), "analysisName" }, { new StringContent(plan), "planName" },
            { new ByteArrayContent(await File.ReadAllBytesAsync(JobEnv.MakePackage(env.Dir, addFiles: addFiles))), "package", "package.zip" },
        };
        return JsonDocument.Parse(await (await env.Http.PostAsync("/api/jobs", form)).Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
    }

    static async Task<string> Done(JobEnv env, string analysis, string plan)
    {
        var id = await Send(env, analysis, plan);
        await Until(() => env.Store.Get(id) is { Finished: true } j ? j : null);
        Assert.Equal(JobState.Done, env.Store.Get(id)!.State);
        return id;
    }

    static Task<HttpResponseMessage> Post(JobEnv env, string analysisId, object body, string? origin = Ok)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/analyses/{analysisId}/films") { Content = new StringContent(JsonSerializer.Serialize(body, CutStore.Json), Encoding.UTF8, "application/json") };
        if (origin != null) req.Headers.Add("Origin", origin);
        return env.Http.SendAsync(req);
    }

    static object Body(IEnumerable<string> cuts, IEnumerable<string> jobs, string output = "each+joined", int delta = 0, Dictionary<string, int>? same = null, int? seed = null) =>
        new { cutIds = cuts, jobIds = jobs, width = 1280, height = 720, fps = 15, output, caption = true, vehicleDelta = delta, sameCounts = same, seed };

    /// <summary>Posts the film and waits until it is finished.</summary>
    static async Task<FilmInfo> Film(JobEnv env, string analysisId, object body)
    {
        var res = await Post(env, analysisId, body);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var id = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        var fs = env.Factory.Services.GetRequiredService<FilmStore>();
        return await Until(() => fs.Get(id) is { Finished: true } f ? f : null);
    }

    static (AnalysisStore St, string AnalysisId) Analysis(JobEnv env) { var st = env.Factory.Services.GetRequiredService<AnalysisStore>(); return (st, st.All().Single().Id); }

    static ProjectInput InputOf(JobEnv env, string jobId) => SearchJob.ReadInput(env.Store.ProjectDir(jobId), TextWriter.Null, new List<string>(), out _)!;

    [Fact]
    public async Task Film_Runs_Each_Cut_For_Each_Version_And_Joins()
    {
        using var env = new JobEnv();
        var j1 = await Done(env, "분석", "A안");
        var j2 = await Done(env, "분석", "B안");
        var (_, aid) = Analysis(env);
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1"), Move("c2") });
        var f = await Film(env, aid, Body(new[] { "c1", "c2" }, new[] { j1, j2 }));

        Assert.Equal(JobState.Done, f.State);
        Assert.Equal((4, 0), (f.Done, f.Failed));
        Assert.Equal(new[] { ("c1", j1), ("c1", j2), ("c2", j1), ("c2", j2) }, f.Items.Select(i => (i.CutId, i.JobId))); // cut by cut, every version
        var dir = env.Factory.Services.GetRequiredService<FilmStore>().FilmDir(f.Id);
        Assert.All(f.Items, i => Assert.True(File.Exists(Path.Combine(dir, i.Name)), i.Name));
        var join = env.Film.Finishes.Single(x => x.Join);
        Assert.Equal(f.Items.Select(i => Path.Combine(dir, i.Name)), join.Mp4s);
        Assert.True(f.JoinedOk); Assert.Equal(Path.Combine(dir, f.Joined!), join.Out);

        // one film model per version: built from a copy with the drawing slot, the ash_cal mark and only the cuts' window recorded
        var builds = env.Film.Builds.Where(b => b.Dir.StartsWith(dir)).ToList();
        Assert.Equal(2, builds.Count);
        Assert.All(builds, b => Assert.Contains("STATIC name ash_cal", b.Amo));
        Assert.All(builds, b => Assert.Contains("Hours counts 1 autoview\n", b.Amo)); // 7198 .. 9272 s of 8 h
        Assert.False(File.Exists(Path.Combine(env.Store.ProjectDir(j1), "m.arc", "ash_cal.asy"))); // the job's own folder is not touched
        Assert.False(Directory.Exists(Path.Combine(dir, j1))); // work copies go once their cuts are made
        // two calibration renders per model, then the cuts with the mark turned off
        var cutScripts = env.Film.Scripts.Where(s => s.Contains("\nrecord movie\n") && s.Contains("framespersec 15") && !s.Contains("quality 100")).ToList();
        Assert.Equal(4, cutScripts.Count);
        Assert.All(cutScripts, s => Assert.Contains("turn system ash_cal picture System off", s));
        Assert.Equal(4, env.Film.Scripts.Count(s => s.Contains("quality 100")));

        var page = await env.Http.GetStringAsync("/films/" + f.Id);
        Assert.Contains("이어 붙인 영상", page); Assert.Contains("<video", page); Assert.DoesNotContain("http-equiv=\"refresh\"", page);
        var file = await env.Http.GetAsync($"/films/{f.Id}/files/{Uri.EscapeDataString(f.Joined!)}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode); Assert.Equal("video/mp4", file.Content.Headers.ContentType!.MediaType);
        Assert.Contains(f.Id, await env.Http.GetStringAsync("/analyses/" + aid));
        Assert.Contains("이 버전의 영상", await env.Http.GetStringAsync("/jobs/" + j2));
    }

    [Fact]
    public async Task Hung_Cut_Fails_Alone()
    {
        using var env = new JobEnv();
        var j1 = await Done(env, "분석", "A안");
        var (_, aid) = Analysis(env);
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1", "멈춤", 4321), Hold("c2", "정상") });
        var f = await Film(env, aid, Body(new[] { "c1", "c2" }, new[] { j1 }));
        Assert.Equal(JobState.Done, f.State);
        Assert.Equal((1, 1), (f.Done, f.Failed));
        Assert.Contains("시간 제한", f.Items[0].Error);
        Assert.True(f.Items[1].Ok);
        Assert.Single(env.Film.Finishes.Single(x => x.Join).Mp4s); // the joined film has the cut that worked
        Assert.True(Directory.Exists(Path.Combine(env.Factory.Services.GetRequiredService<FilmStore>().FilmDir(f.Id), j1))); // kept to look into
    }

    [Fact]
    public async Task Cut_The_Camera_Script_Rejects_Fails_Alone()
    {
        using var env = new JobEnv();
        var j1 = await Done(env, "분석", "A안");
        var (_, aid) = Analysis(env);
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1"), Move("c2") });
        // saved by 0.19.0 before Validate wanted every Move part > 0
        var cuts = Path.Combine(env.Dir, "jobs", "analyses", aid, "cuts.json");
        File.WriteAllText(cuts, File.ReadAllText(cuts).Replace("\"holdASec\":120", "\"holdASec\":0"));
        var f = await Film(env, aid, Body(new[] { "c2", "c1" }, new[] { j1 }));
        Assert.Equal((1, 1), (f.Done, f.Failed));
        Assert.Contains("카메라 스크립트", f.Items.Single(i => i.CutId == "c2").Error);
        Assert.True(f.Items.Single(i => i.CutId == "c1").Ok);
    }

    [Fact]
    public async Task Running_Film_Shows_In_The_Queue_And_Can_Be_Cancelled()
    {
        using var env = new JobEnv();
        var j1 = await Done(env, "분석", "A안");
        var (_, aid) = Analysis(env);
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1"), Hold("c2", "출구", 9000) });
        await Until(() => File.Exists(Path.Combine(env.Store.JobDir(j1), "scenes.json")) ? "ok" : null);
        env.Film.BlockOn = "\ntime 9000\n"; // c2's AutoView run keeps going until it is cancelled
        var fs = env.Factory.Services.GetRequiredService<FilmStore>();
        async Task<string> Start(object body) => JsonDocument.Parse(await (await Post(env, aid, body)).Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        var id = await Start(Body(new[] { "c1", "c2" }, new[] { j1 }));
        await Until(() => fs.Get(id)!.Items[0].Ok ? "ok" : null);

        // the film holds the queue: the API and a queued job's page say so and count it ahead
        var q = JsonDocument.Parse(await env.Http.GetStringAsync("/api/jobs/queue")).RootElement;
        Assert.Equal(FilmWorker.FilmBusy, q.GetProperty("busy").GetString());
        Assert.Equal(1, q.GetProperty("ahead").GetInt32());
        Assert.Equal(JsonValueKind.Null, q.GetProperty("estimatedStart").ValueKind);
        var waiting = await Send(env, "분석", "B안");
        var page = await env.Http.GetStringAsync("/jobs/" + waiting);
        Assert.Contains("앞에 1건이 있습니다(영상 촬영 중", page);
        Assert.Equal(JobState.Queued, env.Store.Get(waiting)!.State);

        // a second film waits; cancelling it does not touch the running one
        var queued = await Start(Body(new[] { "c1" }, new[] { j1 }));
        Assert.Contains("대기 취소", await env.Http.GetStringAsync("/films/" + queued));
        HttpRequestMessage Cancel(string film, string? origin = Ok) { var m = new HttpRequestMessage(HttpMethod.Post, $"/films/{film}/cancel"); if (origin != null) m.Headers.Add("Origin", origin); return m; }
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.SendAsync(Cancel(queued, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.SendAsync(Cancel(queued, "http://evil.example"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.SendAsync(Cancel("20260101-000000-zzzz"))).StatusCode);
        Assert.True((await env.Http.SendAsync(Cancel(queued))).StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.OK);
        Assert.Equal(JobState.Cancelled, fs.Get(queued)!.State);
        Assert.False(fs.Get(id)!.Finished);

        Assert.Contains("촬영 취소", await env.Http.GetStringAsync("/films/" + id));
        await env.Http.SendAsync(Cancel(id));
        var f = await Until(() => fs.Get(id) is { Finished: true } x ? x : null, 30);
        Assert.Equal(JobState.Cancelled, f.State);
        Assert.True(f.Items[0].Ok);                       // made before the cancel: kept
        Assert.True(File.Exists(Path.Combine(fs.FilmDir(id), f.Items[0].Name)));
        Assert.Contains("취소", f.Items[1].Error);
        Assert.DoesNotContain(env.Film.Finishes, x => x.Join && x.Out.StartsWith(fs.FilmDir(id))); // no joined film after a cancel
        var work = Path.Combine(fs.FilmDir(id), j1);     // left to look into, without the run output and raw recordings
        Assert.True(Directory.Exists(work));
        Assert.False(Directory.Exists(Path.Combine(work, "runs")));
        Assert.Empty(Directory.GetFiles(work, "*.mp4"));
        Assert.Contains("취소", await env.Http.GetStringAsync("/films/" + id));

        // the queue moves on
        await Until(() => env.Store.Get(waiting) is { Finished: true } j ? j : null);
        Assert.Null(JsonDocument.Parse(await env.Http.GetStringAsync("/api/jobs/queue")).RootElement.GetProperty("busy").GetString());
    }

    [Fact]
    public async Task Hung_AutoView_Stops_The_Film_After_Two_Timeouts()
    {
        using var env = new JobEnv();
        var j1 = await Done(env, "분석", "A안");
        var j2 = await Done(env, "분석", "B안");
        var (_, aid) = Analysis(env);
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1"), Hold("c2", "출구") });
        env.Film.AlwaysTimeout = true;
        var before = env.Film.Scripts.Count;
        var f = await Film(env, aid, Body(new[] { "c1", "c2" }, new[] { j1, j2 }));
        Assert.Equal(2, env.Film.Scripts.Count - before); // the first version's calibration, then the second's: no cut waits its time limit
        Assert.Equal(JobState.Failed, f.State);
        Assert.Equal((0, 4), (f.Done, f.Failed));
        Assert.All(f.Items.Where(i => i.JobId == j1), i => Assert.Contains("화면 배율 보정 중 시간 제한", i.Error));
        Assert.All(f.Items.Where(i => i.JobId == j2), i => Assert.Equal(FilmWorker.Hung, i.Error));
        Assert.Contains("AutoView가 응답하지 않아 중단", await env.Http.GetStringAsync("/films/" + f.Id));
    }

    [Fact]
    public async Task Calibration_Fallback_Is_Shown_On_The_Film_Page()
    {
        using var env = new JobEnv();
        var j1 = await Done(env, "분석", "A안");
        var (_, aid) = Analysis(env);
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1") });
        env.Film.Mark = null; // nothing differs between the two calibration frames
        var f = await Film(env, aid, Body(new[] { "c1" }, new[] { j1 }));
        Assert.Equal((1, 0), (f.Done, f.Failed)); // filmed with the estimate
        Assert.Equal("A안 v1: " + FilmWorker.CalWarning, f.Warnings.Single());
        Assert.Contains(FilmWorker.CalWarning, await env.Http.GetStringAsync("/films/" + f.Id));
    }

    [Fact]
    public void AutoView_Run_Past_Its_Limit_Is_Killed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash-hang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "m.avw"));
        try
        {
            var hang = Path.Combine(dir, "hang.cmd");
            File.WriteAllText(hang, "@ping -n 60 127.0.0.1 >nul\r\n");
            var sw = Stopwatch.StartNew();
            var r = new AutoViewRenderer(hang, null, "ffmpeg").Render(dir, "m", "camera description\nfilename \"" + Path.Combine(dir, "cut01") + "\"\n", TimeSpan.FromSeconds(1.5), CancellationToken.None);
            Assert.False(r.Ok);
            Assert.Contains("시간 제한", r.Error);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), sw.Elapsed.ToString());
            Assert.Contains("filename", File.ReadAllText(Path.Combine(dir, "m.avw", "camera.des")));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    [Fact]
    public void Vehicle_Delta_Lowers_Decided_Counts_Not_Below_Min()
    {
        var input = new ProjectInput();
        input.Groups.Add(new AgvGroup("G1", "T", 1, 3, new List<string>(), "Nearest"));
        input.Groups.Add(new AgvGroup("G2", "T", 2, 5, new List<string>(), "Nearest"));
        var decided = new Dictionary<string, int> { ["G1"] = 2, ["G2"] = 4 };
        FilmRequest R(int delta, Dictionary<string, int>? same = null) => new("f", "a", new[] { "c" }, new[] { "j" }, 1280, 720, 15, "each", true, null, delta, same);
        var cut = Hold("c") with { VehicleDelta = -1 };
        Assert.Equal(new Dictionary<string, int> { ["G1"] = 2, ["G2"] = 4 }, FilmWorker.Counts(input, decided, R(0), Hold("c")));
        Assert.Equal(new Dictionary<string, int> { ["G1"] = 1, ["G2"] = 3 }, FilmWorker.Counts(input, decided, R(0), cut));
        Assert.Equal(new Dictionary<string, int> { ["G1"] = 1, ["G2"] = 2 }, FilmWorker.Counts(input, decided, R(-1), cut)); // -2 each, never under MinCount
        Assert.Equal(new Dictionary<string, int> { ["G1"] = 1, ["G2"] = 2 }, FilmWorker.Counts(input, decided, R(-20), cut));
        Assert.Equal(new Dictionary<string, int> { ["G1"] = 3, ["G2"] = 2 }, FilmWorker.Counts(input, decided, R(-1, new() { ["G1"] = 3 }), cut)); // same counts win where given
        Assert.Equal(3, FilmWorker.Counts(input, decided, R(0, new() { ["G1"] = 9 }), cut)["G1"]); // and stay within MaxCount
    }

    [Fact]
    public async Task Same_Seed_For_All_Versions()
    {
        using var env = new JobEnv();
        var j1 = await Done(env, "분석", "A안");
        var j2 = await Done(env, "분석", "B안");
        var (_, aid) = Analysis(env);
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1") });
        var dir = env.Factory.Services.GetRequiredService<FilmStore>().Dir;
        List<int> Seeds() { lock (env.Film.Runs) return env.Film.Runs.Where(r => r.ProjectDir.StartsWith(dir)).Select(r => r.Scenario.Seed).ToList(); }

        await Film(env, aid, Body(new[] { "c1" }, new[] { j2, j1 }));
        Assert.Equal(new[] { InputOf(env, j2).Settings.SeedBase, InputOf(env, j2).Settings.SeedBase }, Seeds()); // the first listed version's SeedBase for both
        var before = Seeds().Count;
        await Film(env, aid, Body(new[] { "c1" }, new[] { j1, j2 }, seed: 7));
        Assert.Equal(new[] { 7, 7 }, Seeds().Skip(before));
    }

    [Fact]
    public async Task Caption_Uses_Job_Conclusion_Numbers()
    {
        using var env = new JobEnv();
        var j1 = await Done(env, "분석", "A안");
        var (_, aid) = Analysis(env);
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1", "입구"), Hold("c2", "출구") with { VehicleDelta = -1 }, Hold("c3", "끔") with { Caption = false } });
        var materials = InputOf(env, j1).Materials.Count;
        await Film(env, aid, Body(new[] { "c1", "c2", "c3" }, new[] { j1 }, "each"));
        string Cap(string plan, string cut) => env.Film.Finishes.Last(x => !x.Join && Path.GetFileName(x.Out) == $"{plan}_v1_{cut}.mp4").Captions.Single();
        // the conclusion: 2 vehicles, the confirm run's 60 %, 45 s stop per material in its result.tsv (as the 결론 table)
        Assert.Equal($"분석 · A안 v1 · 입구\n02:00:00 · 권장 2대 · 가동률 60% · 결품 {45 * materials}초", Cap("A안", "입구"));
        // one vehicle less: the search's run at G1=1 (95 %, no stop), not this filming run
        Assert.Equal("분석 · A안 v1 · 출구\n02:00:00 · 1대 · 가동률 95% · 결품 0초", Cap("A안", "출구"));
        Assert.Equal("", Cap("A안", "끔"));
        Assert.Equal(2, env.Film.Runs.Count(r => r.RunsDir != null)); // the -1 cut needed its own model run

        // counts the search never ran: this filming run's own values (result.tsv: 50 %), said so
        var j2 = await Done(env, "분석", "B안");
        var sum = Path.Combine(env.Store.ProjectDir(j2), "runs", "summary.tsv");
        File.WriteAllLines(sum, File.ReadAllLines(sum).Where(l => !l.StartsWith("n1_s1\t")));
        await Film(env, aid, Body(new[] { "c2" }, new[] { j2 }, "each"));
        Assert.Equal($"분석 · B안 v1 · 출구\n02:00:00 · 1대 · 가동률 50% · 결품 {45 * materials}초 · 이 실행 1회 값", Cap("B안", "출구"));
    }

    [Fact]
    public async Task File_Names_Are_Safe()
    {
        using var env = new JobEnv();
        var j1 = await Done(env, "분석/1:*?", "A<안>");
        var (st, aid) = Analysis(env);
        var cuts = new[] { Hold("c1", "입구 \"1\"|..\\x"), Hold("c2", "입구 \"1\"|..\\x"), Hold("c3", new string('가', 60)) };
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, cuts);
        var f = FilmWorker.Plan(new FilmRequest("f1", aid, new[] { "c1", "c2", "c3" }, new[] { j1 }, 1280, 720, 15, "each+joined", true, null, 0, null), st.Get(aid)!,
            cuts, env.Store.Get, out var err)!;
        Assert.Null(err);
        var names = f.Items.Select(i => i.Name).Append(f.Joined!).ToList();
        Assert.All(names, n => { Assert.EndsWith(".mp4", n); Assert.Equal(-1, n.IndexOfAny(Path.GetInvalidFileNameChars())); Assert.DoesNotContain("..\\", n); });
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count()); // same cut names do not overwrite each other
        Assert.StartsWith("A_안__v1_입구", names[0]);

        var made = await Film(env, aid, Body(new[] { "c1", "c2" }, new[] { j1 }));
        Assert.Equal(JobState.Done, made.State);
        foreach (var bad in new[] { "film.json", "..%5Cfilm.json", "..%2F..%2Fanalyses.json", "x.mp4" })
            Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync($"/films/{made.Id}/files/{bad}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await env.Http.GetAsync($"/films/{made.Id}/files/{Uri.EscapeDataString(made.Items[0].Name)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync("/films/..%5C..%5Cx")).StatusCode);
    }

    [Fact]
    public async Task Export_Is_Checked_Before_It_Is_Queued()
    {
        using var env = new JobEnv();
        var j1 = await Done(env, "분석", "A안");
        var (st, aid) = Analysis(env);
        var other = st.CreateAnalysis("다른 분석", "");
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[] { Hold("c1") });
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(env, aid, Body(new[] { "c1" }, new[] { j1 }), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(env, "a-nope0000", Body(new[] { "c1" }, new[] { j1 }))).StatusCode);
        foreach (var (body, why) in new (object, string)[]
        {
            (Body(Array.Empty<string>(), new[] { j1 }), "컷을"), (Body(new[] { "zz" }, new[] { j1 }), "컷이 없습니다"),
            (Body(new[] { "c1" }, new[] { "20260101-000000-zzzz" }), "이 분석의 작업이"), (Body(new[] { "c1" }, new[] { j1 }, "mov"), "파일 형태"),
            (new { cutIds = new[] { "c1" }, jobIds = new[] { j1 }, width = 800, height = 600, fps = 15, output = "each" }, "해상도"),
            (Body(new[] { "c1" }, new[] { j1 }, delta: -50), "대수 변경"),
            (Body(Enumerable.Range(0, 61).Select(i => "c" + i), new[] { j1 }), "컷 × 버전 60개까지"),
        })
        {
            var r = await Post(env, aid, body);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains(why, await r.Content.ReadAsStringAsync());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(env, other.Id, Body(new[] { "c1" }, new[] { j1 }))).StatusCode); // no such cut there
        Assert.Empty(env.Factory.Services.GetRequiredService<FilmStore>().All());
    }

    [Fact]
    public async Task Scenes_Json_Written_After_Search()
    {
        using var env = new JobEnv();
        var j1 = await Done(env, "분석", "A안");
        var path = Path.Combine(env.Store.JobDir(j1), "scenes.json");
        var json = await Until(() => File.Exists(path) ? File.ReadAllText(path) : null);
        Assert.Contains("\"kind\":\"Stockout\"", json);
        var ev = env.Film.Runs.Single(r => r.Scenario.Events);
        Assert.Equal(env.Store.ProjectDir(j1), ev.ProjectDir);
        Assert.Equal(2, ev.Scenario.ActiveByGroup["G1"]); // the conclusion counts
        Assert.Equal(InputOf(env, j1).Settings.SeedBase, ev.Scenario.Seed);
        Assert.Equal(JobState.Done, env.Store.Get(j1)!.State);

        // "후보 찾기" with one vehicle less: the request is run between jobs, then removed
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/jobs/{j1}/scenes?delta=-1"); req.Headers.Add("Origin", Ok);
        Assert.Equal(HttpStatusCode.Accepted, (await env.Http.SendAsync(req)).StatusCode);
        env.Store.Wake();
        var d1 = Path.Combine(env.Store.JobDir(j1), "scenes.delta-1.json");
        await Until(() => File.Exists(d1) && !File.Exists(Path.Combine(env.Store.JobDir(j1), FilmWorker.ScenesRequest)) ? "ok" : null);
        Assert.Equal(1, env.Film.Runs.Last(r => r.Scenario.Events).Scenario.ActiveByGroup["G1"]);
        Assert.Contains("Stockout", await env.Http.GetStringAsync($"/api/jobs/{j1}/scenes?delta=-1"));

        // a request file that cannot be deleted runs once, not on every round of the worker
        var reqFile = Path.Combine(env.Store.JobDir(j1), FilmWorker.ScenesRequest);
        File.WriteAllText(reqFile, "{\"delta\":-2,\"requestedUtc\":\"2026-10-23T00:00:00Z\"}");
        File.SetAttributes(reqFile, FileAttributes.ReadOnly);
        try
        {
            int EventRuns() { lock (env.Film.Runs) return env.Film.Runs.Count(r => r.Scenario.Events && r.ProjectDir == env.Store.ProjectDir(j1)); }
            env.Store.Wake();
            await Until(() => File.Exists(Path.Combine(env.Store.JobDir(j1), "scenes.delta-2.json")) ? "ok" : null);
            var runs = EventRuns();
            await Done(env, "분석", "C안"); // the worker goes round more than once meanwhile
            Assert.Equal(runs, EventRuns());
            Assert.True(File.Exists(reqFile));
        }
        finally { File.SetAttributes(reqFile, FileAttributes.Normal); }

        // a model built before the event log: no events.tsv
        env.Film.WriteEvents = false;
        var j2 = await Done(env, "분석", "B안");
        var none = await Until(() => File.Exists(Path.Combine(env.Store.JobDir(j2), "scenes.json")) ? File.ReadAllText(Path.Combine(env.Store.JobDir(j2), "scenes.json")) : null);
        Assert.Equal(FilmWorker.NoEvents, JsonDocument.Parse(none).RootElement.GetProperty("none").GetString());
    }

    /// <summary>A wall box around the 10 m loop, a cross inside it and a long line out to the east (the drawing is bigger than the layout).</summary>
    static void Drawing(string proj)
    {
        var sb = new StringBuilder("0\nSECTION\n2\nENTITIES\n");
        void Line(double x1, double y1, double x2, double y2, string layer) =>
            sb.Append(System.FormattableString.Invariant($"0\nLINE\n8\n{layer}\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}\n"));
        Line(-3, -3, 14, -3, "WALL"); Line(14, -3, 14, 13, "WALL"); Line(14, 13, -3, 13, "WALL"); Line(-3, 13, -3, -3, "WALL");
        Line(3, 3, 7, 7, "MARK"); Line(3, 7, 7, 3, "MARK"); Line(14, 5, 30, 5, "WALL");
        File.WriteAllText(Path.Combine(proj, JobPackage.LocalDxf), sb.Append("0\nENDSEC\n0\nEOF\n").ToString());
        new ASH.Core.Dxf.ModelFrame("Meters", 0, 0, 1).Write(Path.Combine(proj, ASH.Core.Dxf.ModelFrame.FileName));
    }

    // the whole film path on this PC: search, scenes, then a plan-view Hold, a Move and a tilted perspective cut with the drawing (ASH_AM_HOME; ffmpeg on PATH).
    // ASH_FILM_OUT=dir copies the film folder there to look at.
    [SkippableFact]
    public async Task Real_AutoMod_Film_Hold_Move_And_Tilt()
    {
        var am = Environment.GetEnvironmentVariable("ASH_AM_HOME");
        Skip.If(am is null, "ASH_AM_HOME not set");
        using var env = new JobEnv(amHome: am);
        var form = new MultipartFormDataContent
        {
            { new StringContent("Line A"), "project" }, { new StringContent("m"), "model" }, { new StringContent("tester"), "submitter" },
            { new StringContent("필름 분석"), "analysisName" }, { new StringContent("A안"), "planName" },
            { new ByteArrayContent(await File.ReadAllBytesAsync(JobEnv.MakePackage(env.Dir, addFiles: Drawing))), "package", "package.zip" },
        };
        var j1 = JsonDocument.Parse(await (await env.Http.PostAsync("/api/jobs", form)).Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        var j = await env.WaitFinished(j1, 1800);
        Assert.True(j.State == JobState.Done, j.Error + "\n" + File.ReadAllText(env.Store.LogPath(j1)));
        Console.WriteLine("scenes.json: " + File.ReadAllText(Path.Combine(env.Store.JobDir(j1), "scenes.json")));

        var (_, aid) = Analysis(env);
        var warm = InputOf(env, j1).Settings.WarmupSec;
        env.Factory.Services.GetRequiredService<CutStore>().Save(aid, new[]
        {
            new CutSpec("hold", "평면 고정", CutKind.Hold, warm + 600, 300, new Rect(-1, -1, 12, 12), null, 0, 0, 0, 30, 0, 0, false, true),
            new CutSpec("move", "L001에서 U001로", CutKind.Move, warm + 1000, 0, new Rect(7, 2, 6, 6), new Rect(-1, 7, 6, 6), 60, 60, 60, 18, 0, 0, false, true),
            new CutSpec("tilt", "기울인 원근", CutKind.Hold, warm + 1400, 300, new Rect(-3, -3, 17, 16), null, 0, 0, 0, 30, 45, 30, true, true),
        });
        var f = await Film(env, aid, Body(new[] { "hold", "move", "tilt" }, new[] { j1 }));
        var dir = env.Factory.Services.GetRequiredService<FilmStore>().FilmDir(f.Id);
        var log = File.ReadAllText(Path.Combine(dir, "film.log"));
        Console.WriteLine(log);
        if (Environment.GetEnvironmentVariable("ASH_FILM_OUT") is { Length: > 0 } outDir)
            foreach (var file in Directory.GetFiles(dir)) { Directory.CreateDirectory(outDir); File.Copy(file, Path.Combine(outDir, Path.GetFileName(file)), true); }
        Assert.True((f.Done, f.Failed) == (3, 0), log + string.Join("\n", f.Items.Select(i => i.Label + ": " + i.Error)));
        Assert.True(f.JoinedOk, log);
        Assert.Contains("F0 보정", log);
    }

    [Fact]
    public async Task Model_Without_Event_Log_Gets_No_Scene_Run()
    {
        using var env = new JobEnv();
        // logic written before the event log (0.18): no V_events anywhere
        var id = await Send(env, "분석", "A안", proj => { var asy = Path.Combine(proj, "m.arc", "m~.asy"); File.WriteAllText(asy, File.ReadAllText(asy).Replace("V_events", "V_old")); });
        await Until(() => env.Store.Get(id) is { Finished: true } j ? j : null);
        var json = File.ReadAllText(Path.Combine(env.Store.JobDir(id), "scenes.json"));
        Assert.Equal(FilmWorker.NoEvents, JsonDocument.Parse(json).RootElement.GetProperty("none").GetString());
        Assert.DoesNotContain(env.Film.Runs, r => r.Scenario.Events && r.ProjectDir == env.Store.ProjectDir(id)); // no wasted run
    }

    [Fact]
    public async Task Local_Result_Gets_No_Auto_Scene_Run()
    {
        using var env = new JobEnv();
        var res = await env.Send(JobLocalAndRetentionTests.LocalResultPackage(env.Dir));
        var local = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;
        await Done(env, "분석", "A안"); // the worker has gone round at least once since
        Assert.False(File.Exists(Path.Combine(env.Store.JobDir(local), "scenes.json")));
        Assert.DoesNotContain(env.Film.Runs, r => r.ProjectDir == env.Store.ProjectDir(local));
    }
}
