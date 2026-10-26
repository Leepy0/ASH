using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ASH.Core;
using ASH.Core.Analysis;
using ASH.Core.AutoMod;
using ASH.Core.Dxf;
using ASH.Core.Film;
using ASH.Core.Input;
using ASH.Core.Model;
using ASH.Core.Runner;

namespace ASH.Server;

/// <summary>The AutoMod steps of filming and scene finding (build a model folder, run it once); tests plug in a fake.</summary>
public interface IFilmAutoMod
{
    AmBuildResult Build(string projectDir, string model);
    RunOutcome Run(RunRequest r);
}

/// <summary>Jobs:AmHome, Jobs:BuildTimeoutMin, as the job runner uses them.</summary>
public sealed class AutoModFilm(IConfiguration cfg) : IFilmAutoMod
{
    public AmBuildResult Build(string projectDir, string model)
    {
        var jr = new AutoModJobRunner(cfg);
        if (AmBuilder.FindBuildExe(jr.AmHome) == null) return new AmBuildResult(false, "서버에 AutoMod가 없습니다: " + AmBuilder.BuildExeCandidates(jr.AmHome), "", Array.Empty<string>());
        return AmBuilder.Build(jr.AmHome, projectDir, model, jr.BuildTimeout);
    }

    public RunOutcome Run(RunRequest r) => ModelRunner.Run(r);
}

/// <summary>
/// Filming and scene finding, run by JobWorker between jobs on its own thread, so AutoMod never runs twice at once.
/// A film: per version (and per distinct vehicle counts) a copy of the project in films\&lt;id&gt;\&lt;jobId&gt;\ (the job's folder is never touched) gets the drawing,
/// the ash_cal calibration mark and a recording window, is built and run once, F0 is measured (guide 10.4), and every cut is one AutoView run plus an ffmpeg finish.
/// A cut that fails (or hangs past Film:CutTimeoutMin) fails alone. Scenes: one run with the event log, SceneFinder, &lt;job&gt;\scenes.json or scenes.delta&lt;d&gt;.json.
/// </summary>
public sealed class FilmWorker(JobStore jobs, AnalysisStore analyses, FilmStore films, IFilmAutoMod am, IFilmRenderer renderer, IConfiguration cfg, ILogger<FilmWorker> logger)
{
    public const string ScenesRequest = "scenes-request.json";
    public const string CalSystem = "ash_cal";
    public const string NoEvents = "새 버전으로 다시 보내면 생깁니다";
    const string CalOff = "turn system " + CalSystem + " picture System off";
    const int Quality = 80;
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    public static string ScenesFile(int delta) => delta == 0 ? "scenes.json" : $"scenes.delta{delta.ToString(C)}.json";

    TimeSpan CutTimeout => TimeSpan.FromMinutes(Math.Max(0.01, cfg.GetValue("Film:CutTimeoutMin", 20.0)));
    TimeSpan CalTimeout => TimeSpan.FromMinutes(Math.Max(0.01, cfg.GetValue("Film:CalibrationTimeoutMin", 3.0)));
    AutoModJobRunner Jr => new(cfg);
    /// <summary>The AutoView in use is older than 14 (AVI, no upvector): read per run, like the renderer's settings.</summary>
    bool Legacy => AutoViewExe.IsLegacy(LiveFilmRenderer.ResolveAviewExe(cfg));

    public const string FilmBusy = "영상 촬영 중", ScenesBusy = "장면 후보 찾기 중", PreviewBusy = "컷 미리보기 중";
    /// <summary>Preview frame size: big enough to judge the framing (and the roll), quick to render.</summary>
    public const int PreviewWidth = 1280, PreviewHeight = 720;

    /// <summary>Cut previews, made here between jobs like films.</summary>
    public PreviewStore Previews { get; } = new(jobs.Dir, logger);
    /// <summary>Most cut × version items in one film: a film holds the job queue until it is done.</summary>
    public const int MaxItems = 60;

    /// <summary>What holds the job queue now (<see cref="FilmBusy"/>, <see cref="ScenesBusy"/>), null when no film work runs.</summary>
    public string? Busy => _busy;
    volatile string? _busy;

    readonly object _gate = new();
    string? _filmId;
    CancellationTokenSource? _filmCts;

    /// <summary>Cancels the film if it is the one running now; false otherwise.</summary>
    public bool CancelRunning(string id)
    {
        lock (_gate)
        {
            if (_filmId != id || _filmCts == null) return false;
            _filmCts.Cancel();
            return true;
        }
    }

    /// <summary>One piece of work between jobs: a waiting scene request, else the oldest queued film. False when there was nothing to do.</summary>
    public bool RunNext(CancellationToken stop)
    {
        foreach (var j in jobs.All())
        {
            var req = Path.Combine(jobs.JobDir(j.Id), ScenesRequest);
            if (!File.Exists(req)) continue;
            string text;
            try { text = File.ReadAllText(req); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            // a request file that could not be deleted must not run the scene search again and again
            if (_scenesDone.TryGetValue(j.Id, out var done) && done == text) { TryDelete(req); continue; }
            _scenesDone[j.Id] = text;
            _busy = ScenesBusy;
            try { SceneRequest(j, req, text); } finally { _busy = null; }
            return true;
        }
        if (Previews.TakeNext() is { } p)
        {
            _busy = PreviewBusy;
            try { RunPreview(p, stop); } finally { _busy = null; }
            return true;
        }
        var f = films.TakeNext();
        if (f == null) return false;
        _busy = FilmBusy;
        try { RunFilm(f, stop); } finally { _busy = null; }
        return true;
    }

    /// <summary>Job id -> the scene request text already run (only touched on the worker thread).</summary>
    readonly Dictionary<string, string> _scenesDone = new(StringComparer.Ordinal);

    static void TryDelete(string path) { try { File.Delete(path); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

    void SceneRequest(JobInfo j, string req, string text)
    {
        int delta;
        try { delta = JsonDocument.Parse(text).RootElement.GetProperty("delta").GetInt32(); }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            logger.LogWarning("장면 요청을 읽지 못해 지웁니다 {Id}: {Error}", j.Id, e.Message);
            TryDelete(req);
            return;
        }
        using (var log = new StreamWriter(jobs.LogPath(j.Id), true, new UTF8Encoding(false)) { AutoFlush = true })
        {
            log.WriteLine($"[{SeoulTime.NowText}] 장면 후보 찾기 (결론 대수 {delta:+0;-0;+0}대)");
            FindScenes(j, j.Decided, delta, log);
        }
        // latest wins: a request that came in meanwhile (another delta) stays for the next round
        try { if (File.ReadAllText(req) == text) TryDelete(req); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>One run with the event log on (counts = decided + delta within each group's range, the input's SeedBase) and the scene file for that delta.
    /// Never throws: a failure is written as {"error": ...}; a model built before the event log gives {"none": ...}.</summary>
    public void FindScenes(JobInfo j, IReadOnlyDictionary<string, int>? decided, int delta, TextWriter log)
    {
        object result;
        if (!HasEventLog(jobs.ProjectDir(j.Id), j.Model))
        {
            log.WriteLine("scenes: 이벤트 로그가 없는 예전 모델이라 실행하지 않습니다.");
            result = new { none = NoEvents };
        }
        else try
        {
            if (decided == null) throw new InvalidOperationException("결론 대수가 없습니다.");
            var proj = jobs.ProjectDir(j.Id);
            var input = SearchJob.ReadInput(proj, TextWriter.Null, new List<string>(), out var err) ?? throw new InvalidOperationException(err);
            var counts = input.Groups.ToDictionary(g => g.Group, g => Math.Clamp((decided.TryGetValue(g.Group, out var n) ? n : g.MinCount) + delta, g.MinCount, g.MaxCount));
            if (!File.Exists(Path.Combine(proj, j.Model + ".exe")))
            {
                // a result uploaded from a PC, or run folders already purged: build once in the job's folder
                ModelBuilder.PrepareBuildDir(proj, j.Model);
                var b = am.Build(proj, j.Model);
                if (!b.Success) throw new InvalidOperationException("빌드 실패: " + FirstLine(b.MakeErrText + "\n" + b.Am2ErrText));
            }
            var o = am.Run(new RunRequest(proj, j.Model, input, new RunScenario(input.Settings.SeedBase, counts, Events: true), Jr.RunTimeout, Jr.DialogButton, AmHome: Jr.AmHome));
            log.WriteLine($"scenes: run {o.RunId} {(o.Success ? "ok" : "FAILED " + o.Error)}");
            if (!o.Success) throw new InvalidOperationException("모델 실행 실패: " + o.Error);
            var events = EventLog.Read(Path.Combine(o.RunDir, "events.tsv"));
            if (events.Count == 0)
            {
                log.WriteLine("scenes: events.tsv가 없거나 비어 있습니다(이벤트 로그가 없는 예전 모델).");
                result = new { none = NoEvents };
            }
            else
            {
                var layout = ArcLayout.Load(Path.Combine(proj, j.Model + ".arc")).Layout;
                var found = SceneFinder.Find(events, layout, UnloadCps(input), input.Settings.SimLengthSec, input.Settings.WarmupSec);
                log.WriteLine($"scenes: {found.Count}개");
                result = found;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.WriteLine("scenes: " + e.Message);
            logger.LogWarning("장면 후보 실패 {Id}: {Error}", j.Id, e.Message);
            result = new { error = "장면 후보를 찾지 못했습니다: " + e.Message };
        }
        try
        {
            var path = Path.Combine(jobs.JobDir(j.Id), ScenesFile(delta));
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(result, CutStore.Json));
            File.Move(path + ".tmp", path, true);
        }
        catch (IOException e) { logger.LogWarning("장면 파일 저장 실패 {Id}: {Error}", j.Id, e.Message); }
    }

    /// <summary>False for logic from before the event log (0.18): a run would write no events.tsv. An unreadable file lets the run tell.</summary>
    static bool HasEventLog(string proj, string model)
    {
        try { return File.ReadAllText(Path.Combine(proj, model + ".arc", model + "~.asy")).Contains("V_events"); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; }
    }

    /// <summary>Material -> its unload CP: the route's SupplyToCP, else the station's CP.</summary>
    static Dictionary<string, string> UnloadCps(ProjectInput input) => input.Materials.GroupBy(m => m.Material).ToDictionary(g => g.Key, g =>
        input.Routes.FirstOrDefault(r => r.Material == g.Key)?.SupplyToCP ?? input.Stations.FirstOrDefault(s => s.Station == g.First().Station)?.CPName ?? "");

    static string FirstLine(string s) => s.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "(오류 출력 없음)";

    // ---------------- films ----------------

    /// <summary>The film as stored: items cut by cut, each cut for every version, with unique safe file names; null with the reason when the request is unusable.</summary>
    public static FilmInfo? Plan(FilmRequest r, AnalysisInfo a, IReadOnlyList<CutSpec> cuts, Func<string, JobInfo?> job, out string? error)
    {
        error = null;
        if (r.CutIds.Count * r.JobIds.Count > MaxItems) { error = $"한 번에 찍는 영상은 컷 × 버전 {MaxItems}개까지입니다(지금 {r.CutIds.Count * r.JobIds.Count}개). 나누어 내보내 주세요."; return null; }
        var byId = cuts.ToDictionary(c => c.Id);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string Unique(string stem) { var n = stem + ".mp4"; for (int i = 2; !used.Add(n); i++) n = $"{stem}_{i}.mp4"; return n; }
        var f = new FilmInfo { Request = r, AnalysisName = a.Name, CreatedUtc = DateTime.UtcNow };
        foreach (var cid in r.CutIds)
        {
            if (!byId.TryGetValue(cid, out var cut)) { error = "컷이 없습니다: " + cid; return null; }
            if (!f.Cuts.Contains(cut)) f.Cuts.Add(cut);
            foreach (var jid in r.JobIds)
            {
                var j = job(jid);
                if (j == null || j.AnalysisId != a.Id) { error = "이 분석의 작업이 아닙니다: " + jid; return null; }
                if (j.State != JobState.Done || j.Decided == null) { error = $"결론 대수가 나온 완료 작업만 찍을 수 있습니다: {jid}"; return null; }
                var plan = a.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name ?? "안";
                f.Items.Add(new FilmItem { JobId = jid, CutId = cid, Label = $"{plan} v{j.Version} · {cut.Name}", Name = Unique(AnalysisNames.SafeFileName($"{plan}_v{j.Version}_{cut.Name}")) });
            }
        }
        if (f.Join) f.Joined = Unique(AnalysisNames.SafeFileName($"{a.Name}_{SeoulTime.Now.ToString("yyyyMMdd", C)}"));
        return f;
    }

    /// <summary>Counts for one cut of one version: SameCounts per group, else the conclusion + export delta + the cut's delta; always within MinCount..MaxCount.</summary>
    public static Dictionary<string, int> Counts(ProjectInput input, IReadOnlyDictionary<string, int> decided, FilmRequest r, CutSpec cut) =>
        input.Groups.ToDictionary(g => g.Group, g => Math.Clamp(r.SameCounts != null && r.SameCounts.TryGetValue(g.Group, out var same) ? same
            : (decided.TryGetValue(g.Group, out var d) ? d : g.MinCount) + r.VehicleDelta + cut.VehicleDelta, g.MinCount, g.MaxCount), StringComparer.Ordinal);

    public const string Hung = "AutoView가 응답하지 않아 중단했습니다(연속 2회 시간 초과).";
    public const string CalWarning = "주의: 화면 배율 보정 실패, 추정값 사용";

    /// <summary>Thrown after two AutoView runs in a row hit the time limit: a hung AutoView (session 0, a dialog nobody clicks) would otherwise block the queue for hours.</summary>
    sealed class AutoViewHungException() : Exception(Hung);

    /// <summary>Render runs in a row that hit the time limit (worker thread only).</summary>
    int _timeouts;

    RenderResult Render(string work, string model, string script, TimeSpan timeout, CancellationToken stop)
    {
        var res = renderer.Render(work, model, script, timeout, stop);
        _timeouts = res.TimedOut ? _timeouts + 1 : 0;
        return res;
    }

    void RunFilm(FilmInfo f, CancellationToken stop)
    {
        _timeouts = 0;
        var dir = films.FilmDir(f.Id);
        Directory.CreateDirectory(dir);
        using var log = new StreamWriter(Path.Combine(dir, "film.log"), true, new UTF8Encoding(false)) { AutoFlush = true };
        void Say(string s) => log.WriteLine($"[{SeoulTime.NowText}] {s}");
        string? error = null;
        var cancelled = false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stop);
        lock (_gate) { _filmId = f.Id; _filmCts = cts; }
        if (films.Get(f.Id)?.CancelRequested == true) cts.Cancel();
        try { Film(f, dir, Say, cts.Token); }
        catch (OperationCanceledException) { cancelled = !stop.IsCancellationRequested; error = cancelled ? "취소되었습니다." : "서버가 멈추어 중단되었습니다."; }
        catch (Exception e) { error = e.Message; Say(e.ToString()); logger.LogError(e, "영상 {Id} 실패", f.Id); }
        finally { lock (_gate) { _filmId = null; _filmCts = null; } }
        Say("끝: " + (error ?? "정상"));
        films.Update(f.Id, x =>
        {
            x.FinishedUtc = DateTime.UtcNow; x.Phase = null; x.Error ??= error;
            x.State = cancelled ? JobState.Cancelled : x.Items.Any(i => i.Ok) ? JobState.Done : JobState.Failed;
            foreach (var i in x.Items.Where(i => !i.Ok && i.Error == null)) i.Error = error ?? "만들지 못했습니다.";
            x.Done = x.Items.Count(i => i.Ok); x.Failed = x.Items.Count(i => !i.Ok);
        });
    }

    void Film(FilmInfo f, string dir, Action<string> say, CancellationToken stop)
    {
        var r = f.Request;
        say($"시작: 컷 {f.Cuts.Count}개 × 버전 {r.JobIds.Count}개, {r.Width}×{r.Height} {r.Fps} fps, {r.Output}");
        say("AutoView: " + (LiveFilmRenderer.ResolveAviewExe(cfg) is { } exe ? AutoViewDiagnostics.Program(exe) : "찾지 못함 " + AutoViewExe.Candidates(LiveFilmRenderer.AmHome(cfg))));
        int? seed = r.Seed;
        try { Versions(); }
        catch (AutoViewHungException)
        {
            say(Hung);
            films.Update(f.Id, x => { foreach (var i in x.Items.Where(i => !i.Ok && i.Error == null)) i.Error = Hung; x.Failed = x.Items.Count(i => i.Error != null); x.Error = Hung; });
        }
        void Versions()
        {
            foreach (var jid in r.JobIds)
            {
                stop.ThrowIfCancellationRequested();
                var j = jobs.Get(jid);
                void FailAll(IEnumerable<CutSpec> cuts, string why)
                {
                    say($"{jid}: {why}");
                    var ids = cuts.Select(c => c.Id).ToHashSet();
                    films.Update(f.Id, x => { foreach (var i in x.Items.Where(i => i.JobId == jid && ids.Contains(i.CutId) && !i.Ok)) i.Error = why; x.Failed = x.Items.Count(i => i.Error != null); });
                }
                if (j?.Decided == null) { FailAll(f.Cuts, "작업이 없거나 결론 대수가 없습니다."); continue; }
                var input = SearchJob.ReadInput(jobs.ProjectDir(jid), TextWriter.Null, new List<string>(), out var err);
                if (input == null) { FailAll(f.Cuts, "입력을 읽지 못했습니다: " + err); continue; }
                seed ??= input.Settings.SeedBase; // R8: the first version's seed for every version
                var groups = f.Cuts.GroupBy(c => string.Join(";", Counts(input, j.Decided, r, c).Select(kv => kv.Key + "=" + kv.Value))).ToList();
                for (int k = 0; k < groups.Count; k++)
                {
                    var cuts = groups[k].ToList();
                    try { Version(f, j, input, Counts(input, j.Decided, r, cuts[0]), seed.Value, cuts, Path.Combine(dir, groups.Count == 1 ? jid : $"{jid}-{k + 1}"), say, stop); }
                    catch (Exception e) when (e is not OperationCanceledException and not AutoViewHungException) { FailAll(cuts, e.Message); }
                }
            }
        }
        stop.ThrowIfCancellationRequested(); // the last cut's render may have been the one cancelled: no joined film then
        if (f.Join)
        {
            var g = films.Get(f.Id)!;
            var parts = g.Items.Where(i => i.Ok).Select(i => Path.Combine(dir, i.Name)).ToList();
            if (parts.Count > 0)
            {
                films.Update(f.Id, x => x.Phase = "이어 붙이기");
                try { renderer.Finish(parts, Array.Empty<string>(), Path.Combine(dir, g.Joined!), true, stop); films.Update(f.Id, x => x.JoinedOk = true); say("이어 붙임: " + g.Joined); }
                catch (Exception e) when (e is InvalidOperationException or IOException) { say("이어 붙이기 실패: " + e.Message); films.Update(f.Id, x => x.Error = "이어 붙이지 못했습니다: " + e.Message); }
            }
            if (!f.KeepEach && films.Get(f.Id)!.JoinedOk) foreach (var p in parts) File.Delete(p);
        }
    }

    /// <summary>One film model (a version at one set of counts): copy, drawing, calibration mark, recording window, build, run, F0, then each cut.
    /// The work copy goes when every cut was made; otherwise (a failed cut, build or run, a hang, a cancel) only what explains it stays.</summary>
    void Version(FilmInfo f, JobInfo j, ProjectInput input, Dictionary<string, int> counts, int seed, List<CutSpec> cuts, string work, Action<string> say, CancellationToken stop)
    {
        var clean = false;
        try { clean = MakeVersion(f, j, input, counts, seed, cuts, work, say, stop); }
        finally
        {
            try
            {
                if (clean) { if (Directory.Exists(work)) Directory.Delete(work, true); }
                else Prune(work, j.Model);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { say($"작업 폴더를 정리하지 못함: {e.Message}"); }
        }
    }

    /// <summary>A failed version's work copy without the bulky parts (runs, the build, the recording, frames, raw MP4s); the model source,
    /// the last camera.des and AutoView's aviewerr/aviewout.dat stay to look into.</summary>
    internal static void Prune(string work, string model)
    {
        if (!Directory.Exists(work)) return;
        var avw = Path.Combine(work, model + ".avw");
        if (File.Exists(Path.Combine(avw, "camera.des"))) File.Copy(Path.Combine(avw, "camera.des"), Path.Combine(work, "camera.des"), true);
        foreach (var d in new[] { "runs", model + ".dir", model + ".avw" }.Select(n => Path.Combine(work, n)).Concat(Directory.GetDirectories(work, "*_frames")))
            if (Directory.Exists(d)) Directory.Delete(d, true);
        foreach (var mp4 in Directory.GetFiles(work, "*.mp4").Concat(Directory.GetFiles(work, "*.avi"))) File.Delete(mp4);
    }

    bool MakeVersion(FilmInfo f, JobInfo j, ProjectInput input, Dictionary<string, int> counts, int seed, List<CutSpec> cuts, string work, Action<string> say, CancellationToken stop)
    {
        var r = f.Request;
        var a = analyses.Get(r.AnalysisId);
        var label = $"{a?.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name ?? j.Plan} v{j.Version}";
        void Phase(string p) { films.Update(f.Id, x => x.Phase = $"{label}: {p}"); say($"{label}: {p}"); }
        double simLen = input.Settings.SimLengthSec;
        var ok = new List<CutSpec>();
        foreach (var c in cuts)
            if (c.StartSec + c.SimSeconds > simLen) SetItem(f, j.Id, c.Id, false, $"시뮬레이션 길이({simLen:0}초)를 넘는 컷입니다.");
            else ok.Add(c);
        if (ok.Count == 0) return true;

        double from = Math.Max(0, Math.Floor(ok.Min(c => c.StartSec)) - 2), to = Math.Min(simLen, Math.Ceiling(ok.Max(c => c.StartSec + c.SimSeconds)) + 2);
        var (f0, o) = PrepareModel(j, input, counts, seed, from, to, work, r.Width, r.Height, Phase, s => say($"{label}: {s}"), w => films.Update(f.Id, x => x.Warnings.Add($"{label}: {w}")), stop);
        var kpi = Kpi(j, input, counts, o);
        say($"{label}: 자막 수치 {kpi}");
        var failed = false;
        for (int k = 0; k < ok.Count; k++)
        {
            stop.ThrowIfCancellationRequested();
            var c = ok[k];
            var item = films.Get(f.Id)!.Items.First(i => i.JobId == j.Id && i.CutId == c.Id);
            Phase(c.Name);
            string script;
            try { script = CameraScript.Build(c, f0, r.Width, r.Height, r.Fps, Quality, Path.Combine(work, $"cut{k + 1:00}"), new[] { CalOff }, Legacy); }
            catch (ArgumentException e) { failed = true; SetItem(f, j.Id, c.Id, false, "카메라 스크립트를 만들 수 없는 컷입니다: " + e.Message); continue; } // a cut saved before validation tightened
            var res = Render(work, j.Model, script, CutTimeout, stop);
            if (res.Dialogs.Count > 0) say($"{label}: 대화상자 {string.Join(", ", res.Dialogs)}");
            if (!res.Ok)
            {
                failed = true; SetItem(f, j.Id, c.Id, false, res.Error);
                say($"{label}: {c.Name} 실패: {res.Error}");
                if (_timeouts >= 2) { say($"{label}: 작업 폴더를 남깁니다: {work}"); throw new AutoViewHungException(); }
                continue;
            }
            if (res.Mp4!.EndsWith(".avi", StringComparison.OrdinalIgnoreCase)) say($"{label}: AutoView가 AVI를 만들었습니다(이전 AutoView 방식, 확인되지 않은 경로): ffmpeg로 MP4로 바꿉니다.");
            var caption = r.Caption && c.Caption ? $"{a?.Name ?? f.AnalysisName} · {label} · {c.Name}\n{Hms(c.StartSec)} · {kpi}" : "";
            try { renderer.Finish(new[] { res.Mp4! }, new[] { caption }, Path.Combine(films.FilmDir(f.Id), item.Name), false, stop); SetItem(f, j.Id, c.Id, true, null); say($"{label}: {item.Name}"); Thumb(f, item.Name, say, stop); }
            catch (Exception e) when (e is InvalidOperationException or IOException) { failed = true; SetItem(f, j.Id, c.Id, false, e.Message); }
        }
        if (failed) say($"{label}: 실패한 컷이 있어 작업 폴더를 남깁니다(기록·영상 원본은 지움): {work}");
        return !failed;
    }

    /// <summary>A film model in <paramref name="work"/> (a copy of the job's project): the drawing, the ash_cal calibration mark, the recording window
    /// <paramref name="from"/>..<paramref name="to"/>, build, one run at <paramref name="counts"/>, then F0. Throws when the build or the run fails.</summary>
    (double F0, RunOutcome Run) PrepareModel(JobInfo j, ProjectInput input, Dictionary<string, int> counts, int seed, double from, double to, string work, int width, int height,
        Action<string> Phase, Action<string> say, Action<string> warn, CancellationToken stop)
    {
        double simLen = input.Settings.SimLengthSec;
        Phase($"모델 준비 (대수 {string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value))}, 시드 {seed})");
        var proj = jobs.ProjectDir(j.Id);
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Copy(proj, work, j.Model);
        var arc = Path.Combine(work, j.Model + ".arc");
        var amoPath = Path.Combine(arc, "model.amo");
        var units = Regex.Match(File.ReadAllText(amoPath), @"^UNITS (\S+)", RegexOptions.Multiline) is { Success: true } um ? um.Groups[1].Value : "Meters";
        var layout = ArcLayout.Load(arc).Layout;
        var pts = layout.Paths.SelectMany(p => p.Segments).SelectMany(s => new[] { s.Start, s.End }).Concat(layout.Cps.Select(c => c.Position))
            .Select(p => (p.X, p.Y)).ToList();
        var dxf = Path.Combine(work, "drawing.dxf"); var frame = Path.Combine(work, ModelFrame.FileName);
        if (File.Exists(dxf) && File.Exists(frame))
            try
            {
                DxfDrawing d;
                using (var rd = new StreamReader(dxf, Encoding.UTF8)) d = DxfLite.ToModel(DxfLite.Read(rd), ModelFrame.Read(frame));
                // AGV_3D* plan symbols and wall/fence lines are drawn in 3D by the model's ash_3d system; their 2D lines would only clutter the floor
                var segs = d.Segs.Where(s => new[] { s.X1, s.Y1, s.X2, s.Y2 }.All(double.IsFinite) && !s.Layer.StartsWith("AGV_3D", StringComparison.OrdinalIgnoreCase)).ToList();
                StaticDrawing.AddTo(arc, d with { Segs = segs }, units, null);
                pts.AddRange(segs.SelectMany(s => new[] { (s.X1, s.Y1), (s.X2, s.Y2) }));
                say($"도면 선 {segs.Count}개");
            }
            catch (Exception e) when (e is IOException or FormatException or InvalidDataException or ArgumentException) { say($"경고: 도면 없이 찍습니다({e.Message})"); }
        else say($"drawing.dxf가 없어 도면 없이 찍습니다(예전 작업).");
        if (pts.Count == 0) pts.Add((0, 0));
        double x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), y0 = pts.Min(p => p.Y), y1 = pts.Max(p => p.Y);
        // calibration mark (guide 10.4): a rectangle half the scene's size at its centre, inside the scene so it does not move F0
        double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, lw = Math.Max((x1 - x0) / 2, 1), lh = Math.Max((y1 - y0) / 2, 1);
        var mark = new[] { (-1, -1, 1, -1), (1, -1, 1, 1), (1, 1, -1, 1), (-1, 1, -1, -1) }
            .Select(q => new DxfSeg(cx + q.Item1 * lw / 2, cy + q.Item2 * lh / 2, cx + q.Item3 * lw / 2, cy + q.Item4 * lh / 2, "cal")).ToList();
        StaticDrawing.AddTo(arc, new DxfDrawing(mark, Array.Empty<DxfText>(), new[] { "cal" }), units, null, CalSystem);
        File.WriteAllText(amoPath, ModelAmoWriter.WithRecording(File.ReadAllText(amoPath), simLen, from, to), new UTF8Encoding(false));

        Phase("빌드");
        ModelBuilder.PrepareBuildDir(work, j.Model);
        var b = am.Build(work, j.Model);
        if (!b.Success) throw new InvalidOperationException("빌드 실패: " + FirstLine(b.MakeErrText + "\n" + b.Am2ErrText));
        stop.ThrowIfCancellationRequested();
        Phase("모델 실행(기록)");
        var o = am.Run(new RunRequest(work, j.Model, input, new RunScenario(seed, counts), Jr.RunTimeout, Jr.DialogButton, RunsDir: Path.Combine(work, "runs"), AmHome: Jr.AmHome));
        if (!o.Success) throw new InvalidOperationException("모델 실행 실패: " + o.Error);
        stop.ThrowIfCancellationRequested();

        Phase("화면 배율 보정");
        var f0 = Calibrate(work, j.Model, (cx, cy, lw, lh, Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0) + 2.5 * 2.5)), from + 1, width, height, say, warn, stop);
        return (f0, o);
    }

    // ---------------- previews ----------------

    void RunPreview(PreviewInfo p, CancellationToken stop)
    {
        string? error = null;
        try { MakePreview(p, stop); }
        catch (OperationCanceledException) { error = "서버가 멈추어 중단되었습니다."; }
        catch (AutoViewHungException) { error = Hung; }
        catch (Exception e) { error = e.Message; logger.LogWarning("컷 미리보기 실패 {Analysis}/{Cut}: {Error}", p.AnalysisId, p.Cut.Id, e.Message); }
        Previews.Update(p.AnalysisId, p.Cut.Id, x => { x.State = error == null ? JobState.Done : JobState.Failed; x.Error = error; x.Phase = null; x.FinishedUtc = DateTime.UtcNow; });
    }

    /// <summary>One frame of the cut at <see cref="PreviewInfo.AtSec"/> with its camera (a Move cut: its A view), on a film model of the version at the cut's counts
    /// that records only a few seconds around that time. The model (and its F0) stays in previews\work and is reused while the version, counts and window are the same,
    /// so trying other camera angles at the same time only costs the AutoView run.</summary>
    void MakePreview(PreviewInfo p, CancellationToken stop)
    {
        _timeouts = 0;
        void Phase(string s) => Previews.Update(p.AnalysisId, p.Cut.Id, x => x.Phase = s);
        var j = jobs.Get(p.JobId) ?? throw new InvalidOperationException("작업이 없습니다: " + p.JobId);
        if (j.Decided == null) throw new InvalidOperationException("결론 대수가 없는 작업입니다.");
        var input = SearchJob.ReadInput(jobs.ProjectDir(j.Id), TextWriter.Null, new List<string>(), out var err) ?? throw new InvalidOperationException("입력을 읽지 못했습니다: " + err);
        double simLen = input.Settings.SimLengthSec, t = p.AtSec;
        if (t + 1 > simLen) throw new InvalidOperationException($"시뮬레이션 길이({simLen:0}초)를 넘는 시각입니다.");
        var counts = Counts(input, j.Decided, new FilmRequest("", p.AnalysisId, Array.Empty<string>(), Array.Empty<string>(), PreviewWidth, PreviewHeight, 15, "each", false, null, 0, null), p.Cut);
        var seed = input.Settings.SeedBase;
        double from = Math.Max(0, Math.Floor(t) - 2), to = Math.Min(simLen, Math.Ceiling(t) + 3);
        var work = Path.Combine(Previews.Dir(p.AnalysisId), "work");
        var keyFile = Path.Combine(work, "preview.key");
        var key = string.Join("|", j.Id, string.Join(";", counts.Select(kv => kv.Key + "=" + kv.Value)), seed, from.ToString(C), to.ToString(C));
        double f0;
        var kept = File.Exists(keyFile) ? File.ReadAllLines(keyFile) : Array.Empty<string>();
        if (kept.Length == 2 && kept[0] == key && double.TryParse(kept[1], NumberStyles.Float, C, out var k0)) f0 = k0;
        else
        {
            if (File.Exists(keyFile)) File.Delete(keyFile);
            f0 = PrepareModel(j, input, counts, seed, from, to, work, PreviewWidth, PreviewHeight, Phase, s => logger.LogInformation("미리보기 {Cut}: {Text}", p.Cut.Id, s), _ => { }, stop).F0;
            File.WriteAllLines(keyFile, new[] { key, f0.ToString("R", C) });
        }
        Phase("AutoView 그리기");
        var c = p.Cut;
        var shot = c with { Kind = c.Kind == CutKind.Move ? CutKind.Hold : c.Kind, B = null, StartSec = t, HoldSec = 1, HoldASec = 0, MoveSec = 0, HoldBSec = 0, Speed = 1 };
        var res = Render(work, j.Model, CameraScript.Build(shot, f0, PreviewWidth, PreviewHeight, 15, Quality, Path.Combine(work, "preview"), new[] { CalOff }, Legacy), CutTimeout, stop);
        if (!res.Ok) { File.Delete(keyFile); throw new InvalidOperationException(res.Error); } // the next try builds the model again
        var jpg = Previews.ImagePath(p.AnalysisId, c.Id);
        renderer.Thumbnail(res.Mp4!, jpg + ".tmp.jpg", 0, stop, PreviewWidth);
        File.Move(jpg + ".tmp.jpg", jpg, true);
    }

    /// <summary>thumb.jpg from the first cut that was made (1 s in); a failure (no ffmpeg) only means a grey frame on the page.</summary>
    void Thumb(FilmInfo f, string mp4Name, Action<string> say, CancellationToken stop)
    {
        var dir = films.FilmDir(f.Id); var thumb = Path.Combine(dir, "thumb.jpg");
        if (File.Exists(thumb)) return;
        try { renderer.Thumbnail(Path.Combine(dir, mp4Name), thumb, 1.0, stop); }
        catch (Exception e) when (e is InvalidOperationException or IOException or System.ComponentModel.Win32Exception) { say("썸네일을 만들지 못했습니다: " + e.Message); }
    }

    void SetItem(FilmInfo f, string jobId, string cutId, bool ok, string? error) => films.Update(f.Id, x =>
    {
        var i = x.Items.First(i => i.JobId == jobId && i.CutId == cutId);
        i.Ok = ok; i.Error = error;
        x.Done = x.Items.Count(i => i.Ok); x.Failed = x.Items.Count(i => i.Error != null);
    });

    /// <summary>The project without its run output and build products (rebuilt for the film anyway).</summary>
    static void Copy(string src, string dst, string model)
    {
        var skip = new[] { "runs", "work", model + ".dir", model + ".avw" }.Select(s => Path.Combine(src, s) + Path.DirectorySeparatorChar).ToList();
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            if (skip.Any(s => file.StartsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
            var to = Path.Combine(dst, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to, true);
        }
    }

    /// <summary>F0 (metres) of this film model, guide 10.4: two one-frame renders at scale 1 with and without the ash_cal mark, F0 = H × Lc / p.
    /// Each render has Film:CalibrationTimeoutMin (short: a stuck AutoView is reported in minutes, not after the cut limit).
    /// Falls back to the first guess 1.19 × the scene's 3D diagonal (a warning with the render's error and diagnostics in the log and on the film page)
    /// when the measurement fails, times out or is far off. A timeout counts towards the two-in-a-row rule: if AutoView is really stuck the first cut times out too and the film stops.</summary>
    double Calibrate(string work, string model, (double Cx, double Cy, double Lw, double Lh, double Diag) scene, double t, int width, int height, Action<string> say,
        Action<string> warn, CancellationToken stop)
    {
        var guess = 1.19 * scene.Diag;
        var legacy = Legacy;
        string Script(bool hide, string movie) => string.Join("\n",
            "camera description", "distance feet", "time seconds", "angle degree", "time scale 1", "orthogonal", "solid", "grid off", hide ? CalOff : "") + "\n" +
            CameraScript.MovieSpec(movie, width, height, 100, 15, legacy) + string.Join("\n",
            "time " + t.ToString("0.###", C),
            CameraScript.View(scene.Cx * 1e6, scene.Cy * 1e6, 0, scene.Cx * 1e6, scene.Cy * 1e6, 30480000, 0, 1, 0, 1, legacy),
            "record movie", "time " + (t + 1).ToString("0.###", C), "generate movie", "");
        var on = Render(work, model, Script(false, Path.Combine(work, "cal_on")), CalTimeout, stop);
        var off = on.Ok ? Render(work, model, Script(true, Path.Combine(work, "cal_off")), CalTimeout, stop) : on;
        stop.ThrowIfCancellationRequested(); // a cancelled render is not a calibration failure
        if ((on.TimedOut || off.TimedOut) && _timeouts >= 2) throw new AutoViewHungException();
        var why = on.Error ?? off.Error;
        (double W, double H)? px = null;
        try { if (on.Ok && off.Ok) px = renderer.MarkSize(on.Mp4!, off.Mp4!, width, height, stop); }
        catch (InvalidOperationException e) { say("보정 측정 실패: " + e.Message); }
        if (px is { } p && Math.Max(p.W, p.H) > 8)
        {
            // the longer side of the mark in pixels is the more accurate one
            var f0 = p.W >= p.H ? height * scene.Lw / p.W : height * scene.Lh / p.H;
            say($"F0 보정: 표시 {p.W:0}×{p.H:0} px (실제 {scene.Lw:0.##}×{scene.Lh:0.##} m) → F0 {f0:0.##} m (첫 추정 {guess:0.##} m)");
            if (f0 > guess / 3 && f0 < guess * 3) return f0;
            say("경고: 보정값이 첫 추정과 너무 달라 첫 추정을 씁니다.");
        }
        else say($"경고: F0 보정 실패({why ?? "표시가 보이지 않음"}) → 첫 추정 1.19 × 장면 대각선 = {guess:0.##} m를 씁니다.");
        warn(why == null ? CalWarning : $"{CalWarning} — {why}");
        logger.LogWarning("F0 보정 실패, 첫 추정 사용 {Work}", work);
        return guess;
    }


    static string Hms(double sec) { var t = TimeSpan.FromSeconds(Math.Max(0, sec)); return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"; }

    static string Pct(double d) => double.IsNaN(d) ? "-" : (d * 100).ToString("0", C) + "%";

    /// <summary>Caption numbers (spec 6.1): the version's conclusion (as its 결론 table), or for changed counts the search's runs at those counts,
    /// else this filming run's own values, marked "이 실행 1회 값".</summary>
    string Kpi(JobInfo j, ProjectInput input, Dictionary<string, int> counts, RunOutcome o)
    {
        var total = counts.Values.Sum();
        if (input.Groups.All(g => counts[g.Group] == (j.Decided!.TryGetValue(g.Group, out var d) ? d : g.MinCount)) && AnalysisPages.MetricsOf(jobs, j) is { } m)
            return $"권장 {total}대 · 가동률 {Pct(m.Util)} · 결품 {JobPages.F(m.Stop, "0")}초";
        var v = JobView.For(jobs, j);
        var runs = v.RunCounts().Where(kv => input.Groups.All(g => (kv.Value.TryGetValue(g.Group, out var n) ? n : g.MinCount) == counts[g.Group])).Select(kv => kv.Key).ToHashSet();
        var rows = v.Rows.Where(x => runs.Contains(x.RunId)).ToList();
        static double Mean(IEnumerable<double> xs) { var l = xs.Where(x => !double.IsNaN(x)).ToList(); return l.Count == 0 ? double.NaN : l.Average(); }
        if (rows.Count > 0)
            return $"{total}대 · 가동률 {Pct(rows.GroupBy(x => x.Group).Select(g => Mean(g.Select(x => x.Utilization))).DefaultIfEmpty(double.NaN).Max())} · " +
                   $"결품 {JobPages.F(Mean(rows.GroupBy(x => x.RunId).Select(g => g.Sum(x => x.StopTimeSec))), "0")}초";
        try
        {
            var k = ASH.Core.Runner.Kpi.FromRun(ResultParser.Parse(o.ResultTsvPath!), null, input);
            return $"{total}대 · 가동률 {Pct(k.Select(x => x.Utilization).DefaultIfEmpty(double.NaN).Max())} · 결품 {JobPages.F(k.Sum(x => x.StopTimeSec), "0")}초 · 이 실행 1회 값";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentNullException) { return $"{total}대 · 이 실행 1회 값"; }
    }
}
