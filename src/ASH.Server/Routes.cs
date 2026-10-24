using System.Text;

namespace ASH.Server;

/// <summary>Helpers shared by the route files (Routes.*.cs); each file adds one Map* extension for its group of endpoints.</summary>
internal static partial class Routes
{
    internal const long UploadLimit = 512L * 1024 * 1024;
    internal const int NameMax = 100, NoteMax = 200, DeltaMax = 20;
    internal const string CrossSite = "다른 사이트에서 보낸 요청은 받지 않습니다.";
    internal const string TagError = "법인·제품군·라인은 100자까지입니다.";
    internal const string NameNoteError = "이름은 100자, 메모는 200자까지이며 폼 데이터(name, note)가 필요합니다.";
    internal const string WorkerOff = "이 서버는 작업을 실행하지 않습니다(Jobs:Enabled 또는 Jobs:WorkerEnabled가 false).";

    internal static IResult Msg(int status, string text) => Results.Text(text, "text/plain; charset=utf-8", Encoding.UTF8, status);

    internal static string ClientIp(HttpContext ctx)
    {
        var a = ctx.Connection.RemoteIpAddress;
        if (a == null) return "";
        return (a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).ToString();
    }
    internal static bool AcceptsJobs(IConfiguration cfg) => cfg.GetValue("Jobs:Enabled", true);
    internal static bool WorkerOn(IConfiguration cfg) => cfg.GetValue("Jobs:Enabled", true) && cfg.GetValue("Jobs:WorkerEnabled", true);
    internal static IResult Html(string html, int status = 200) => Results.Content(html, "text/html; charset=utf-8", Encoding.UTF8, status);
    // a running film or scene search holds the one worker like a job: it counts as one ahead, and its length is unknown (no start estimate)
    internal static (int Ahead, DateTime? EstimatedStartUtc, TimeSpan? Typical) Queue(JobStore js, FilmWorker fw, string? id = null)
    {
        var q = js.QueueInfo(id);
        return fw.Busy == null ? q : (q.Ahead + 1, null, q.Typical);
    }

    // Auth: the X-ASH-Admin-Token header (scripts), or the browser session cookie set by /admin/login after the token was typed once.
    // Forms post with the cookie only from this site: the cookie is SameSite=Strict and every POST must carry an Origin (or Referer) of this host.
    internal static bool SameOrigin(HttpContext ctx)
    {
        var o = ctx.Request.Headers.Origin.ToString();
        if (o.Length == 0) o = ctx.Request.Headers.Referer.ToString();
        return Uri.TryCreate(o, UriKind.Absolute, out var u) && string.Equals(u.Authority, ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
    internal static bool HeaderAdmin(HttpContext ctx, ReleaseStore rs) => rs.TokenMatches(ctx.Request.Headers[ReleaseStore.HeaderName].FirstOrDefault());
    internal static bool CookieAdmin(HttpContext ctx, AdminSessions sessions) => sessions.Valid(ctx.Request.Cookies[AdminSessions.CookieName]);
    // null = allowed, else the answer to send
    internal static IResult? DenyPost(HttpContext ctx, ReleaseStore rs, AdminSessions sessions)
    {
        if (!rs.UploadsEnabled) return Msg(403, $"웹 설정이 꺼져 있습니다(Admin:Password가 {ReleaseStore.MinPasswordLength}~{ReleaseStore.MaxPasswordLength}자 규칙에 맞지 않음).");
        if (HeaderAdmin(ctx, rs)) return null;
        if (!CookieAdmin(ctx, sessions)) return Msg(401, "관리자 로그인이 필요합니다(/admin/settings).");
        return SameOrigin(ctx) ? null : Msg(403, "다른 사이트에서 보낸 요청은 받지 않습니다.");
    }

    // form fields name/note, trimmed; over the limit (or not a form) is a 400 (null) instead of a silent cut
    internal static async Task<(string Name, string Note)?> NameNote(HttpContext ctx)
    {
        if (!ctx.Request.HasFormContentType) return null;
        var f = await ctx.Request.ReadFormAsync();
        string name = f["name"].ToString().Trim(), note = f["note"].ToString().Trim();
        return name.Length > NameMax || note.Length > NoteMax ? null : (name, note);
    }

    // optional tag fields, trimmed; null when one is over NameMax
    internal static (string Corp, string ProductLine, string Line)? Tags(IFormCollection f)
    {
        string c = f["corp"].ToString().Trim(), p = f["productLine"].ToString().Trim(), l = f["line"].ToString().Trim();
        return c.Length > NameMax || p.Length > NameMax || l.Length > NameMax ? null : (c, p, l);
    }

    // the plugin sends no Origin/Referer (and does not call these); a browser always does, so a cross-site page cannot create analyses
    internal static bool CrossSiteBrowser(HttpContext ctx) => (ctx.Request.Headers.Origin.Count > 0 || ctx.Request.Headers.Referer.Count > 0) && !SameOrigin(ctx);

    // web-form actions: no login, but a browser post must come from this site
    internal static async Task<IFormCollection?> FormFrom(HttpContext ctx) => ctx.Request.HasFormContentType ? await ctx.Request.ReadFormAsync() : null;

    // where a form goes after it is done: its "back" field when that is a path on this site, else the fallback
    internal static string Back(IFormCollection? f, string fallback)
    {
        var b = f?["back"].ToString() ?? "";
        return b.Length > 1 && b[0] == '/' && b[1] != '/' && b.IndexOf('\\') < 0 && !b.Any(char.IsControl) ? b : fallback; // a tab/CR/LF is stripped by browsers: "/\t/x" would become "//x"
    }

    internal static string Tail(string path, int lines)
    {
        if (!File.Exists(path)) return "";
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (fs.Length > 256 * 1024) fs.Seek(-256 * 1024, SeekOrigin.End);
        using var rd = new StreamReader(fs, Encoding.UTF8);
        var all = rd.ReadToEnd().Split('\n');
        return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
    }

    internal static bool CanCancel(HttpContext ctx, JobInfo j, ReleaseStore rs, string? token) =>
        ClientIp(ctx) == j.Ip || (rs.UploadsEnabled && rs.TokenMatches(token ?? ctx.Request.Headers[ReleaseStore.HeaderName]));
}
