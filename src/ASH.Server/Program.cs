using System.Net;
using System.Text;
using ASH.Core;
using ASH.Core.Update;
using ASH.Server;
using Microsoft.AspNetCore.Http.Features;
using ASH.Core.Runner;

const long UploadLimit = 512L * 1024 * 1024;

if (args.Length > 0 && args[0] is "--install" or "--uninstall") return ServiceCommand.Run(install: args[0] == "--install");

// content root = exe folder (as a service the working directory is system32; from a console it may be anywhere)
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Host.UseWindowsService(o => o.ServiceName = ServiceCommand.ServiceName);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = UploadLimit);
builder.Services.AddSingleton(sp => ReleaseStore.Create(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<ReleaseStore>()));
builder.Services.AddSingleton(sp =>
{
    var rs = sp.GetRequiredService<ReleaseStore>();
    return new RunnerStore(rs.Dir, rs.Key, sp.GetRequiredService<ILoggerFactory>().CreateLogger<RunnerStore>());
});
builder.Services.AddSingleton<ServerUpdater>();
builder.Services.AddSingleton<GitHubSync>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GitHubSync>());
builder.Services.AddSingleton(sp => JobStore.Create(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILoggerFactory>().CreateLogger<JobStore>()));
builder.Services.AddSingleton<IJobRunner, AutoModJobRunner>();
builder.Services.AddSingleton<JobWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobWorker>());

var app = builder.Build();
app.Services.GetRequiredService<ReleaseStore>(); // bad key/folder/GitHub URL fails at start, not at the first request
app.Services.GetRequiredService<GitHubSync>();
app.Services.GetRequiredService<JobStore>();

static IResult Msg(int status, string text) => Results.Text(text, "text/plain; charset=utf-8", Encoding.UTF8, status);

app.MapGet("/healthz", () => Results.Text("ok"));
// the self-update batch waits for this to answer the new version before it keeps the new exe
app.MapGet("/healthz/version", (ServerUpdater u) => Results.Text(u.Current));

// no-store: a proxy or browser cache must never keep serving an old manifest after a new release
app.MapGet("/api/ash/version", (HttpContext ctx, ReleaseStore s) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    return s.ReadManifestBytes() is { } b ? Results.Bytes(b, "application/json") : Msg(404, "게시된 버전이 없습니다.");
});

app.MapGet("/api/ash/version.sig", (HttpContext ctx, ReleaseStore s) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    return s.ReadSignatureBytes() is { } b ? Results.Bytes(b, "application/octet-stream") : Msg(404, "서명이 없습니다.");
});

// the runner (ash-run) updates itself from these: manifest + signature, then the zip through /download
app.MapGet("/api/ash/runner", (HttpContext ctx, RunnerStore r) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    return r.ReadManifestBytes() is { } b ? Results.Bytes(b, "application/json") : Msg(404, "게시된 Runner가 없습니다.");
});

app.MapGet("/api/ash/runner.sig", (HttpContext ctx, RunnerStore r) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    return r.ReadSignatureBytes() is { } b ? Results.Bytes(b, "application/octet-stream") : Msg(404, "서명이 없습니다.");
});

app.MapGet("/download/{file}", (string file, ReleaseStore s, RunnerStore r) =>
{
    var dir = ReleaseManifest.IsValidFileName(file) ? s.FilesDir : PackageManifest.IsRunnerFileName(file) ? r.FilesDir : null;
    if (dir == null) return Msg(400, "파일 이름이 올바르지 않습니다.");
    var path = Path.Combine(dir, file);
    return File.Exists(path) ? Results.File(path, "application/octet-stream", file, enableRangeProcessing: true) : Msg(404, "파일이 없습니다.");
});

app.MapPost("/api/admin/releases", async (HttpContext ctx, ReleaseStore s, CancellationToken ct) =>
{
    if (!s.UploadsEnabled) return Msg(403, $"업로드가 꺼져 있습니다(Admin:Token이 비어 있거나 {ReleaseStore.MinTokenLength}자 미만).");
    if (!s.TokenMatches(ctx.Request.Headers[ReleaseStore.HeaderName])) return Msg(401, "토큰이 없거나 틀립니다.");
    if (!ctx.Request.HasFormContentType) return Msg(400, "multipart/form-data가 필요합니다.");
    // the 512 MB limit applies to this endpoint only (Kestrel default stays 30 MB elsewhere)
    if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } lim) lim.MaxRequestBodySize = UploadLimit;
    try
    {
        var form = await ctx.Request.ReadFormAsync(ct);
        var manifest = form.Files.GetFile("manifest");
        var setup = form.Files.GetFile("setup");
        if (manifest == null || setup == null) return Msg(400, "manifest와 setup 파트가 필요합니다.");
        var m = await s.PublishAsync(manifest, form.Files.GetFile("signature"), setup, ctx.Request.Query["force"] == "1", ct);
        return Msg(200, $"게시됨: ASH {m.Version} ({m.File})" + (s.Key == null ? "\n경고: 서버에 서명 공개키가 없어 서명을 확인하지 않았습니다." : ""));
    }
    catch (UploadException e) { return Msg(e.Status, e.Message); }
    catch (BadHttpRequestException e) { return Msg(e.StatusCode, e.Message); }
    catch (InvalidDataException e) { return Msg(400, e.Message); } // malformed multipart
});

app.MapGet("/", (ReleaseStore s, GitHubSync g, ServerUpdater u, RunnerStore r) =>
{
    static string H(string? t) => WebUtility.HtmlEncode(t ?? "");
    var cur = s.GetCurrent()?.Manifest;
    var sb = new StringBuilder("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><title>ASH Server</title></head><body><h1>ASH 배포 서버</h1><p><a href=\"/jobs\">작업 대기열과 결과 보기</a></p>");
    if (cur == null) sb.Append("<p>게시된 버전이 없습니다.</p>");
    else sb.Append($"<table border=\"1\" cellpadding=\"4\"><tr><th>현재 버전</th><td>{H(cur.Version)}</td></tr><tr><th>최소 지원</th><td>{H(cur.MinSupported)}</td></tr>" +
        $"<tr><th>게시 시각(서울)</th><td>{H(SeoulTime.FromIso(cur.Published))}</td></tr><tr><th>설치 파일</th><td>{H(cur.File)} ({cur.Size:N0} bytes)</td></tr></table>");
    sb.Append("<p>서명 키: ").Append(s.Key == null
        ? (s.AllowUnsigned ? "<b>서명 키 없음(서명 확인 안 함) — Update:AllowUnsigned=true, 개발용</b>" : "<b>서명 키 없음 — 업로드 거절(Update:AllowUnsigned=true로만 허용)</b>")
        : H(s.Key.KeyId)).Append("</p>");
    sb.Append("<p>GitHub 가져오기: ").Append(g.BaseUrl == null ? "꺼짐" : H(g.BaseUrl) + $" ({g.Interval.TotalMinutes:0}분마다, DNS {(g.DnsServers.Count == 0 ? "PC 기본" : string.Join(", ", g.DnsServers))})")
      .Append("<br>마지막 확인(서울): ").Append(g.LastCheckUtc is { } t ? H(SeoulTime.Text(t)) + " — " + H(g.LastResult) : H(g.LastResult)).Append("</p>");
    sb.Append("<p>Runner(ash-run): ").Append(r.Current is { } rc ? H(rc.Version) + $" ({rc.Size:N0} bytes)" : "게시된 버전 없음").Append(" — 마지막 확인: ").Append(H(g.RunnerResult)).Append("</p>");
    sb.Append("<p>서버 프로그램: ").Append(H(u.Current)).Append(" — 자동 업데이트: ").Append(H(u.LastResult));
    if (u.PreviousRun != null) sb.Append("<br>지난 업데이트: ").Append(H(u.PreviousRun));
    sb.Append("</p>");
    // the clock behind every time on this page: if it differs from the wall clock the PC's own date and time is wrong
    sb.Append("<p>서버 시각(서울): ").Append(H(SeoulTime.NowText)).Append(" · PC 표준 시간대: ").Append(H(TimeZoneInfo.Local.Id)).Append("</p>");
    sb.Append("<p>수동 업로드: ").Append(s.UploadsEnabled ? "가능" : "꺼짐(Admin:Token 미설정 또는 짧음, GitHub 가져오기만 사용)").Append("</p><h2>이전 버전</h2><ul>");
    foreach (var h in s.History()) sb.Append($"<li>{H(h.Version)} ({H(SeoulTime.FromIso(h.Published))})</li>");
    sb.Append("</ul></body></html>");
    return Results.Content(sb.ToString(), "text/html; charset=utf-8");
});

// ---- jobs: the plugin sends a model, the worker builds it and searches the vehicle counts, the pages show the result ----
static string ClientIp(HttpContext ctx)
{
    var a = ctx.Connection.RemoteIpAddress;
    if (a == null) return "";
    return (a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).ToString();
}
static bool AcceptsJobs(IConfiguration cfg) => cfg.GetValue("Jobs:Enabled", true);
static bool WorkerOn(IConfiguration cfg) => cfg.GetValue("Jobs:Enabled", true) && cfg.GetValue("Jobs:WorkerEnabled", true);

app.MapGet("/api/jobs/queue", (JobStore js, IConfiguration cfg) =>
{
    var q = js.QueueInfo();
    return Results.Json(new { ahead = q.Ahead, estimatedStart = q.EstimatedStartUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
        typicalMinutes = q.Typical?.TotalMinutes, enabled = WorkerOn(cfg) && AcceptsJobs(cfg) });
});

app.MapPost("/api/jobs", async (HttpContext ctx, JobStore js, IConfiguration cfg, CancellationToken ct) =>
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
        js.Add(job);
        var q = js.QueueInfo(id);
        return Results.Json(new { id, url = $"{ctx.Request.Scheme}://{ctx.Request.Host}/jobs/{id}", ahead = q.Ahead });
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

app.MapGet("/jobs", (JobStore js, IConfiguration cfg) =>
    Results.Content(JobPages.List(js.All(), js.QueueInfo(), WorkerOn(cfg)), "text/html; charset=utf-8"));

static string Tail(string path, int lines)
{
    if (!File.Exists(path)) return "";
    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    if (fs.Length > 256 * 1024) fs.Seek(-256 * 1024, SeekOrigin.End);
    using var rd = new StreamReader(fs, Encoding.UTF8);
    var all = rd.ReadToEnd().Split('\n');
    return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
}

static bool CanCancel(HttpContext ctx, JobInfo j, ReleaseStore rs, string? token) =>
    ClientIp(ctx) == j.Ip || (rs.UploadsEnabled && rs.TokenMatches(token ?? ctx.Request.Headers[ReleaseStore.HeaderName]));

app.MapGet("/jobs/{id}", (string id, string? tab, HttpContext ctx, JobStore js, ReleaseStore rs) =>
{
    if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
    var view = j.State == JobState.Queued ? null : JobView.For(js, j);
    var t = tab is "detail" or "input" or "files" ? tab : "summary";
    return Results.Content(JobPages.Job(j, view, t, t == "files" ? Tail(js.LogPath(id), 200) : "", js.QueueInfo(id), CanCancel(ctx, j, rs, null)), "text/html; charset=utf-8");
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
        _ => null,
    };
    if (path == null) return Msg(404, "그런 파일은 없습니다.");
    if (name == "runs.zip")
    {
        if (!j.Finished) return Msg(409, "작업이 끝난 뒤에 받을 수 있습니다.");
        if (!Directory.Exists(runs)) return Msg(404, "실행 폴더가 없습니다.");
        if (!File.Exists(path))
        {
            var tmp = path + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);
            System.IO.Compression.ZipFile.CreateFromDirectory(runs, tmp, System.IO.Compression.CompressionLevel.Fastest, false);
            File.Move(tmp, path, true);
        }
    }
    if (!File.Exists(path)) return Msg(404, "파일이 아직 없습니다.");
    var type = name.EndsWith(".zip") ? "application/zip" : "text/plain; charset=utf-8";
    var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    return Results.File(fs, type, $"{id}-{name}", enableRangeProcessing: name.EndsWith(".zip"));
});

app.Run();
return 0;

public partial class Program { }
