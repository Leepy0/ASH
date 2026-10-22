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
builder.Services.AddSingleton(sp => AnalysisStore.Create(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILoggerFactory>().CreateLogger<AnalysisStore>()));
builder.Services.AddSingleton<IJobRunner, AutoModJobRunner>();
builder.Services.AddSingleton<JobWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobWorker>());
builder.Services.AddSingleton<JobRetention>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobRetention>());
builder.Services.AddSingleton(sp => new AppSettingsFile(Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "appsettings.json")));
builder.Services.AddSingleton<AdminSessions>();

var app = builder.Build();
app.Services.GetRequiredService<AnalysisStore>(); // a bad analyses.json is set aside and logged at boot
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
        return Msg(200, $"게시됨: ASH {m.Version} ({m.File})" + (s.Key == null ? "\n참고: 서명 없이 받았습니다. PC는 github.com의 같은 버전 ASH.json과 대조한 뒤에만 설치합니다." : ""));
    }
    catch (UploadException e) { return Msg(e.Status, e.Message); }
    catch (BadHttpRequestException e) { return Msg(e.StatusCode, e.Message); }
    catch (InvalidDataException e) { return Msg(400, e.Message); } // malformed multipart
});

static string ClientIp(HttpContext ctx)
{
    var a = ctx.Connection.RemoteIpAddress;
    if (a == null) return "";
    return (a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).ToString();
}
static bool AcceptsJobs(IConfiguration cfg) => cfg.GetValue("Jobs:Enabled", true);
static bool WorkerOn(IConfiguration cfg) => cfg.GetValue("Jobs:Enabled", true) && cfg.GetValue("Jobs:WorkerEnabled", true);
static IResult Html(string html, int status = 200) => Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, status);

app.MapGet("/", (ReleaseStore s, GitHubSync g, ServerUpdater u, RunnerStore r, JobStore js, IConfiguration cfg) =>
    Html(ServerPages.Home(new(s, g, u, r, js.All(), js.QueueInfo(), WorkerOn(cfg)))));

// ---- admin pages: settings (appsettings.json) and service restart ----
// Auth: the X-ASH-Admin-Token header (scripts), or the browser session cookie set by /admin/login after the token was typed once.
// Forms post with the cookie only from this site: the cookie is SameSite=Strict and every POST must carry an Origin (or Referer) of this host.
static bool SameOrigin(HttpContext ctx)
{
    var o = ctx.Request.Headers.Origin.ToString();
    if (o.Length == 0) o = ctx.Request.Headers.Referer.ToString();
    return Uri.TryCreate(o, UriKind.Absolute, out var u) && string.Equals(u.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
}
static bool HeaderAdmin(HttpContext ctx, ReleaseStore rs) => rs.TokenMatches(ctx.Request.Headers[ReleaseStore.HeaderName].FirstOrDefault());
static bool CookieAdmin(HttpContext ctx, AdminSessions sessions) => sessions.Valid(ctx.Request.Cookies[AdminSessions.CookieName]);
// null = allowed, else the answer to send
static IResult? DenyPost(HttpContext ctx, ReleaseStore rs, AdminSessions sessions)
{
    if (!rs.UploadsEnabled) return Msg(403, $"웹 설정이 꺼져 있습니다(Admin:Token이 비어 있거나 {ReleaseStore.MinTokenLength}자 미만).");
    if (HeaderAdmin(ctx, rs)) return null;
    if (!CookieAdmin(ctx, sessions)) return Msg(401, "관리자 로그인이 필요합니다(/admin/settings).");
    return SameOrigin(ctx) ? null : Msg(403, "다른 사이트에서 보낸 요청은 받지 않습니다.");
}
static Func<string, string?> FormLookup(IFormCollection f) => k => f.TryGetValue(k, out var v) ? v.ToString() : null;
static ServerPages.SettingsView View(AppSettingsFile file, ServerUpdater u, JobStore js, Func<string, string?>? submitted = null, SettingsSave? saved = null) =>
    new(file.Load(), file.Path, submitted, saved, u.RestartDisabledReason, js.All().FirstOrDefault(j => j.State == JobState.Running));

app.MapGet("/admin/settings", (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, AppSettingsFile file, ServerUpdater u, JobStore js) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (!rs.UploadsEnabled || !(HeaderAdmin(ctx, rs) || CookieAdmin(ctx, sessions))) return Html(ServerPages.Login(rs.UploadsEnabled), rs.UploadsEnabled ? 401 : 403);
    try { return Html(ServerPages.Settings(View(file, u, js))); }
    catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or IOException)
    { return Html(ServerPages.Message("서버 설정", $"<div class=\"msg bad\">appsettings.json을 읽을 수 없습니다: {WebUtility.HtmlEncode(e.Message)}</div>"), 500); }
});

app.MapPost("/admin/login", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions) =>
{
    if (!rs.UploadsEnabled) return Html(ServerPages.Login(false), 403);
    if (!SameOrigin(ctx)) return Msg(403, "다른 사이트에서 보낸 요청은 받지 않습니다.");
    var token = ctx.Request.HasFormContentType ? (await ctx.Request.ReadFormAsync())["token"].ToString() : "";
    if (!rs.TokenMatches(token))
    {
        await Task.Delay(1000); // slows guessing; the token is 16+ random characters anyway
        return Html(ServerPages.Login(true, "토큰이 틀립니다."), 401);
    }
    ctx.Response.Cookies.Append(AdminSessions.CookieName, sessions.Create(), new CookieOptions
    { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = ctx.Request.IsHttps, Path = "/admin", MaxAge = AdminSessions.Lifetime });
    return Results.Redirect("/admin/settings");
});

app.MapPost("/admin/logout", (HttpContext ctx, AdminSessions sessions) =>
{
    if (SameOrigin(ctx)) sessions.End(ctx.Request.Cookies[AdminSessions.CookieName]);
    ctx.Response.Cookies.Delete(AdminSessions.CookieName, new CookieOptions { Path = "/admin" });
    return Results.Redirect("/");
});

app.MapPost("/admin/settings", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, AppSettingsFile file, ServerUpdater u, JobStore js) =>
{
    if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
    if (!ctx.Request.HasFormContentType) return Msg(400, "폼 데이터가 필요합니다.");
    var form = FormLookup(await ctx.Request.ReadFormAsync());
    SettingsSave saved;
    try { saved = file.Save(form); }
    catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
    { return Html(ServerPages.Message("서버 설정", $"<div class=\"msg bad\">appsettings.json을 저장하지 못했습니다(바뀐 것 없음): {WebUtility.HtmlEncode(e.Message)}</div><p><a href=\"/admin/settings\">돌아가기</a></p>"), 500); }
    if (saved.Changed.Count > 0) app.Logger.LogWarning("설정 저장(웹, {Ip}): {Keys}", ClientIp(ctx), string.Join(", ", saved.Changed.Select(d => d.Key)));
    return Html(ServerPages.Settings(View(file, u, js, saved.Errors.Count > 0 ? form : null, saved)), saved.Errors.Count > 0 ? 400 : 200);
});

app.MapPost("/admin/restart", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, ServerUpdater u, JobStore js) =>
{
    if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
    var confirm = ctx.Request.HasFormContentType && (await ctx.Request.ReadFormAsync())["confirm"] == "1";
    var back = "<p><a href=\"/admin/settings\">설정으로 돌아가기</a></p>";
    if (u.RestartDisabledReason != null)
        return Html(ServerPages.Message("서비스 다시 시작", $"<div class=\"msg bad\">여기서는 다시 시작할 수 없습니다: {WebUtility.HtmlEncode(u.RestartDisabledReason)}</div>{back}"), 409);
    if (js.All().FirstOrDefault(j => j.State == JobState.Running) is { } running && !confirm)
        return Html(ServerPages.Message("서비스 다시 시작", $"<div class=\"msg warn\">작업 {WebUtility.HtmlEncode(running.Id)}이(가) 실행 중이라 다시 시작하지 않았습니다. 중단해도 되면 설정 페이지에서 확인란을 체크하고 다시 누르세요.</div>{back}"), 409);
    try { await u.RestartAsync(); }
    catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
    { return Html(ServerPages.Message("서비스 다시 시작", $"<div class=\"msg bad\">다시 시작하지 못했습니다(서버는 그대로 실행 중): {WebUtility.HtmlEncode(e.Message)}</div>{back}"), 500); }
    app.Logger.LogWarning("서비스 재시작 요청(웹, {Ip})", ClientIp(ctx));
    return Html(ServerPages.Message("서비스 다시 시작", "<div class=\"msg ok\">다시 시작하는 중입니다. 15초 뒤 홈으로 이동합니다. 열리지 않으면 잠시 뒤 새로 고치세요(결과: 서버 폴더 update\\apply.log).</div>", "/"));
});

// ---- jobs: the plugin sends a model, the worker builds it and searches the vehicle counts, the pages show the result ----

app.MapGet("/api/jobs/queue", (JobStore js, IConfiguration cfg) =>
{
    var q = js.QueueInfo();
    return Results.Json(new { ahead = q.Ahead, estimatedStart = q.EstimatedStartUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
        typicalMinutes = q.Typical?.TotalMinutes, enabled = WorkerOn(cfg) && AcceptsJobs(cfg) });
});

// ---- analyses: a study groups plans (안); a send is filed as the next version of a plan ----
const int NameMax = 100, NoteMax = 200;
var analysisGate = new object(); // find-or-create by name must not run twice at once, or two sends would make two analyses

static int NextVersion(IEnumerable<JobInfo> jobs, string analysisId, string planId) =>
    jobs.Where(j => j.AnalysisId == analysisId && j.PlanId == planId).Select(j => j.Version).DefaultIfEmpty(0).Max() + 1;

app.MapGet("/api/analyses", (AnalysisStore st, JobStore js) =>
{
    var jobs = js.All();
    return Results.Json(st.All().Select(a => new { a.Id, a.Name, a.Note, Plans = a.Plans.Select(p => new { p.Id, p.Name, NextVersion = NextVersion(jobs, a.Id, p.Id) }) }));
});

// form fields name/note, trimmed; over the limit (or not a form) is a 400 (null) instead of a silent cut
static async Task<(string Name, string Note)?> NameNote(HttpContext ctx)
{
    if (!ctx.Request.HasFormContentType) return null;
    var f = await ctx.Request.ReadFormAsync();
    string name = f["name"].ToString().Trim(), note = f["note"].ToString().Trim();
    return name.Length > NameMax || note.Length > NoteMax ? null : (name, note);
}
const string NameNoteError = "이름은 100자, 메모는 200자까지이며 폼 데이터(name, note)가 필요합니다.";

app.MapPost("/api/analyses", async (HttpContext ctx, AnalysisStore st) =>
{
    if (await NameNote(ctx) is not { } v) return Msg(400, NameNoteError);
    try { return Results.Json(new { st.CreateAnalysis(v.Name, v.Note).Id }); }
    catch (ArgumentException e) { return Msg(400, e.Message); }
});

app.MapPost("/api/analyses/{id}/plans", async (string id, HttpContext ctx, AnalysisStore st) =>
{
    if (await NameNote(ctx) is not { } v) return Msg(400, NameNoteError);
    try { return Results.Json(new { st.CreatePlan(id, v.Name, v.Note).Id }); }
    catch (ArgumentException e) { return Msg(400, e.Message); }
});

// web-form actions: no login, but a browser post must come from this site
static async Task<IFormCollection?> FormFrom(HttpContext ctx) => ctx.Request.HasFormContentType ? await ctx.Request.ReadFormAsync() : null;
const string CrossSite = "다른 사이트에서 보낸 요청은 받지 않습니다.";

// where a form goes after it is done: its "back" field when that is a path on this site, else the fallback
static string Back(IFormCollection? f, string fallback)
{
    var b = f?["back"].ToString() ?? "";
    return b.Length > 1 && b[0] == '/' && b[1] != '/' && b.IndexOfAny(new[] { '\\', '\r', '\n' }) < 0 ? b : fallback;
}

app.MapPost("/analyses", async (HttpContext ctx, AnalysisStore st) =>
{
    if (!SameOrigin(ctx)) return Msg(403, CrossSite);
    if (await NameNote(ctx) is not { } v) return Msg(400, NameNoteError);
    try { return Results.Redirect("/analyses/" + st.CreateAnalysis(v.Name, v.Note).Id); }
    catch (ArgumentException e) { return Msg(400, e.Message); }
});

app.MapPost("/analyses/{id}/plans", async (string id, HttpContext ctx, AnalysisStore st) =>
{
    if (!SameOrigin(ctx)) return Msg(403, CrossSite);
    if (await NameNote(ctx) is not { } v) return Msg(400, NameNoteError);
    try { st.CreatePlan(id, v.Name, v.Note); return Results.Redirect(Back(await FormFrom(ctx), "/analyses/" + id)); }
    catch (ArgumentException e) { return Msg(400, e.Message); }
});

app.MapPost("/analyses/{id}/rename", async (string id, HttpContext ctx, AnalysisStore st) =>
{
    if (!SameOrigin(ctx)) return Msg(403, CrossSite);
    var f = await FormFrom(ctx);
    var name = f?["name"].ToString().Trim() ?? "";
    if (name.Length > NameMax) return Msg(400, "이름은 100자까지입니다.");
    try { st.Rename(id, null, name); return Results.Redirect(Back(f, "/analyses/" + id)); }
    catch (ArgumentException e) { return Msg(400, e.Message); }
});

app.MapPost("/analyses/{id}/plans/{pid}/rename", async (string id, string pid, HttpContext ctx, AnalysisStore st) =>
{
    if (!SameOrigin(ctx)) return Msg(403, CrossSite);
    var f = await FormFrom(ctx);
    var name = f?["name"].ToString().Trim() ?? "";
    if (name.Length > NameMax) return Msg(400, "이름은 100자까지입니다.");
    try { st.Rename(id, pid, name); return Results.Redirect(Back(f, "/analyses/" + id)); }
    catch (ArgumentException e) { return Msg(400, e.Message); }
});

app.MapPost("/analyses/{id}/delete", (string id, HttpContext ctx, AnalysisStore st, JobStore js) =>
{
    if (!SameOrigin(ctx)) return Msg(403, CrossSite);
    return st.DeleteIfEmpty(id, js) ? Results.Redirect("/analyses") : Msg(409, "작업이 있거나 없는 분석은 지울 수 없습니다.");
});

// analysisId + planId, or one "target" field "analysisId|planId" (what the page's select sends)
app.MapPost("/jobs/{id}/move", async (string id, HttpContext ctx, AnalysisStore st, JobStore js) =>
{
    if (!SameOrigin(ctx)) return Msg(403, CrossSite);
    if (!JobStore.IsValidId(id) || js.Get(id) == null) return Msg(404, "작업이 없습니다.");
    var f = await FormFrom(ctx);
    var target = (f?["target"].ToString() ?? "").Split('|');
    string aid = target.Length == 2 ? target[0] : f?["analysisId"].ToString() ?? "", pid = target.Length == 2 ? target[1] : f?["planId"].ToString() ?? "";
    try { st.Move(js, id, aid, pid); return Results.Redirect(Back(f, "/jobs/" + id)); }
    catch (ArgumentException e) { return Msg(400, e.Message); }
});

app.MapPost("/jobs/{id}/select", async (string id, HttpContext ctx, AnalysisStore st, JobStore js) =>
{
    if (!SameOrigin(ctx)) return Msg(403, CrossSite);
    if (!JobStore.IsValidId(id) || js.Get(id) == null) return Msg(404, "작업이 없습니다.");
    var f = await FormFrom(ctx);
    st.SetSelected(js, id, f?["on"].ToString() == "1");
    return Results.Redirect(Back(f, "/jobs/" + id));
});

app.MapGet("/analyses", (AnalysisStore st, JobStore js) => Html(AnalysisPages.Index(st.All(), js.All())));
app.MapGet("/analyses/unclassified", (AnalysisStore st, JobStore js) => Html(AnalysisPages.Unclassified(js.All(), st.All())));
app.MapGet("/analyses/{id}", (string id, AnalysisStore st, JobStore js) =>
    st.Get(id) is { } a ? Html(AnalysisPages.Detail(a, js.All(), js)) : Msg(404, "분석이 없습니다."));

app.MapPost("/api/jobs", async (HttpContext ctx, JobStore js, AnalysisStore analyses, IConfiguration cfg, CancellationToken ct) =>
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
        var q = js.QueueInfo(id);
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

app.MapGet("/jobs/{id}", (string id, string? tab, HttpContext ctx, JobStore js, ReleaseStore rs, AnalysisStore analyses) =>
{
    if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
    var view = j.State == JobState.Queued ? null : JobView.For(js, j);
    var t = tab is "detail" or "input" or "files" ? tab : "summary";
    return Results.Content(JobPages.Job(j, view, t, t == "files" ? Tail(js.LogPath(id), 200) : "", js.QueueInfo(id), CanCancel(ctx, j, rs, null), analyses.All()), "text/html; charset=utf-8");
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

app.Run();
return 0;

public partial class Program { }
