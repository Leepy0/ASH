using System.Net;

namespace ASH.Server;

internal static partial class Routes
{
    static int NextVersion(IEnumerable<JobInfo> jobs, string analysisId, string planId) =>
        jobs.Where(j => j.AnalysisId == analysisId && j.PlanId == planId).Select(j => j.Version).DefaultIfEmpty(0).Max() + 1;

    /// <summary>Analyses (a study groups plans (안); a send is filed as the next version of a plan), their cut editor and films.</summary>
    public static void MapAnalysis(this WebApplication app)
    {
        app.MapGet("/api/analyses", (AnalysisStore st, JobStore js) =>
        {
            var jobs = js.All();
            return Results.Json(st.All().Select(a => new { a.Id, a.Name, a.Note, Plans = a.Plans.Select(p => new { p.Id, p.Name, NextVersion = NextVersion(jobs, a.Id, p.Id) }) }));
        });

        app.MapPost("/api/analyses", async (HttpContext ctx, AnalysisStore st) =>
        {
            if (CrossSiteBrowser(ctx)) return Msg(403, "다른 사이트에서 보낸 요청은 받지 않습니다.");
            if (await NameNote(ctx) is not { } v) return Msg(400, NameNoteError);
            try { return Results.Json(new { st.CreateAnalysis(v.Name, v.Note).Id }); }
            catch (ArgumentException e) { return Msg(400, e.Message); }
        });

        app.MapPost("/api/analyses/{id}/plans", async (string id, HttpContext ctx, AnalysisStore st) =>
        {
            if (CrossSiteBrowser(ctx)) return Msg(403, "다른 사이트에서 보낸 요청은 받지 않습니다.");
            if (await NameNote(ctx) is not { } v) return Msg(400, NameNoteError);
            try { return Results.Json(new { st.CreatePlan(id, v.Name, v.Note).Id }); }
            catch (ArgumentException e) { return Msg(400, e.Message); }
        });

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

        // admin delete of any analysis (under /admin because only there the admin cookie is sent): the jobs stay as unclassified, the rest of it goes
        app.MapGet("/admin/analyses/{id}/delete", (string id, HttpContext ctx, ReleaseStore rs, AdminSessions sessions, AnalysisStore st, JobStore js, FilmStore fs, CutStore cs) =>
        {
            ctx.Response.Headers.CacheControl = "no-store";
            if (!rs.UploadsEnabled || !(HeaderAdmin(ctx, rs) || CookieAdmin(ctx, sessions)))
                return Html(ServerPages.Login(rs.UploadsEnabled, null, ctx.Request.Path.Value, "분석 삭제(관리자)"), rs.UploadsEnabled ? 401 : 403);
            if (st.Get(id) is not { } a) return Msg(404, "분석이 없습니다.");
            return Html(AnalysisPages.DeleteConfirm(a, js.All().Count(j => j.AnalysisId == id), fs.All().Where(f => f.Request.AnalysisId == id).ToList(), cs.Get(id).Count));
        });

        app.MapPost("/admin/analyses/{id}/delete", async (string id, HttpContext ctx, ReleaseStore rs, AdminSessions sessions, AnalysisStore st, JobStore js, FilmStore fs, CutStore cs) =>
        {
            if (DenyPost(ctx, rs, sessions) is { } deny) return deny;
            if (st.Get(id) is not { } a) return Msg(404, "분석이 없습니다.");
            var back = $"<p><a href=\"/admin/analyses/{WebUtility.HtmlEncode(id)}/delete\">돌아가기</a></p>";
            if ((await FormFrom(ctx))?["name"].ToString().Trim() != a.Name)
                return Html(ServerPages.Message("분석 삭제", $"<div class=\"msg bad\">입력한 이름이 분석 이름과 다릅니다. 아무것도 지우지 않았습니다.</div>{back}"), 400);
            try
            {
                if (fs.RemoveByAnalysis(id) is not { } films)
                    return Html(ServerPages.Message("분석 삭제", $"<div class=\"msg bad\">이 분석의 영상이 대기 중이거나 만드는 중이라 지우지 않았습니다. 영상 페이지에서 먼저 취소한 뒤 다시 시도하세요.</div>{back}"), 409);
                cs.Delete(id);
                var jobs = st.DeleteAnalysis(id, js) ?? 0;
                app.Logger.LogWarning("분석 삭제(웹, {Ip}): {Id} '{Name}', 작업 {Jobs}건 분류 해제, 영상 {Films}건 삭제", ClientIp(ctx), id, a.Name, jobs, films);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { return Html(ServerPages.Message("분석 삭제", $"<div class=\"msg bad\">지우는 중 파일 오류가 났습니다(영상을 재생 중이면 닫고 다시 시도하세요): {WebUtility.HtmlEncode(e.Message)}</div>{back}"), 500); }
            return Results.Redirect("/analyses");
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
        app.MapGet("/analyses/{id}", (string id, AnalysisStore st, JobStore js, FilmStore fs, CatalogStore cs) =>
            st.Get(id) is { } a ? Html(AnalysisPages.Detail(a, js.All(), js, fs.All(), cs.Prices())) : Msg(404, "분석이 없습니다."));

        // ---- cuts: an analysis' film cuts, edited in the browser and filmed by the server ----
        app.MapGet("/analyses/{id}/cuts", (string id, string? job, AnalysisStore st, JobStore js) =>
            st.Get(id) is { } a ? Html(CutEditorPage.Render(a, js.All(), js, job)) : Msg(404, "분석이 없습니다."));

        app.MapGet("/api/analyses/{id}/cuts", (string id, AnalysisStore st, CutStore cs) =>
            st.Get(id) == null ? Msg(404, "분석이 없습니다.") : Results.Json(cs.Get(id), CutStore.Json));

        app.MapPut("/api/analyses/{id}/cuts", async (string id, HttpContext ctx, AnalysisStore st, CutStore cs) =>
        {
            if (!SameOrigin(ctx)) return Msg(403, CrossSite);
            if (st.Get(id) == null) return Msg(404, "분석이 없습니다.");
            List<ASH.Core.Film.CutSpec>? cuts;
            try { cuts = await System.Text.Json.JsonSerializer.DeserializeAsync<List<ASH.Core.Film.CutSpec>>(ctx.Request.Body, CutStore.Json, ctx.RequestAborted); }
            catch (System.Text.Json.JsonException e) { return Msg(400, $"컷 목록을 읽을 수 없습니다(JSON 배열이 필요합니다, 위치 {e.Path})."); }
            if (cuts == null) return Msg(400, "컷 목록(JSON 배열)이 필요합니다.");
            try { cs.Save(id, cuts); }
            catch (ArgumentException e) { return Msg(400, e.Message); }
            return Results.Json(cs.Get(id), CutStore.Json);
        });

        // ---- films: the export of the cut editor, filmed by FilmWorker between jobs ----
        app.MapPost("/api/analyses/{id}/films", async (string id, HttpContext ctx, AnalysisStore st, CutStore cs, JobStore js, FilmStore fs, IConfiguration cfg) =>
        {
            if (!SameOrigin(ctx)) return Msg(403, CrossSite);
            if (!WorkerOn(cfg)) return Msg(409, WorkerOff);
            if (st.Get(id) is not { } a) return Msg(404, "분석이 없습니다.");
            FilmRequest? r;
            try { r = await System.Text.Json.JsonSerializer.DeserializeAsync<FilmRequest>(ctx.Request.Body, CutStore.Json, ctx.RequestAborted); }
            catch (System.Text.Json.JsonException e) { return Msg(400, $"내보내기 요청을 읽을 수 없습니다(위치 {e.Path})."); }
            if (r == null) return Msg(400, "내보내기 요청(JSON)이 필요합니다.");
            string? bad = r.CutIds is not { Count: > 0 } ? "컷을 하나 이상 고르세요."
                : r.JobIds is not { Count: > 0 } ? "버전을 하나 이상 고르세요."
                : r.CutIds.Count > CutStore.MaxCuts || r.JobIds.Count > 50 ? "컷이나 버전이 너무 많습니다."
                : r.CutIds.Distinct().Count() != r.CutIds.Count || r.JobIds.Distinct().Count() != r.JobIds.Count ? "같은 컷이나 버전을 두 번 골랐습니다."
                : (r.Width, r.Height) is not ((1280, 720) or (1920, 1080)) ? "해상도는 1280×720 또는 1920×1080입니다."
                : r.Fps is not (15 or 30) ? "프레임은 15 또는 30입니다."
                : r.Output is not ("each+joined" or "joined" or "each") ? "파일 형태는 each+joined, joined, each 중 하나입니다."
                : r.VehicleDelta is < -DeltaMax or > DeltaMax ? $"대수 변경은 -{DeltaMax}~{DeltaMax}대입니다."
                : r.SameCounts != null && r.SameCounts.Values.Any(n => n is < 1 or > 999) ? "같은 대수는 그룹마다 1~999대입니다."
                : r.Seed is < 0 ? "시드는 0 이상입니다." : null;
            if (bad != null) return Msg(400, bad);
            var f = FilmWorker.Plan(r with { Id = JobStore.NewId(), AnalysisId = a.Id }, a, cs.Get(a.Id), js.Get, out var why);
            if (f == null) return Msg(400, why!);
            fs.Add(f);
            js.Wake();
            return Results.Json(new { id = f.Id });
        });

        app.MapGet("/films/{id}", (string id, FilmStore fs) =>
        {
            if (!JobStore.IsValidId(id) || fs.Get(id) is not { } f) return Msg(404, "영상이 없습니다.");
            var dir = fs.FilmDir(id);
            return Html(AnalysisPages.Film(f, n => File.Exists(Path.Combine(dir, n)), Tail(Path.Combine(dir, "film.log"), 60)));
        });

        // a queued film is cancelled at once; a running one stops its AutoView/ffmpeg run, the cuts left are marked cancelled and the MP4s made so far stay
        app.MapPost("/films/{id}/cancel", (string id, HttpContext ctx, FilmStore fs, FilmWorker fw) =>
        {
            if (!SameOrigin(ctx)) return Msg(403, CrossSite);
            if (!JobStore.IsValidId(id) || fs.Get(id) == null) return Msg(404, "영상이 없습니다.");
            fs.Update(id, x =>
            {
                if (x.Finished) return;
                x.CancelRequested = true;
                if (x.State != JobState.Queued) return;
                x.State = JobState.Cancelled; x.FinishedUtc = DateTime.UtcNow; x.Error = "찍기 전에 취소되었습니다.";
                foreach (var i in x.Items.Where(i => !i.Ok)) i.Error ??= "취소되었습니다.";
                x.Failed = x.Items.Count(i => i.Error != null);
            });
            fw.CancelRunning(id);
            return Results.Redirect("/films/" + id);
        });

        // only names the film itself lists are served: never a path from the URL
        app.MapGet("/films/{id}/files/{name}", (string id, string name, FilmStore fs) =>
        {
            if (!JobStore.IsValidId(id) || fs.Get(id) is not { } f) return Msg(404, "영상이 없습니다.");
            if (name != "film.log" && name != f.Joined && f.Items.All(i => i.Name != name)) return Msg(404, "그런 파일은 없습니다.");
            var path = Path.Combine(fs.FilmDir(id), name);
            if (!File.Exists(path)) return Msg(404, "파일이 아직 없거나 지워졌습니다.");
            if (name == "film.log") return Results.File(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), "text/plain; charset=utf-8");
            return Results.File(path, "video/mp4", enableRangeProcessing: true); // inline: the page's <video> plays it, the link's download attribute saves it
        });
    }
}
