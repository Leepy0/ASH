using ASH.Core.Runner;
using Microsoft.AspNetCore.Http.Features;

namespace ASH.Server;

internal static partial class Routes
{
    // find-or-create by name must not run twice at once, or two sends would make two analyses
    static readonly object analysisGate = new();

    static string ScenesFile(int delta) => FilmWorker.ScenesFile(delta);
    const string ScenesRequest = FilmWorker.ScenesRequest;

    /// <summary>Jobs: the plugin sends a model, the worker builds it and searches the vehicle counts, the pages show the result.</summary>
    public static void MapJobs(this WebApplication app)
    {
        app.MapGet("/api/jobs/queue", (JobStore js, FilmWorker fw, IConfiguration cfg) =>
        {
            var q = Queue(js, fw);
            return Results.Json(new { ahead = q.Ahead, estimatedStart = q.EstimatedStartUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
                typicalMinutes = q.Typical?.TotalMinutes, enabled = WorkerOn(cfg) && AcceptsJobs(cfg), busy = fw.Busy });
        });

        app.MapPost("/api/jobs", async (HttpContext ctx, JobStore js, AnalysisStore analyses, FilmWorker fw, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!AcceptsJobs(cfg)) return Msg(403, "이 서버는 작업을 받지 않습니다(Jobs:Enabled=false).");
            if (!ctx.Request.HasFormContentType) return Msg(400, "multipart/form-data가 필요합니다.");
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } lim) lim.MaxRequestBodySize = UploadLimit;
            IFormCollection form;
            try { form = await ctx.Request.ReadFormAsync(ct); }
            catch (BadHttpRequestException e) { return Msg(e.StatusCode, e.Message); }
            catch (InvalidDataException e) { return Msg(400, e.Message); }
            string Field(string k, int max) { var v = form[k].ToString().Trim(); return v.Length > max ? v[..max] : v; }
            var model = Field("model", 64);
            if (model.Length == 0 || model.Contains("..") || model.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || !ASH.Core.Layout.LayoutChecker.IsValidName(model))
                return Msg(400, "모델 이름이 올바르지 않습니다: " + model);
            var analysisId = Field("analysisId", 64); var planId = Field("planId", 64);
            var analysisName = form["analysisName"].ToString().Trim(); var planName = form["planName"].ToString().Trim(); var versionNote = form["versionNote"].ToString().Trim();
            if (analysisName.Length > NameMax || planName.Length > NameMax) return Msg(400, "분석·안 이름은 100자까지입니다.");
            if (versionNote.Length > NoteMax) return Msg(400, "버전 메모는 200자까지입니다.");
            var classify = analysisId.Length + planId.Length + analysisName.Length + planName.Length > 0;
            if (classify && (analysisId.Length == 0 && analysisName.Length == 0 || planId.Length == 0 && planName.Length == 0))
                return Msg(400, "분석과 안을 모두 지정해야 합니다(analysisId/analysisName, planId/planName).");
            var pkg = form.Files.GetFile("package");
            if (pkg == null || pkg.Length == 0) return Msg(400, "package 파트(zip)가 필요합니다.");
            var id = JobStore.NewId();
            var dir = js.JobDir(id);
            Directory.CreateDirectory(dir);
            try
            {
                await using (var fs = File.Create(js.UploadPath(id))) await pkg.CopyToAsync(fs, ct);
                var files = JobPackage.Extract(js.UploadPath(id), js.ProjectDir(id), model);
                var job = new JobInfo
                {
                    Id = id, Model = model, Project = Field("project", 100), Plan = Field("plan", 100), Submitter = Field("submitter", 60), Ip = ClientIp(ctx),
                    CreatedUtc = DateTime.UtcNow, Impact = form["impact"].ToString() != "0", InitialReps = 1, Files = files.ToList(), UploadBytes = pkg.Length,
                };
                if (job.Project.Length == 0) job.Project = model;
                if (JobPackage.HasResults(js.ProjectDir(id)))
                {
                    // a result run on the user's PC (ash-run): shown like a server job, nothing is queued
                    var sum = SearchSummaryFile.Read(Path.Combine(js.ProjectDir(id), "runs", SearchSummaryFile.FileName));
                    var now = DateTime.UtcNow;
                    job.Source = JobSource.Local;
                    job.State = sum.ExitCode == 1 ? JobState.Failed : JobState.Done;
                    job.StartedUtc = now.AddSeconds(-Math.Max(0, sum.ElapsedSec)); job.FinishedUtc = now;
                    job.ExitCode = sum.ExitCode; job.Error = sum.Error; job.RunsDone = sum.Runs; job.RunsFailed = sum.Failures;
                    job.Decided = sum.Decided.Count == 0 ? null : sum.Decided.ToDictionary(kv => kv.Key, kv => kv.Value);
                    job.Clusters = sum.Clusters.Select(c => c.ToList()).ToList();
                    job.ConfirmRunIds = sum.ConfirmRunIds.ToList(); job.Warnings = sum.Warnings.ToList(); job.FailureMessages = sum.FailureMessages.ToList();
                }
                var version = 0;
                if (classify)
                {
                    // ids first, else find by name or create; resolved before js.Add so a wrong id leaves no job behind (the folder is removed below)
                    string aid, pid;
                    lock (analysisGate)
                    {
                        var a = analysisId.Length > 0 ? analyses.Get(analysisId) ?? throw new ArgumentException("분석이 없습니다: " + analysisId)
                            : analyses.All().FirstOrDefault(x => string.Equals(x.Name, analysisName, StringComparison.OrdinalIgnoreCase)) ?? analyses.CreateAnalysis(analysisName, "");
                        var p = planId.Length > 0 ? a.Plans.FirstOrDefault(x => x.Id == planId) ?? throw new ArgumentException("안이 없습니다: " + planId)
                            : a.Plans.FirstOrDefault(x => string.Equals(x.Name, planName, StringComparison.OrdinalIgnoreCase)) ?? analyses.CreatePlan(a.Id, planName, "");
                        aid = a.Id; pid = p.Id;
                    }
                    js.Add(job);
                    version = analyses.Assign(js, id, aid, pid, versionNote).Version;
                }
                else js.Add(job);
                var q = Queue(js, fw, id);
                return Results.Json(new { id, url = $"{ctx.Request.Scheme}://{ctx.Request.Host}/jobs/{id}", ahead = q.Ahead, version });
            }
            catch (ArgumentException e)
            {
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return Msg(400, e.Message);
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return Msg(400, "올린 파일을 받을 수 없습니다: " + e.Message);
            }
        });

        app.MapGet("/api/jobs/{id}", (string id, JobStore js) =>
        {
            if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
            return Results.Json(new { j.Id, j.State, j.Phase, j.RunsDone, j.RunsFailed, j.Error, j.Decided, j.Warnings, j.CreatedUtc, j.StartedUtc, j.FinishedUtc });
        });

        app.MapGet("/jobs/{id}", (string id, string? tab, HttpContext ctx, JobStore js, ReleaseStore rs, AnalysisStore analyses, FilmStore fs, FilmWorker fw) =>
        {
            if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
            var view = j.State == JobState.Queued ? null : JobView.For(js, j);
            var t = tab is "detail" or "input" or "files" ? tab : "summary";
            return Results.Content(JobPages.Job(j, view, t, t == "files" ? Tail(js.LogPath(id), 200) : "", Queue(js, fw, id), CanCancel(ctx, j, rs, null), analyses.All(), fs.All(), fw.Busy), "text/html; charset=utf-8");
        });

        app.MapPost("/jobs/{id}/cancel", async (string id, HttpContext ctx, JobStore js, JobWorker worker, ReleaseStore rs) =>
        {
            if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
            string? token = ctx.Request.HasFormContentType ? (await ctx.Request.ReadFormAsync())["token"].ToString() : null;
            if (!CanCancel(ctx, j, rs, string.IsNullOrEmpty(token) ? null : token)) return Msg(403, "보낸 PC나 관리자만 취소할 수 있습니다.");
            js.Update(id, x =>
            {
                if (x.Finished) return;
                x.CancelRequested = true;
                if (x.State == JobState.Queued) { x.State = JobState.Cancelled; x.FinishedUtc = DateTime.UtcNow; x.Error = "실행 전에 취소되었습니다."; }
            });
            worker.CancelRunning(id);
            return Results.Redirect($"/jobs/{id}");
        });

        app.MapGet("/jobs/{id}/files/{name}", (string id, string name, JobStore js) =>
        {
            if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
            var runs = Path.Combine(js.ProjectDir(id), "runs");
            string? path = name switch
            {
                "summary.tsv" => Path.Combine(runs, "summary.tsv"),
                "impact.tsv" => Path.Combine(runs, "impact.tsv"),
                "job.log" => js.LogPath(id),
                "upload.zip" => js.UploadPath(id),
                "runs.zip" => Path.Combine(js.JobDir(id), "runs.zip"),
                "report.xlsx" => Path.Combine(js.JobDir(id), "report.xlsx"),
                _ => null,
            };
            if (path == null) return Msg(404, "그런 파일은 없습니다.");
            if (name == "runs.zip")
            {
                if (!j.Finished) return Msg(409, "작업이 끝난 뒤에 받을 수 있습니다.");
                if (j.RunsPurgedUtc is { } purged) return Msg(410, $"실행 원본은 보관 기간이 지나 {ASH.Core.SeoulTime.Text(purged)}에 정리되었습니다(Jobs:KeepRunsDays). summary.tsv와 upload.zip은 남아 있습니다.");
                if (!Directory.Exists(runs)) return Msg(404, "실행 폴더가 없습니다.");
                if (!File.Exists(path))
                {
                    var tmp = path + ".tmp";
                    if (File.Exists(tmp)) File.Delete(tmp);
                    System.IO.Compression.ZipFile.CreateFromDirectory(runs, tmp, System.IO.Compression.CompressionLevel.Fastest, false);
                    File.Move(tmp, path, true);
                }
            }
            if (name == "report.xlsx")
            {
                if (!j.Finished) return Msg(409, "작업이 끝난 뒤에 받을 수 있습니다.");
                // made on first request; a job that changed afterwards (purged, re-read) gets a fresh one
                if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < File.GetLastWriteTimeUtc(Path.Combine(js.JobDir(id), "job.json")))
                    JobReport.Write(j, JobView.For(js, j), path);
            }
            if (!File.Exists(path)) return Msg(404, "파일이 아직 없습니다.");
            var type = name.EndsWith(".zip") ? "application/zip" : name.EndsWith(".xlsx") ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : "text/plain; charset=utf-8";
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Results.File(fs, type, $"{id}-{name}", enableRangeProcessing: name.EndsWith(".zip"));
        });

        // the factory drawing (DXF) and the AGV layout in model metres for the cut editor; built once, then cached next to job.json
        app.MapGet("/jobs/{id}/drawing.json", (string id, JobStore js) =>
        {
            if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
            var json = DrawingJson.Cached(js.JobDir(id), js.ProjectDir(id), j.Model);
            if (json == null) return Msg(410, "모델 폴더가 없습니다.");
            return Results.Content(json, "application/json; charset=utf-8");
        });

        // scene candidates: <job>\scenes.json (conclusion counts, written after the search) or scenes.delta<d>.json (counts + d, on request).
        // A request is <job>\scenes-request.json {"delta":d,"requestedUtc":...}; FilmWorker runs it between jobs, writes the result file and deletes the request.

        app.MapGet("/api/jobs/{id}/scenes", (string id, int? delta, JobStore js) =>
        {
            if (!JobStore.IsValidId(id) || js.Get(id) == null) return Msg(404, "작업이 없습니다.");
            var d = delta ?? 0;
            if (Math.Abs(d) > DeltaMax) return Msg(400, $"대수 변경은 -{DeltaMax}~{DeltaMax}대입니다.");
            var path = Path.Combine(js.JobDir(id), ScenesFile(d));
            if (File.Exists(path)) return Results.Content(File.ReadAllText(path), "application/json; charset=utf-8");
            var req = Path.Combine(js.JobDir(id), ScenesRequest);
            try
            {
                if (File.Exists(req) && System.Text.Json.JsonDocument.Parse(File.ReadAllText(req)).RootElement.GetProperty("delta").GetInt32() == d)
                    return Results.Json(new { pending = "후보를 찾는 중입니다. 작업 대기열에서 차례가 오면 모델을 한 번 돌립니다." });
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { }
            return Results.Content("[]", "application/json; charset=utf-8");
        });

        app.MapPost("/api/jobs/{id}/scenes", (string id, int? delta, HttpContext ctx, JobStore js, IConfiguration cfg) =>
        {
            if (!SameOrigin(ctx)) return Msg(403, CrossSite);
            if (!WorkerOn(cfg)) return Msg(409, WorkerOff);
            if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
            if (delta is not { } d || Math.Abs(d) > DeltaMax) return Msg(400, $"delta(결론 대수에 더할 대수, -{DeltaMax}~{DeltaMax})가 필요합니다.");
            if (j.State != JobState.Done || j.Decided == null) return Msg(409, "결론 대수가 나온 완료 작업에서만 후보를 찾을 수 있습니다.");
            var dir = js.JobDir(id);
            try
            {
                File.Delete(Path.Combine(dir, ScenesFile(d))); // the new result replaces it; until then the page shows "pending"
                var tmp = Path.Combine(dir, ScenesRequest + ".tmp");
                File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(new { delta = d, requestedUtc = DateTime.UtcNow }));
                File.Move(tmp, Path.Combine(dir, ScenesRequest), true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Msg(503, "후보 찾기 요청을 저장하지 못했습니다. 잠시 뒤 다시 눌러 주세요: " + e.Message); }
            js.Wake();
            return Results.Json(new { delta = d }, statusCode: 202);
        });

        // one run to watch in AutoMod on a PC: the run id is only looked up in summary.tsv, never joined into a path
        app.MapGet("/jobs/{id}/runs/{run}/model.zip", (string id, string run, HttpContext ctx, JobStore js, IConfiguration cfg) =>
        {
            if (!JobStore.IsValidId(id) || js.Get(id) is not { } j || j.State == JobState.Queued) return Msg(404, "작업이 없습니다.");
            var v = JobView.For(js, j);
            if (!RunModelZip.IsValidRunId(run) || v.Input == null || v.Scenario(run) is not { } s) return Msg(404, "그런 실행은 없습니다.");
            if (!Directory.Exists(Path.Combine(js.ProjectDir(id), j.Model + ".arc"))) return Msg(410, "모델 폴더가 없습니다.");
            var am = new AutoModJobRunner(cfg).AmHome;
            // the zip writer still flushes its deflate streams synchronously on dispose (even with the .NET 10 async APIs)
            if (ctx.Features.Get<IHttpBodyControlFeature>() is { } body) body.AllowSynchronousIO = true;
            return Results.Stream(body => RunModelZip.WriteAsync(body, js.ProjectDir(id), j.Model, v.Input, s, id, run, am), "application/zip", $"{id}-{run}.zip");
        });
    }
}
