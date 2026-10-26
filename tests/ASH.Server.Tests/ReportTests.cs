using System.Net;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

/// <summary>The analysis report: the print-ready page /analyses/{id}/report and the PowerPoint /analyses/{id}/report.pptx.</summary>
public class ReportTests
{
    const string FilmId = "20260101-000001-bbbb";

    /// <summary>A finished job filed under "라인 증설" › A안 and a finished film of it with a cut file and a thumbnail; returns (analysis id, job id).</summary>
    static async Task<(string Aid, string Jid)> Make(JobEnv env)
    {
        var jid = (await env.WaitFinished(await env.SendFiled("라인 증설", "A안"))).Id;
        var aid = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id;
        var fs = env.Factory.Services.GetRequiredService<FilmStore>();
        fs.Add(new FilmInfo
        {
            Request = new FilmRequest(FilmId, aid, new[] { "c1" }, new[] { jid }, 1280, 720, 15, "each", true, null, 0, null),
            State = JobState.Done, CreatedUtc = DateTime.UtcNow, Items = { new FilmItem { JobId = jid, CutId = "c1", Label = "입구", Name = "c1.mp4", Ok = true } },
        });
        File.WriteAllText(Path.Combine(fs.FilmDir(FilmId), "c1.mp4"), "x");
        File.WriteAllBytes(Path.Combine(fs.FilmDir(FilmId), "thumb.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
        return (aid, jid);
    }

    [Fact] public async Task Report_Page_Has_Every_Section_And_Print_Css()
    {
        using var env = new JobEnv(); var (aid, jid) = await Make(env);
        var html = await env.Http.GetStringAsync($"/analyses/{aid}/report");
        Assert.Equal(5, Regex.Matches(html, "<section class=\"rs").Count); // cover+결론, 그룹, 자재, 병목, 영상: a page each when printed
        foreach (var s in new[] { "<h1>라인 증설</h1>", "기준 버전 <b>A안 v1</b>", ">권장 대수<", ">최대 가동률<", ">자재 품절<", "<h3>안별 결론</h3>", "<h2>그룹별 권장 대수</h2>",
            "<h2>자재별 공급", "<h2>병목 지도", "<svg class=\"bm\"", "<div class=\"rk\">", "<h2>영상", $"<img src=\"/films/{FilmId}/files/thumb.jpg\"", $"/films/{FilmId}/files/c1.mp4\">c1.mp4</a>",
            "@page{size:A4 landscape", "break-before:page", ".side,.noprint", "onclick=\"window.print()\">PDF로 저장", $"href=\"/analyses/{aid}/report.pptx?job={jid}\"", $"href=\"/jobs/{jid}/files/report.xlsx\"" })
            Assert.Contains(s, html);
        Assert.Single(Regex.Matches(html, "class=\"(btn pri|primary)\"")); // one blue button
        Assert.Matches("<tr class=\"cur( fail| over)?\"><td>A안</td>", html); // the version the report is about, in 안별 결론

        // the detail page and the home lane lead here
        Assert.Contains($"href=\"/analyses/{aid}/report\">✓ 보고서 받기", await env.Http.GetStringAsync($"/analyses/{aid}"));
    }

    [Fact] public async Task Pptx_Opens_With_Sections_Charts_And_Appendix()
    {
        using var env = new JobEnv(); var (aid, jid) = await Make(env);
        await env.WaitFinished(await env.SendFiled("라인 증설", "B안")); // a second plan: the 안별 비교 slide
        var res = await env.Http.GetAsync($"/analyses/{aid}/report.pptx?job={jid}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.presentationml.presentation", res.Content.Headers.ContentType?.MediaType);
        using var ms = new MemoryStream(await res.Content.ReadAsByteArrayAsync());
        using var doc = PresentationDocument.Open(ms, false);
        Assert.Empty(new OpenXmlValidator().Validate(doc).Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}"));
        var pp = doc.PresentationPart!;
        var slides = pp.Presentation.SlideIdList!.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().Select(s => (SlidePart)pp.GetPartById(s.RelationshipId!)).ToList();
        string Text(SlidePart s) => string.Concat(s.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>().Select(t => t.Text));
        var texts = slides.Select(Text).ToList();

        // cover (name + headline numbers), then the sections in order, the detail tables last
        Assert.Contains("라인 증설", texts[0]); Assert.Contains("권장 대수", texts[0]); Assert.Contains("기준 버전  A안 v1", texts[0]);
        int at = 0;
        foreach (var label in new[] { "01 · 요약", "02 · 안별 비교", "· 그룹별 부하", "· 병목", "· 영상", "부록 · " })
        {
            at = texts.FindIndex(at, t => t.Contains(label));
            Assert.True(at > 0, $"no slide '{label}' in order");
        }
        Assert.Contains("핵심 발견", texts[1]);
        Assert.Contains($"2 / {slides.Count}", texts[1]); // footer: page n/N

        // native charts with their data workbook: two on 안별 비교 (A안 and B안), one on 그룹별 부하
        var compare = slides[texts.FindIndex(t => t.Contains("02 · 안별 비교"))];
        Assert.Equal(2, compare.ChartParts.Count());
        Assert.All(compare.ChartParts, c => Assert.Contains("B안", c.ChartSpace.InnerText));
        Assert.Single(slides[texts.FindIndex(t => t.Contains("· 그룹별 부하"))].ChartParts);
        Assert.All(slides.SelectMany(s => s.ChartParts), c => Assert.NotNull(c.EmbeddedPackagePart));

        var film = slides[texts.FindIndex(t => t.Contains("· 영상"))];
        Assert.Single(film.ImageParts); // the film's thumbnail
        Assert.Contains(film.HyperlinkRelationships, h => h.Uri.ToString().EndsWith($"/films/{FilmId}", StringComparison.Ordinal));
    }

    [Fact] public async Task Unknown_Analysis_Is_404_And_No_Result_Says_So()
    {
        using var env = new JobEnv(worker: false);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync("/analyses/nope/report")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync("/analyses/nope/report.pptx")).StatusCode);
        var a = env.Factory.Services.GetRequiredService<AnalysisStore>().CreateAnalysis("빈 분석", "");
        Assert.Contains("결과가 나온 버전이 없습니다", await env.Http.GetStringAsync($"/analyses/{a.Id}/report"));
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync($"/analyses/{a.Id}/report.pptx")).StatusCode);
    }
}
