using ASH.Core.Film;

namespace ASH.Server;

internal static partial class Routes
{
    public const string NoAutoView = "AutoView 없음";

    /// <summary>Cut preview (영상 만들기 inspector): one AutoView frame of the cut as the editor has it, made by FilmWorker between jobs.
    /// POST queues it (body = the cut, ?job = the version), GET answers its state, preview.jpg is the frame.</summary>
    public static void MapPreview(this WebApplication app)
    {
        app.MapPost("/api/analyses/{id}/cuts/{cutId}/preview", async (string id, string cutId, string? job, HttpContext ctx, AnalysisStore st, CutStore cs, JobStore js, FilmWorker fw, IConfiguration cfg) =>
        {
            if (!SameOrigin(ctx)) return Msg(403, CrossSite);
            if (!WorkerOn(cfg)) return Msg(409, WorkerOff);
            if (st.Get(id) is not { } a) return Msg(404, "분석이 없습니다.");
            if (!cs.Get(a.Id).Any(c => c.Id == cutId)) return Msg(404, "저장된 컷이 아닙니다. 먼저 리스트에 추가하세요.");
            var exe = LiveFilmRenderer.ResolveAviewExe(cfg);
            if (exe == null || !File.Exists(exe))
                return Msg(503, $"{NoAutoView}: 서버에서 AutoView 실행 파일을 찾지 못해 미리보기를 만들 수 없습니다({exe ?? AutoViewExe.Candidates(LiveFilmRenderer.AmHome(cfg))}, 설정 Film:AviewExe).");
            CutSpec? cut;
            try { cut = await System.Text.Json.JsonSerializer.DeserializeAsync<CutSpec>(ctx.Request.Body, CutStore.Json, ctx.RequestAborted); }
            catch (System.Text.Json.JsonException e) { return Msg(400, $"컷을 읽을 수 없습니다(위치 {e.Path})."); }
            if (cut == null || cut.Id != cutId) return Msg(400, "컷(JSON)이 필요하고 id가 주소와 같아야 합니다.");
            if (CutStore.Validate(new[] { cut }) is { } why) return Msg(400, why);
            if (job == null || js.Get(job) is not { } j || j.AnalysisId != a.Id) return Msg(400, "이 분석의 버전(job)을 고르세요.");
            if (j.State != JobState.Done || j.Decided == null) return Msg(400, "결론 대수가 나온 완료 버전만 미리볼 수 있습니다.");
            var simLen = JobView.For(js, j).Input?.Settings.SimLengthSec ?? double.MaxValue;
            var at = cut.StartSec + 1; // the first second of the cut: its opening view
            if (at + 1 > simLen) return Msg(400, $"시뮬레이션 길이({simLen:0}초)를 넘는 컷입니다.");
            var p = new PreviewInfo { AnalysisId = a.Id, JobId = j.Id, Cut = cut, AtSec = at };
            if (!fw.Previews.Request(p)) return Msg(409, "이 컷의 미리보기를 만드는 중입니다. 끝난 뒤 다시 누르세요.");
            js.Wake();
            return Results.Json(Status(p, fw, js), CutStore.Json, statusCode: 202);
        });

        app.MapGet("/api/analyses/{id}/cuts/{cutId}/preview", (string id, string cutId, FilmWorker fw, JobStore js) =>
            PreviewStore.Ok(id) && PreviewStore.Ok(cutId) && fw.Previews.Get(id, cutId) is { } p ? Results.Json(Status(p, fw, js), CutStore.Json) : Msg(404, "미리보기가 없습니다."));

        app.MapGet("/api/analyses/{id}/cuts/{cutId}/preview.jpg", (string id, string cutId, HttpContext ctx, FilmWorker fw) =>
        {
            if (!PreviewStore.Ok(id) || !PreviewStore.Ok(cutId)) return Msg(404, "미리보기가 없습니다.");
            var jpg = fw.Previews.ImagePath(id, cutId);
            ctx.Response.Headers.CacheControl = "no-store";
            return File.Exists(jpg) ? Results.File(new FileStream(jpg, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), "image/jpeg") : Msg(404, "미리보기 그림이 없습니다.");
        });
    }

    // ahead: what the preview waits for: the running job, the running film work, earlier previews (the worker takes one job, then one piece of film work)
    static object Status(PreviewInfo p, FilmWorker fw, JobStore js) => new
    {
        p.State, p.Phase, p.Error, p.AtSec, p.JobId, p.Cut, p.RequestedUtc, p.FinishedUtc,
        image = p.State == JobState.Done ? $"/api/analyses/{p.AnalysisId}/cuts/{p.Cut.Id}/preview.jpg?t={p.FinishedUtc?.Ticks}" : null,
        busy = fw.Busy,
        ahead = p.State == JobState.Queued ? js.All().Count(x => x.State == JobState.Running) + (fw.Busy != null ? 1 : 0) + fw.Previews.Ahead(p) : 0,
    };
}
