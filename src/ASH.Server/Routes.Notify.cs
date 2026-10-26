using System.Globalization;

namespace ASH.Server;

internal static partial class Routes
{
    /// <summary>GET /api/me/events?since=: my jobs and films that ended (done, failed, cancelled) after <c>since</c>, for the browser notifier.
    /// "Mine" = same IP or submitter name as HomeView (no login). Without a usable <c>since</c> nothing is returned, only <c>now</c> to start from.</summary>
    public static void MapNotify(this WebApplication app)
    {
        app.MapGet("/api/me/events", (HttpContext ctx, string? since, JobStore js, FilmStore fs, AnalysisStore st) =>
        {
            var now = DateTime.UtcNow;
            var events = new List<object>();
            if (DateTime.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var from))
            {
                if (from < now.AddDays(-1)) from = now.AddDays(-1); // a tab left alone for days does not replay old results
                var ip = ClientIp(ctx);
                var jobs = js.All();
                var names = Mine.Names(jobs, ip);
                // (end time, event); the upper bound `now` keeps a result that lands during this request for the next poll
                bool Fresh(DateTime? t) => t is { } x && x > from && x <= now;
                var list = new List<(DateTime At, object Ev)>();
                foreach (var j in jobs.Where(j => j.Finished && Fresh(j.FinishedUtc) && Mine.Is(j, ip, names)))
                {
                    var a = j.AnalysisId == null ? null : st.Get(j.AnalysisId);
                    var name = JobPages.JobName(j, a == null ? null : new[] { a });
                    var (title, body) = j.State switch
                    {
                        JobState.Done => ($"✓ 완료: {name}", j.Decided is { Count: > 0 } d ? $"권장 {d.Values.Sum()}대" : ""),
                        JobState.Failed => ($"⚠ 실패: {name}", j.Error ?? j.FailureMessages.FirstOrDefault() ?? ""),
                        _ => ($"취소: {name}", ""),
                    };
                    list.Add((j.FinishedUtc!.Value, new { kind = "job", id = j.Id, state = j.State, title, body, href = "/jobs/" + j.Id, tag = $"job-{j.Id}-{j.State}" }));
                }
                var myAnalyses = jobs.Where(j => j.AnalysisId != null && Mine.Is(j, ip, names)).Select(j => j.AnalysisId!).ToHashSet(StringComparer.Ordinal);
                foreach (var f in fs.All().Where(f => f.Finished && Fresh(f.FinishedUtc) && myAnalyses.Contains(f.Request.AnalysisId)))
                {
                    var name = $"{f.AnalysisName} › 영상";
                    var (title, body) = f.State switch
                    {
                        JobState.Done => ($"✓ 완료: {name}", $"컷 {f.Done}개 완료" + (f.Failed > 0 ? $" · 실패 {f.Failed}" : "")),
                        JobState.Failed => ($"⚠ 실패: {name}", f.Error ?? ""),
                        _ => ($"취소: {name}", ""),
                    };
                    list.Add((f.FinishedUtc!.Value, new { kind = "film", id = f.Id, state = f.State, title, body, href = "/films/" + f.Id, tag = $"film-{f.Id}-{f.State}" }));
                }
                events.AddRange(list.OrderBy(x => x.At).TakeLast(20).Select(x => x.Ev));
            }
            return Results.Json(new { now = now.ToString("o", CultureInfo.InvariantCulture), events });
        });
    }
}
