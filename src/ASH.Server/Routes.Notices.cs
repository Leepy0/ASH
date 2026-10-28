namespace ASH.Server;

internal static partial class Routes
{
    /// <summary>GET /api/notices?current=&lt;version&gt; (public, the plugin reads it) and the admin page /admin/notices with its POSTs (add, save, approve, hide, draft, github, delete).</summary>
    public static void MapNotices(this WebApplication app)
    {
        app.MapGet("/api/notices", (HttpContext ctx, string? current, NoticeStore ns, ReleaseStore rs) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Bytes(ns.For(current, rs.GetCurrent()?.Manifest.Version ?? "", DateTime.UtcNow).ToJson(), "application/json");
        });

        app.MapGet("/admin/notices", (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, NoticeStore ns, GitHubSync g) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (!rs.UploadsEnabled || !(HeaderAdmin(ctx, rs) || CookieAdmin(ctx, sessions)))
                return Html(ServerPages.Login(rs.UploadsEnabled, null, "/admin/notices", "공지(관리자)"), rs.UploadsEnabled ? 401 : 403);
            return Html(NoticePages.Page(ns.All(), g.BaseUrl == null ? "꺼짐(GitHub:Enabled=false)" : g.NotesResult));
        });

        app.MapPost("/admin/notices", async (HttpContext ctx, ReleaseStore rs, AdminSessions sessions, NoticeStore ns) =>
        {
            if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
            var f = await FormFrom(ctx);
            if (f == null) return Msg(400, "폼이 아닙니다.");
            var (from, to) = (NoticeStore.Day(f["from"]), NoticeStore.Day(f["to"]));
            if (from == null || to == null) return Msg(400, "날짜는 yyyy-MM-dd 형식입니다.");
            var n = ns.Add(f["title"].ToString(), f["important"].ToString().Length > 0, NoticeStore.SplitLines(f["lines"]), from, to, out var err);
            return n == null ? Msg(400, err!) : Results.Redirect("/admin/notices#" + n.Id);
        });

        app.MapPost("/admin/notices/{id}/{act}", async (string id, string act, HttpContext ctx, ReleaseStore rs, AdminSessions sessions, NoticeStore ns) =>
        {
            if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
            if (ns.Get(id) is not { } n) return Msg(404, "공지가 없습니다.");
            var f = await FormFrom(ctx);
            // save, approve, hide and draft all keep the text that was typed in the form
            if (act is "save" or "approve" or "hide" or "draft" && f != null && f.ContainsKey("title"))
            {
                string? from = null, to = null;
                if (n.General)
                {
                    (from, to) = (NoticeStore.Day(f["from"]), NoticeStore.Day(f["to"]));
                    if (from == null || to == null) return Msg(400, "날짜는 yyyy-MM-dd 형식입니다.");
                }
                if (ns.Edit(id, f["title"].ToString(), f["important"].ToString().Length > 0, NoticeStore.SplitLines(f["lines"]), from, to) is { } err) return Msg(400, err);
            }
            switch (act)
            {
                case "save": break;
                case "approve": ns.SetState(id, NoticeStore.Approved); break;
                case "hide": ns.SetState(id, NoticeStore.Hidden); break;
                case "draft": ns.SetState(id, NoticeStore.Draft); break;
                case "github": if (!ns.TakeGithub(id)) return Msg(409, "GitHub 내용이 없습니다."); break;
                case "delete": if (!ns.Delete(id)) return Msg(409, "GitHub에서 온 공지는 지울 수 없습니다. 숨기세요."); return Results.Redirect("/admin/notices");
                default: return Msg(404, "없는 명령입니다.");
            }
            app.Logger.LogInformation("공지 {Id} {Act}(웹, {Ip})", id, act, ClientIp(ctx));
            return Results.Redirect("/admin/notices#" + id);
        });
    }
}
