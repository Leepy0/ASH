using System.Net;
using ASH.Core;
using ASH.Core.Update;
using ASH.Server;
using Microsoft.AspNetCore.Http.Features;
using ASH.Core.Runner;
using static ASH.Server.Routes;

if (args.Length > 0 && args[0] is "--install" or "--uninstall") return ServiceCommand.Run(install: args[0] == "--install");
if (args.Length > 0 && args[0] == FilmHelper.Arg) return FilmHelper.RunHelper();
if (args.Length > 0 && args[0] is FilmHelper.InstallArg or FilmHelper.UninstallArg) return FilmHelper.Install(args[0] == FilmHelper.InstallArg, args.Length > 1 ? args[1] : null);

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
builder.Services.AddSingleton(sp => new NoticeStore(sp.GetRequiredService<JobStore>().Dir, sp.GetRequiredService<ILoggerFactory>().CreateLogger<NoticeStore>()));
builder.Services.AddSingleton<ServerUpdater>();
builder.Services.AddSingleton<GitHubSync>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GitHubSync>());
builder.Services.AddSingleton(sp => JobStore.Create(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILoggerFactory>().CreateLogger<JobStore>()));
builder.Services.AddSingleton(sp => AnalysisStore.Create(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<ILoggerFactory>().CreateLogger<AnalysisStore>()));
builder.Services.AddSingleton(sp => new CutStore(sp.GetRequiredService<JobStore>().Dir, sp.GetRequiredService<ILoggerFactory>().CreateLogger<CutStore>()));
builder.Services.AddSingleton(sp => new CatalogStore(sp.GetRequiredService<JobStore>().Dir, sp.GetRequiredService<ILoggerFactory>().CreateLogger<CatalogStore>()));
builder.Services.AddSingleton(sp => new ASH.Server.Catalog.AssetStore(sp.GetRequiredService<JobStore>().Dir, sp.GetRequiredService<ILoggerFactory>().CreateLogger<ASH.Server.Catalog.AssetStore>()));
builder.Services.AddSingleton<IJobRunner, AutoModJobRunner>();
builder.Services.AddSingleton(sp => new FilmStore(sp.GetRequiredService<JobStore>().Dir, sp.GetRequiredService<ILoggerFactory>().CreateLogger<FilmStore>()));
builder.Services.AddSingleton<IFilmAutoMod, AutoModFilm>();
// AutoView program, AutoMod folder and ffmpeg are read from the settings at each film run (see LiveFilmRenderer)
builder.Services.AddSingleton<ASH.Core.Film.IFilmRenderer>(sp => new LiveFilmRenderer(sp.GetRequiredService<IConfiguration>(),
    new FilmHelper.Client(FilmHelper.DirOf(sp.GetRequiredService<JobStore>().Dir), FilmHelper.Wake, TimeSpan.FromSeconds(30), () => FilmHelper.Diagnose(FilmHelper.DirOf(sp.GetRequiredService<JobStore>().Dir)))));
builder.Services.AddSingleton<LimitWorker>();
builder.Services.AddSingleton<FilmWorker>();
builder.Services.AddSingleton<JobWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobWorker>());
builder.Services.AddSingleton<JobRetention>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<JobRetention>());
builder.Services.AddSingleton(sp => new AppSettingsFile(Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, "appsettings.json")));
builder.Services.AddSingleton<AdminSessions>();

var app = builder.Build();
// an unhandled error shows what went wrong instead of an empty 500, so a user can pass the line on; the whole exception goes to the log
app.UseExceptionHandler(err => err.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
    var e = ex?.Error;
    app.Logger.LogError(e, "요청 처리 실패 {Path}", ex?.Path);
    var what = e == null ? "알 수 없는 오류" : $"{e.GetType().Name}: {e.Message}";
    var at = e?.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("ASH.")) ?? "";
    ctx.Response.StatusCode = 500;
    if (ex?.Path.StartsWith("/api/", StringComparison.Ordinal) == true) { await ctx.Response.WriteAsJsonAsync(new { error = what }); return; }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    await ctx.Response.WriteAsync(ServerPages.Message("서버 오류", $"<div class=\"msg bad\">{WebUtility.HtmlEncode(what)}</div>" +
        $"<p class=\"muted\">{WebUtility.HtmlEncode(ex?.Path ?? "")} · {WebUtility.HtmlEncode(at)} · {WebUtility.HtmlEncode(SeoulTime.NowText)}</p><p>이 내용을 관리자에게 알려 주세요.</p>"));
}));
ASH.Core.Cause.CauseAnalysis.Failed += (name, e) => app.Logger.LogWarning(e, "원인 분석 {Name} 건너뜀", name);
app.Services.GetRequiredService<AnalysisStore>(); // a bad analyses.json is set aside and logged at boot
app.Services.GetRequiredService<ReleaseStore>(); // bad key/folder/GitHub URL fails at start, not at the first request
app.Services.GetRequiredService<GitHubSync>();
app.Services.GetRequiredService<JobStore>();
app.Services.GetRequiredService<FilmStore>(); // a film that was running when the server stopped is queued again now

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
    if (!s.UploadsEnabled) return Msg(403, $"업로드가 꺼져 있습니다(Admin:Password가 {ReleaseStore.MinPasswordLength}~{ReleaseStore.MaxPasswordLength}자 규칙에 맞지 않음).");
    if (!s.TokenMatches(ctx.Request.Headers[ReleaseStore.HeaderName])) return Msg(401, "관리자 암호가 없거나 틀립니다.");
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

app.MapGet("/", (HttpContext ctx, string? me, JobStore js, AnalysisStore st, FilmStore fs, FilmWorker fw, IConfiguration cfg) =>
{
    var tags = FileRoutes.FileQuery.From(ctx.Request.Query);
    var films = fs.All(); var jobs = js.All();
    var v = HomeView.Build(st.All(), jobs, films, films.FirstOrDefault(f => f.State == JobState.Running), Routes.Queue(js, fw), Routes.WorkerOn(cfg),
        JobProgress.Parallel(cfg), Routes.ClientIp(ctx), me switch { "1" => true, "0" => false, _ => null }, DateTime.UtcNow, tags, js, fw.Waiting(jobs));
    var live = LiveFields.OfHome(v);
    return Routes.Html(HomePages.Home(v, FileRoutes.TagFilterHtml(tags, st), tags.TagQuery(), live.Active ? JobPages.PollScript("/api/home?me=" + (v.Me ? 1 : 0) + tags.TagQuery(), live.Watch) : null,
        ctx.Request.Query["tab"].ToString()));
});

static ServerPages.StatusData StatusOf(HttpContext ctx)
{
    var sp = ctx.RequestServices;
    var js = sp.GetRequiredService<JobStore>();
    return new(sp.GetRequiredService<ReleaseStore>(), sp.GetRequiredService<GitHubSync>(), sp.GetRequiredService<ServerUpdater>(),
        sp.GetRequiredService<RunnerStore>(), js.All(), Queue(js, sp.GetRequiredService<FilmWorker>()), WorkerOn(sp.GetRequiredService<IConfiguration>()), sp.GetRequiredService<AnalysisStore>().All());
}

// ---- admin pages: settings (appsettings.json) and service restart ----
static Func<string, string?> FormLookup(IFormCollection f) => k => f.TryGetValue(k, out var v) ? v.ToString() : null;
static ServerPages.SettingsView View(AppSettingsFile file, ServerUpdater u, JobStore js, Func<string, string?>? submitted = null, SettingsSave? saved = null) =>
    new(file.Load(), file.Path, submitted, saved, u.RestartDisabledReason, js.All().FirstOrDefault(j => j.State == JobState.Running));

app.MapGet("/admin/settings", (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, AppSettingsFile file, ServerUpdater u, JobStore js) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (!rs.UploadsEnabled || !(HeaderAdmin(ctx, rs) || CookieAdmin(ctx, sessions))) return Html(ServerPages.Login(rs.UploadsEnabled), rs.UploadsEnabled ? 401 : 403);
    try { return Html(ServerPages.Settings(View(file, u, js), StatusOf(ctx), ctx.Request.Query["tab"].ToString())); }
    catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or IOException)
    { return Html(ServerPages.Message("서버 설정", $"<div class=\"msg bad\">appsettings.json을 읽을 수 없습니다: {WebUtility.HtmlEncode(e.Message)}</div>"), 500); }
});

app.MapPost("/admin/login", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions) =>
{
    if (!rs.UploadsEnabled) return Html(ServerPages.Login(false), 403);
    if (!SameOrigin(ctx)) return Msg(403, "다른 사이트에서 보낸 요청은 받지 않습니다.");
    var form = ctx.Request.HasFormContentType ? await ctx.Request.ReadFormAsync() : null;
    var token = form?["token"].ToString() ?? "";
    var back = Back(form, "/admin/settings"); // an /admin page that asked for the login (e.g. the analysis delete page)
    if (!rs.TokenMatches(token))
    {
        await Task.Delay(1000); // slows guessing: a 4-character password is short
        return Html(ServerPages.Login(true, "관리자 암호가 틀립니다.", back == "/admin/settings" ? null : back), 401);
    }
    ctx.Response.Cookies.Append(AdminSessions.CookieName, sessions.Create(), new CookieOptions
    { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = ctx.Request.IsHttps, Path = "/admin", MaxAge = AdminSessions.Lifetime });
    return Results.Redirect(back);
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
    return Html(ServerPages.Settings(View(file, u, js, saved.Errors.Count > 0 ? form : null, saved), StatusOf(ctx), "server"), saved.Errors.Count > 0 ? 400 : 200);
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

// ---- server update: only staged on its own; an admin applies it from the banner every page loads ----
app.MapGet("/admin/update-banner", (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, ServerUpdater u, JobStore js, FilmWorker fw) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (!rs.UploadsEnabled || !(HeaderAdmin(ctx, rs) || CookieAdmin(ctx, sessions)) || u.Waiting is not { } v) return Results.NoContent();
    return Html(ServerPages.UpdateBanner(v, u.Armed, js.All().Any(j => j.State == JobState.Running) || fw.Busy != null));
});
// act: now = apply at once (running work is cut off and continues after the start), after = at the worker's next gap, cancel = undo "after"
app.MapPost("/admin/update", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, ServerUpdater u, JobStore js) =>
{
    if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
    var form = ctx.Request.HasFormContentType ? await ctx.Request.ReadFormAsync() : null;
    var act = form?["act"].ToString();
    var back = Back(form, "/");
    if (act == "cancel") { u.Armed = false; return Results.Redirect(back); }
    if (act is not ("now" or "after")) return Msg(404, "없는 명령입니다.");
    if (u.Waiting is not { } v) return Html(ServerPages.Message("서버 업데이트", "<div class=\"msg warn\">받아 둔 업데이트 없음</div><p><a href=\"/\">홈</a></p>"), 409);
    if (act == "after" && u.WorkerApplies)
    {
        u.Armed = true;
        js.Wake(); // an idle worker applies it at once
        app.Logger.LogWarning("서버 업데이트 {Version}: 작업 끝나면 적용(웹, {Ip})", v, ClientIp(ctx));
        return Results.Redirect(back);
    }
    app.Logger.LogWarning("서버 업데이트 {Version}: 지금 적용(웹, {Ip})", v, ClientIp(ctx)); // "after" without a worker: nothing runs, so now
    if (!await u.ApplyNowAsync())
        return Html(ServerPages.Message("서버 업데이트", $"<div class=\"msg bad\">적용 실패 · 서버 그대로: {WebUtility.HtmlEncode(u.LastResult)}</div><p><a href=\"/\">홈</a></p>"), 500);
    return Html(ServerPages.Message("서버 업데이트", $"<div class=\"msg ok\">{WebUtility.HtmlEncode(v)} 적용 중 · 15초 뒤 홈</div>", "/"));
});

// 최신 버전 확인: the scheduled GitHub check (ASH = plugin, Runner, then the server, which only stages) starts in the background and the page
// returns at once; the 서버 설정 tab polls the steps from /admin/check-updates/status. One check at a time (a second press starts nothing).
app.MapPost("/admin/check-updates", (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, GitHubSync g) =>
{
    if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
    if (g.BaseUrl == null) return Html(ServerPages.Message("최신 버전 확인", "<div class=\"msg warn\">GitHub 가져오기 꺼짐(GitHub:Enabled=false)</div><p><a href=\"/admin/settings?tab=server\">돌아가기</a></p>"), 409);
    app.Logger.LogInformation(g.StartCheck(app.Lifetime.ApplicationStopping) ? "최신 버전 확인 시작(웹, {Ip})" : "최신 버전 확인: 이미 진행 중(웹, {Ip})", ClientIp(ctx));
    return Results.Redirect("/admin/settings?tab=server");
});
app.MapGet("/admin/check-updates/status", (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, GitHubSync g) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (!rs.UploadsEnabled || !(HeaderAdmin(ctx, rs) || CookieAdmin(ctx, sessions))) return Msg(rs.UploadsEnabled ? 401 : 403, "관리자 로그인이 필요합니다(/admin/settings).");
    return Results.Json(g.Progress.Snapshot());
});

app.MapGet("/admin/jobs", (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, JobStore js, FilmWorker fw, IConfiguration cfg, AnalysisStore st) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    if (!rs.UploadsEnabled || !(HeaderAdmin(ctx, rs) || CookieAdmin(ctx, sessions))) return Html(ServerPages.Login(rs.UploadsEnabled, back: "/admin/jobs", title: "작업 대기열"), rs.UploadsEnabled ? 401 : 403);
    var all = ctx.Request.Query["archived"] == "1";
    return Html(JobPages.List(js.All().Where(j => all || !j.Archived).ToList(), Queue(js, fw), WorkerOn(cfg), admin: true, archived: all, archivedCount: js.All().Count(j => j.Archived), analyses: st.All()));
});
// 작업 대기열 buttons: cancel a queued/running job, put a finished one away (or back), delete a finished one with its folder
app.MapPost("/admin/jobs/{id}/{act}", (string id, string act, HttpContext ctx, ReleaseStore rs, AdminSessions sessions, JobStore js, JobWorker worker) =>
{
    if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
    if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
    var back = ctx.Request.Query["archived"] == "1" ? "/admin/jobs?archived=1" : "/admin/jobs";
    switch (act)
    {
        case "cancel":
            js.Update(id, x =>
            {
                if (x.Finished) return;
                x.CancelRequested = true;
                if (x.State == JobState.Queued) { x.State = JobState.Cancelled; x.FinishedUtc = DateTime.UtcNow; x.Error = "관리자가 실행 전에 취소했습니다."; }
            });
            worker.CancelRunning(id);
            break;
        case "resume": case "restart":
            if (!js.Requeue(id, act == "resume")) return Msg(409, "실패했거나 취소된 서버 작업만 다시 실행할 수 있습니다.");
            break;
        case "archive": case "unarchive":
            if (!j.Finished) return Msg(409, "대기 중이거나 실행 중인 작업은 보관할 수 없습니다. 먼저 취소하세요.");
            js.Update(id, x => x.Archived = act == "archive");
            break;
        case "delete":
            if (!js.Delete(id)) return Msg(409, "대기 중이거나 실행 중인 작업은 지울 수 없습니다. 먼저 취소하세요.");
            break;
        default: return Msg(404, "없는 명령입니다.");
    }
    return Results.Redirect(back);
});
app.MapGet("/jobs", () => Results.Redirect("/admin/jobs", permanent: true));

app.MapJobs();
app.MapAnalysis();
app.MapReport();
app.MapCatalog();
app.MapAssets();
app.MapFiles();
app.MapFilms();
app.MapPreview();
app.MapAdminFs();
app.MapNotify();
app.MapNotices();
app.MapLimit();

app.Run();
return 0;

public partial class Program { }
