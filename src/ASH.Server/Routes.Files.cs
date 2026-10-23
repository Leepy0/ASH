using System.IO.Compression;
using System.Text;
using ASH.Core;
using ASH.Core.Analysis;
using ASH.Core.Input;
using ASH.Core.Runner;

namespace ASH.Server;

internal static partial class Routes
{
    /// <summary>Per-file downloads of a job's package and continue.zip (everything needed to carry on in ZWCAD). Also backfills index.json for old jobs.</summary>
    public static void MapFiles(this WebApplication app)
    {
        var js = app.Services.GetRequiredService<JobStore>();
        var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FileIndex");
        _ = Task.Run(() => { try { FileIndex.BuildMissing(js, log); } catch (Exception e) { log.LogWarning("index.json 일괄 생성 실패: {Error}", e.Message); } });

        app.MapGet("/files", (HttpContext ctx, JobStore s, AnalysisStore an) =>
            Results.Content(FilePages.Files(FileRoutes.FileQuery.From(ctx.Request.Query), s, an), "text/html; charset=utf-8"));

        // literal names win over the parameter route /jobs/{id}/files/{name}
        app.MapGet("/jobs/{id}/files/drawing.dxf", (string id, JobStore s) => FileRoutes.Drawing(id, s, "drawing.dxf", "application/dxf"));
        app.MapGet("/jobs/{id}/files/drawing.dwg", (string id, JobStore s) => FileRoutes.Drawing(id, s, "drawing.dwg", "application/acad"));
        app.MapGet("/jobs/{id}/files/input.xlsx", (string id, JobStore s) =>
        {
            if (!JobStore.IsValidId(id) || s.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
            if (!File.Exists(Path.Combine(s.ProjectDir(id), CellDump.FileName))) return Msg(404, "이 작업에는 셀 스냅샷이 없어 input.xlsx를 되살릴 수 없습니다.");
            return Results.File(FileRoutes.RebuiltXlsx(s, j), FileRoutes.XlsxType, $"{id}-input.xlsx");
        });
        app.MapGet("/jobs/{id}/files/continue.zip", (string id, JobStore s, AnalysisStore analyses) =>
        {
            if (!JobStore.IsValidId(id) || s.Get(id) is not { } j) return Msg(404, "작업이 없습니다.");
            if (!File.Exists(Path.Combine(s.ProjectDir(id), CellDump.FileName))) return Msg(404, "이 작업에는 셀 스냅샷이 없어 이어서 작업할 자료를 만들 수 없습니다.");
            var tmp = FileRoutes.BuildContinueZip(s, analyses, j);
            return Results.File(new FileStream(tmp, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose), "application/zip", $"{id}-continue.zip");
        });
    }
}

public static class FileRoutes
{
    internal const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>"자료 받기" anchor for the status tab's version line; empty when the package has no cell snapshot.</summary>
    public static string ContinueLink(JobInfo j) =>
        j.Files.Any(f => f.Path == CellDump.FileName) ? $"<a href=\"/jobs/{JobPages.H(j.Id)}/files/continue.zip\">자료 받기</a>" : "";

    /// <summary>쿼리: q(분석·안·버전 메모·보낸 사람·모델 부분 일치) material(자재 코드 부분 일치) from/to(yyyy-MM-dd, 서울; to는 그날 끝까지, ToUtc는 배타) corp productLine line(정확히 일치, 대소문자 무시).</summary>
    public sealed record FileQuery(string Q = "", string Material = "", DateTime? FromUtc = null, DateTime? ToUtc = null, string Corp = "", string ProductLine = "", string Line = "")
    {
        public static FileQuery From(IQueryCollection q)
        {
            string S(string k) => q[k].ToString().Trim();
            DateTime? D(string k, int addDays) => DateTime.TryParseExact(S(k), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
                ? DateTime.SpecifyKind(d.AddDays(addDays) - SeoulTime.Offset, DateTimeKind.Utc) : null;
            return new FileQuery(S("q"), S("material"), D("from", 0), D("to", 1), S("corp"), S("productLine"), S("line"));
        }
    }

    static bool Has(string? text, string needle) => (text ?? "").Contains(needle, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Newest first. Jobs without an analysis never pass a tag filter.</summary>
    public static IReadOnlyList<JobInfo> Search(FileQuery f, IReadOnlyList<JobInfo> jobs, IReadOnlyList<AnalysisInfo> analyses, Func<string, IReadOnlyList<string>> materialsOf)
    {
        var byId = analyses.ToDictionary(a => a.Id, StringComparer.Ordinal);
        return jobs.Where(j =>
        {
            if (f.FromUtc is { } from && j.CreatedUtc < from) return false;
            if (f.ToUtc is { } to && j.CreatedUtc >= to) return false;
            AnalysisInfo? a = j.AnalysisId != null && byId.TryGetValue(j.AnalysisId, out var x) ? x : null;
            if (!TagMatch(a, f)) return false;
            if (f.Q.Length > 0)
            {
                var plan = a?.Plans.FirstOrDefault(p => p.Id == j.PlanId)?.Name;
                if (!(Has(a?.Name ?? j.Project, f.Q) || Has(plan ?? j.Plan, f.Q) || Has(j.VersionNote, f.Q) || Has(j.Submitter, f.Q) || Has(j.Model, f.Q))) return false;
            }
            return f.Material.Length == 0 || materialsOf(j.Id).Any(m => Has(m, f.Material));
        }).OrderByDescending(j => j.CreatedUtc).ToList();
    }

    /// <summary>A null analysis (unfiled) fails whenever a tag filter is set.</summary>
    public static bool TagMatch(AnalysisInfo? a, FileQuery f)
    {
        static bool Eq(string have, string want) => want.Length == 0 || string.Equals(have, want, StringComparison.OrdinalIgnoreCase);
        return a == null ? f.Corp.Length + f.ProductLine.Length + f.Line.Length == 0 : Eq(a.Corp, f.Corp) && Eq(a.ProductLine, f.ProductLine) && Eq(a.Line, f.Line);
    }

    /// <summary>Three tag selects filled with the existing values; shared by /files and /analyses.</summary>
    public static string TagFilterHtml(FileQuery f, AnalysisStore st)
    {
        var (corps, products, lines) = st.TagValues();
        static string Sel(string name, string label, string cur, IReadOnlyList<string> vals)
        {
            var sb = new StringBuilder($"<label>{label} <select name=\"{name}\"><option value=\"\">전체</option>");
            foreach (var v in cur.Length > 0 && !vals.Contains(cur, StringComparer.OrdinalIgnoreCase) ? vals.Append(cur) : vals)
                sb.Append($"<option value=\"{JobPages.H(v)}\"{(string.Equals(v, cur, StringComparison.OrdinalIgnoreCase) ? " selected" : "")}>{JobPages.H(v)}</option>");
            return sb.Append("</select></label> ").ToString();
        }
        return Sel("corp", "법인", f.Corp, corps) + Sel("productLine", "제품군", f.ProductLine, products) + Sel("line", "라인", f.Line, lines);
    }

    internal static IResult Drawing(string id, JobStore js, string name, string type)
    {
        if (!JobStore.IsValidId(id) || js.Get(id) is null) return Routes.Msg(404, "작업이 없습니다.");
        var path = Path.Combine(js.ProjectDir(id), name);
        if (!File.Exists(path)) return Routes.Msg(404, name.EndsWith(".dwg") ? "이 작업에는 DWG가 없습니다(0.19 이전 플러그인)." : "이 작업에는 DXF가 없습니다.");
        return Results.File(path, type, $"{id}-{name}", enableRangeProcessing: true);
    }

    /// <summary>&lt;job&gt;\input.xlsx, rebuilt from the cell snapshot; reused while it is newer than the snapshot.</summary>
    internal static string RebuiltXlsx(JobStore js, JobInfo j)
    {
        var cells = Path.Combine(js.ProjectDir(j.Id), CellDump.FileName);
        var path = Path.Combine(js.JobDir(j.Id), "input.xlsx");
        if (File.Exists(path) && File.GetLastWriteTimeUtc(path) >= File.GetLastWriteTimeUtc(cells)) return path;
        var tmp = Path.Combine(js.JobDir(j.Id), "input.tmp-" + Guid.NewGuid().ToString("N") + ".xlsx");
        try
        {
            using (var wb = CellDump.Load(cells)) { ExcelTemplate.Update(wb, null); wb.SaveAs(tmp); }
            File.Move(tmp, path, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
        return path;
    }

    internal static string BuildContinueZip(JobStore js, AnalysisStore analyses, JobInfo j)
    {
        var proj = js.ProjectDir(j.Id);
        var dwg = Path.Combine(proj, "drawing.dwg"); var dxf = Path.Combine(proj, "drawing.dxf"); var set = Path.Combine(proj, JobPackage.SettingsInPackage);
        bool hasDwg = File.Exists(dwg), hasDxf = File.Exists(dxf), hasSettings = File.Exists(set);
        var a = j.AnalysisId == null ? null : analyses.Get(j.AnalysisId);
        var p = a?.Plans.FirstOrDefault(x => x.Id == j.PlanId);
        var xlsx = RebuiltXlsx(js, j);
        var tmp = Path.Combine(js.JobDir(j.Id), "continue.zip.tmp-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite);
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: true))
            {
                void Add(string entry, string file) { if (File.Exists(file)) zip.CreateEntryFromFile(file, entry, CompressionLevel.Optimal); }
                Add("input.xlsx", xlsx);
                if (hasDwg) Add("layout.dwg", dwg); else Add("drawing.dxf", dxf);
                if (hasSettings) Add(JobPackage.SettingsInPackage, set);
                using var w = new StreamWriter(zip.CreateEntry("README.txt").Open(), new UTF8Encoding(true));
                w.Write(ReadmeText(a, p, j, hasDwg, hasSettings, hasDxf));
            }
        }
        catch { if (File.Exists(tmp)) File.Delete(tmp); throw; }
        return tmp;
    }

    internal static string ReadmeText(AnalysisInfo? a, PlanInfo? p, JobInfo j, bool hasDwg, bool hasSettings, bool hasDxf = true)
    {
        var sb = new StringBuilder();
        if (a != null && p != null) sb.Append($"이 자료는 분석 {a.Name} › 안 {p.Name} v{j.Version}.\r\n");
        else sb.Append($"이 자료는 분석에 넣지 않은 작업 {j.Id}.\r\n");
        sb.Append("ZWCAD에서 열고 AGVINIT 뒤 AGVSEND 때 같은 분석을 고르면 다음 버전, 새 안을 고르면 새 안.\r\n\r\n");
        sb.Append($"보낸 사람: {(j.Submitter.Length == 0 ? "(없음)" : j.Submitter)} · 보낸 시각: {SeoulTime.Text(j.CreatedUtc)} · 모델: {j.Model}\r\n");
        if (!hasDwg) sb.Append(hasDxf ? "DWG 없음: DXF를 넣었습니다.\r\n" : "도면 파일이 없습니다.\r\n");
        if (!hasSettings) sb.Append("ash.settings.tsv 없음: AGVINIT가 새로 만듭니다.\r\n");
        return sb.ToString();
    }
}
