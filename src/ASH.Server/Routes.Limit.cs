namespace ASH.Server;

internal static partial class Routes
{
    /// <summary>생산량 한계: queue or cancel the production limit of a finished job (the 결론 tabs show it); same rights as a rerun.</summary>
    public static void MapLimit(this WebApplication app)
    {
        app.MapPost("/jobs/{id}/limit", async (string id, HttpContext ctx, JobStore js, LimitWorker lw, ReleaseStore rs, IConfiguration cfg) =>
        {
            if (!AcceptsJobs(cfg)) return Msg(403, "작업 받지 않는 서버");
            if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업 없음");
            if (CrossSiteBrowser(ctx)) return Msg(403, CrossSite);
            var form = await FormFrom(ctx);
            var token = form?["token"].ToString();
            if (!CanCancel(ctx, j, rs, string.IsNullOrEmpty(token) ? null : token)) return Msg(403, "보낸 PC나 관리자만");
            if (j.State != JobState.Done || j.Decided == null) return Msg(409, "결론 대수 나온 완료 작업만");
            lw.Request(id); // queued or running already: nothing changes
            return Results.Redirect(Back(form, $"/jobs/{id}"));
        });

        app.MapPost("/jobs/{id}/limit/cancel", async (string id, HttpContext ctx, JobStore js, LimitWorker lw, ReleaseStore rs) =>
        {
            if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업 없음");
            if (CrossSiteBrowser(ctx)) return Msg(403, CrossSite);
            var form = await FormFrom(ctx);
            var token = form?["token"].ToString();
            if (!CanCancel(ctx, j, rs, string.IsNullOrEmpty(token) ? null : token)) return Msg(403, "보낸 PC나 관리자만");
            lw.Cancel(id);
            return Results.Redirect(Back(form, $"/jobs/{id}"));
        });
    }
}
