using System.Net;
using System.Text;
using ASH.Core.Update;
using ASH.Server;
using Microsoft.AspNetCore.Http.Features;

const long UploadLimit = 512L * 1024 * 1024;

if (args.Length > 0 && args[0] is "--install" or "--uninstall") return ServiceCommand.Run(install: args[0] == "--install");

// content root = exe folder (as a service the working directory is system32; from a console it may be anywhere)
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Host.UseWindowsService(o => o.ServiceName = ServiceCommand.ServiceName);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = UploadLimit);
builder.Services.AddSingleton(sp => ReleaseStore.Create(sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>(),
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<ReleaseStore>()));

var app = builder.Build();
app.Services.GetRequiredService<ReleaseStore>(); // bad key/folder fails at start, not at the first request

static IResult Msg(int status, string text) => Results.Text(text, "text/plain; charset=utf-8", Encoding.UTF8, status);

app.MapGet("/healthz", () => Results.Text("ok"));

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

app.MapGet("/download/{file}", (string file, ReleaseStore s) =>
{
    if (!ReleaseManifest.IsValidFileName(file)) return Msg(400, "파일 이름이 올바르지 않습니다.");
    var path = Path.Combine(s.FilesDir, file);
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

app.MapGet("/", (ReleaseStore s) =>
{
    static string H(string? t) => WebUtility.HtmlEncode(t ?? "");
    var cur = s.GetCurrent()?.Manifest;
    var sb = new StringBuilder("<!doctype html><html lang=\"ko\"><head><meta charset=\"utf-8\"><title>ASH Server</title></head><body><h1>ASH 배포 서버</h1>");
    if (cur == null) sb.Append("<p>게시된 버전이 없습니다.</p>");
    else sb.Append($"<table border=\"1\" cellpadding=\"4\"><tr><th>현재 버전</th><td>{H(cur.Version)}</td></tr><tr><th>최소 지원</th><td>{H(cur.MinSupported)}</td></tr>" +
        $"<tr><th>게시 시각</th><td>{H(cur.Published)}</td></tr><tr><th>설치 파일</th><td>{H(cur.File)} ({cur.Size:N0} bytes)</td></tr></table>");
    sb.Append("<p>서명 키: ").Append(s.Key == null ? "<b>서명 키 없음(서명 확인 안 함)</b>" : H(s.Key.KeyId)).Append("</p>");
    sb.Append("<p>업로드: ").Append(s.UploadsEnabled ? "가능" : "꺼짐(Admin:Token 미설정 또는 짧음)").Append("</p><h2>이전 버전</h2><ul>");
    foreach (var h in s.History()) sb.Append($"<li>{H(h.Version)} ({H(h.Published)})</li>");
    sb.Append("</ul></body></html>");
    return Results.Content(sb.ToString(), "text/html; charset=utf-8");
});

app.Run();
return 0;

public partial class Program { }
