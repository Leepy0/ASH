namespace ASH.Server;

internal static partial class Routes
{
    /// <summary>An analysis' report: the print-ready page (?job= picks the version, ?map=all the whole layout) and the same report as PowerPoint.</summary>
    public static void MapReport(this WebApplication app)
    {
        static AnalysisPageData? DataOf(string id, AnalysisStore st, JobStore js, FilmStore fs, CatalogStore cat) =>
            st.Get(id) is not { } a ? null
                : new AnalysisPageData(a, js.All().Where(j => j.AnalysisId == id).ToList(), fs.All().Where(f => f.Request.AnalysisId == id).ToList(), js, default, 1, 0, fs, cat.Prices());

        app.MapGet("/analyses/{id}/report", (string id, string? job, string? map, AnalysisStore st, JobStore js, FilmStore fs, CatalogStore cat) =>
            DataOf(id, st, js, fs, cat) is { } d ? Html(ReportPages.Page(d.A, ReportPages.Data(d, job), map == "all")) : Msg(404, "분석이 없습니다."));

        app.MapGet("/analyses/{id}/report.pptx", (string id, string? job, HttpContext ctx, AnalysisStore st, JobStore js, FilmStore fs, CatalogStore cat) =>
        {
            if (DataOf(id, st, js, fs, cat) is not { } d) return Msg(404, "분석이 없습니다.");
            if (ReportPages.Data(d, job) is not { } r) return Msg(404, "결과가 나온 버전이 없습니다.");
            var bytes = ReportPptx.Build(r, $"{ctx.Request.Scheme}://{ctx.Request.Host}");
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                $"{ASH.Core.Analysis.AnalysisNames.SafeFileName(d.A.Name)}-보고서.pptx");
        });
    }
}
