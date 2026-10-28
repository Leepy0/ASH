using System.IO.Compression;
using ASH.Core.Runner;
using ASH.Server.Catalog;

namespace ASH.Server;

internal static partial class Routes
{
    /// <summary>Form field of the rerun counts: "n.&lt;group&gt;" = vehicle count of that group.</summary>
    internal const string RerunPrefix = "n.";

    /// <summary>대수 바꿔 다시 돌리기: a confirmation-only run (no search) of a finished job's model with the counts of the form, filed as the next version of the same plan.</summary>
    public static void MapRerun(this WebApplication app)
    {
        // 이어서 하기 / 처음부터 다시: a failed or cancelled job goes back in the queue as itself (mode=resume keeps the runs it finished)
        app.MapPost("/jobs/{id}/retry", async (string id, HttpContext ctx, JobStore js, ReleaseStore rs, IConfiguration cfg) =>
        {
            if (!AcceptsJobs(cfg)) return Msg(403, "이 서버는 작업을 받지 않습니다(Jobs:Enabled=false).");
            if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
            if (CrossSiteBrowser(ctx)) return Msg(403, CrossSite);
            var form = await FormFrom(ctx);
            var token = form?["token"].ToString();
            if (!CanCancel(ctx, j, rs, string.IsNullOrEmpty(token) ? null : token)) return Msg(403, "보낸 PC나 관리자만 다시 실행할 수 있습니다.");
            if (!js.Requeue(id, form?["mode"].ToString() != "restart")) return Msg(409, "실패했거나 취소된 서버 작업만 다시 실행할 수 있습니다.");
            return Results.Redirect($"/jobs/{id}");
        });

        app.MapPost("/jobs/{id}/rerun", async (string id, HttpContext ctx, JobStore js, AnalysisStore analyses, ReleaseStore rs, AssetStore assets, ServerUpdater updater, IConfiguration cfg, CancellationToken ct) =>
        {
            if (!AcceptsJobs(cfg)) return Msg(403, "이 서버는 작업을 받지 않습니다(Jobs:Enabled=false).");
            if (!JobStore.IsValidId(id) || js.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
            if (CrossSiteBrowser(ctx)) return Msg(403, CrossSite);
            if (await FormFrom(ctx) is not { } form) return Msg(400, "폼 데이터가 필요합니다.");
            var token = form["token"].ToString();
            if (!CanCancel(ctx, j, rs, string.IsNullOrEmpty(token) ? null : token)) return Msg(403, "보낸 PC나 관리자만 다시 돌릴 수 있습니다.");
            if (j.State != JobState.Done || j.Decided == null) return Msg(409, "결론 대수가 나온 완료 작업에서만 다시 돌릴 수 있습니다.");
            var view = JobView.For(js, j);
            if (view.Input == null) return Msg(409, "입력을 읽지 못해 그룹 범위를 알 수 없습니다: " + view.InputError);

            // the decided counts, with every n.<group> field laid over them
            var counts = view.Input.Groups.ToDictionary(g => g.Group, g => j.Decided.TryGetValue(g.Group, out var d) ? d : g.MinCount, StringComparer.Ordinal);
            foreach (var key in form.Keys.Where(k => k.StartsWith(RerunPrefix, StringComparison.Ordinal)))
            {
                var name = key[RerunPrefix.Length..];
                var g = view.Input.Groups.FirstOrDefault(x => x.Group == name);
                if (g == null) return Msg(400, "그룹이 없습니다: " + name);
                if (!int.TryParse(form[key].ToString().Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
                    return Msg(400, $"{name}: 대수는 정수여야 합니다.");
                if (n < g.MinCount || n > g.MaxCount) return Msg(400, $"{name}: {n}대는 범위 {g.MinCount}~{g.MaxCount}대 밖입니다.");
                counts[name] = n;
            }
            var changed = view.Input.Groups.Where(g => counts[g.Group] != (j.Decided.TryGetValue(g.Group, out var d) ? d : g.MinCount)).ToList();
            if (changed.Count == 0) return Msg(400, "바뀐 대수가 없습니다.");
            var note = "대수 변경 확인: " + string.Join(", ", changed.Select(g => $"{g.Group} {(j.Decided.TryGetValue(g.Group, out var d) ? d : g.MinCount)}→{counts[g.Group]}"));
            if (note.Length > NoteMax) note = note[..(NoteMax - 1)] + "…";

            if (!File.Exists(js.UploadPath(id))) return Msg(410, "보낸 파일(upload.zip)이 없어 다시 돌릴 수 없습니다.");
            var nid = JobStore.NewId();
            var dir = js.JobDir(nid);
            Directory.CreateDirectory(dir);
            try
            {
                File.Copy(js.UploadPath(id), js.UploadPath(nid));
                // a local result upload carries ash-run's runs\ too: the copy keeps the model package only
                using (var zip = ZipFile.Open(js.UploadPath(nid), ZipArchiveMode.Update))
                    foreach (var e in zip.Entries.Where(e => e.FullName.Replace('\\', '/').StartsWith("runs/", StringComparison.OrdinalIgnoreCase)).ToList()) e.Delete();
                var files = JobPackage.Extract(js.UploadPath(nid), js.ProjectDir(nid), j.Model);
                // a source package has no .arc: the current generator makes the model again
                var generated = ServerModelGen.Run(js.ProjectDir(nid), j.Model, assets, updater.Current, js.LogPath(nid));
                var job = new JobInfo
                {
                    Id = nid, Model = j.Model, Project = j.Project, Plan = j.Plan, Submitter = j.Submitter, Ip = ClientIp(ctx),
                    CreatedUtc = DateTime.UtcNow, Impact = false, InitialReps = 1, Files = files.ToList(), UploadBytes = new FileInfo(js.UploadPath(nid)).Length,
                    FixedCounts = counts, VersionNote = note, ServerGenerated = generated,
                };
                js.Add(job);
                // a plan deleted meanwhile leaves the new job unclassified (it is already queued)
                if (j.AnalysisId != null && j.PlanId != null) try { analyses.Assign(js, nid, j.AnalysisId, j.PlanId, note); } catch (ArgumentException) { }
                return Results.Redirect($"/jobs/{nid}");
            }
            catch (Exception e) when (e is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
            {
                try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return Msg(400, "다시 돌릴 작업을 만들지 못했습니다: " + e.Message);
            }
        });
    }
}
