using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using ASH.Core.Cause;
using ASH.Core.Film;
using ASH.Core.Geometry;
using ASH.Core.Layout;
using ASH.Server;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

public class CausePagesTests
{
    static Layout Lay()
    {
        var l = new Layout();
        var p = new LayoutPath("P1"); p.Segments.Add(new LineSeg(new Point2(0, 0), new Point2(40, 0))); l.Paths.Add(p);
        l.Blocks.Add(new LayoutBlock { Name = "B005", Cap = 1, Rect = new Rect2(new Point2(10, -2), new Point2(14, 2)) });
        return l;
    }

    static readonly CauseItem[] Items =
    {
        new(CauseKinds.TimeLoss, 0, "대기 많음 G1", "G1", "block 대기 30%", ""),
        new(CauseKinds.Deadlock, 2, "교착 <B005>·B007", "B005", "V3 대기 B007 · 점유 B005", "U003 출구 쪽 1.5 m 이동", 3725, 12, 0),
        new(CauseKinds.DeadlockRisk, 1, "교착 위험 B001·B002", "B001", "사이 대기 공간 없음", "B002 줄이기"),
        new(CauseKinds.Detour, 0, "우회 L003→U001", "L003→U001", "직선의 2.4배", "", null, 30, 0),
    };

    static string Page(IReadOnlyList<CauseItem> items, IReadOnlyList<TimeBreakdown>? bd = null, Layout? l = null, string note = "")
    {
        var sb = new StringBuilder(); CausePages.Render(sb, "a-1", "j-1", items, bd ?? Array.Empty<TimeBreakdown>(), l, note); return sb.ToString();
    }

    [Fact] public void Groups_Follow_Kind_Order_With_Korean_Names_And_Escaped_Text()
    {
        var html = Page(Items, null, Lay());
        var at = new[] { "<h3>교착<span>", "<h3>교착 위험<span>", "<h3>우회<span>", "<h3>시간 손실<span>" }.Select(s => html.IndexOf(s, StringComparison.Ordinal)).ToList();
        Assert.All(at, i => Assert.True(i >= 0)); Assert.Equal(at.OrderBy(i => i), at);
        Assert.DoesNotContain("block 점유", html); // no finding of that kind: no heading
        Assert.Contains("교착 &lt;B005&gt;&#183;B007", html); Assert.DoesNotContain("<B005>", html); // H() encodes the middle dot
        foreach (var s in new[] { "tag red\">심각", "tag orange\">주의", "tag gray\">참고", "<em>조치</em>U003 출구 쪽 1.5 m 이동", ">1:02:05<" }) Assert.Contains(s, html);
        Assert.Equal(2, Regex.Matches(html, "<em>조치</em>").Count); // the deadlock and the risk have a fix
    }

    [Fact] public void Cut_Button_Needs_A_Time_And_A_Place_And_Markers_Need_A_Place()
    {
        var html = Page(Items, null, Lay());
        var forms = Regex.Matches(html, "<form method=\"post\" action=\"/analyses/a-1/cuts/cause\">").Count;
        Assert.Equal(1, forms); // the deadlock only: the detour has a place but no time
        Assert.Contains("name=\"x\" value=\"12\"", html); Assert.Contains("name=\"at\" value=\"3725\"", html); Assert.Contains(">영상 컷</button>", html);
        Assert.Equal(2, Regex.Matches(html, "class=\"mk cm\"").Count); // deadlock and detour
        Assert.Contains("class=\"cz map\"", html); Assert.Equal(2, Regex.Matches(html, "<li class=\"cz-r\" data-i=").Count);
        Assert.Contains("svg class=\"bm\"", html); Assert.Contains("<title>B005</title>", html);
    }

    [Fact] public void Rows_With_Refs_Expand_To_A_Zoomed_Map_Of_Their_Paths_Cps_And_Blocks()
    {
        var l = Lay(); l.Cps.Add(new LayoutCp { Name = "U003", Type = "unload", Position = new Point2(12, 0) });
        var items = new CauseItem[]
        {
            new(CauseKinds.BlockHold, 1, "B005 안 정차", "B005", "d", "", null, 12, 0, new[] { "B005", "U003", "P1", "nope" }),
            new(CauseKinds.Detour, 1, "우회", "A→B", "d", "", null, 30, 0, new[] { "nope" }),
            new(CauseKinds.TimeLoss, 0, "대기", "G1", "d", ""),
        };
        var html = Page(items, null, l);
        Assert.Equal(1, Regex.Matches(html, "data-ex=\"1\"").Count); // only the row whose refs resolve
        Assert.Equal(1, Regex.Matches(html, "class=\"cz-s\"").Count);
        foreach (var s in new[] { "경로 P1", "CP U003", "block B005", "stroke=\"#007aff\" stroke-width=\"6\"", "fill=\"#d70015\"", "stroke=\"#ff9500\"" }) Assert.Contains(s, html);
        Assert.DoesNotContain("nope</", html);
        Assert.Contains("class=\"mk cm\"", html); // the overview map stays
    }

    [Fact] public void Without_A_Layout_Or_Places_There_Is_No_Map()
    {
        Assert.DoesNotContain("class=\"bm\"", Page(Items, null, null));
        Assert.DoesNotContain("class=\"bm\"", Page(new[] { Items[0], Items[2] }, null, Lay()));
    }

    [Fact] public void Empty_State_And_Note()
    {
        var html = Page(Array.Empty<CauseItem>(), null, Lay(), "실행 자료 없음");
        Assert.Contains("원인 없음", html); Assert.Contains("실행 자료 없음", html); Assert.DoesNotContain("<h3>", html); Assert.DoesNotContain("영상 컷", html);
    }

    [Fact] public void Breakdown_Is_A_Stacked_Bar_And_A_Row_Per_Group()
    {
        var html = Page(Array.Empty<CauseItem>(), new[] { new TimeBreakdown("G1", 40, 100, 60, 20, 10, 5, 5), new TimeBreakdown("G2", 10, 50, 50, 0, 0, 0, 0) });
        foreach (var s in new[] { "작업당 시간", "<td>G1</td><td>40</td>", "<td>G2</td><td>10</td>", "<th>block 대기</th>", "<th>CP 대기</th>", "<th>상하차</th>", "<th>기타</th>", "u-ld", "u-bx", "u-rp", "u-hd", "u-id" }) Assert.Contains(s, html);
        Assert.Contains("class=\"u-ld\" style=\"width:60%\"", html); // 60 of the longest job (100 s)
        Assert.Contains("class=\"u-ld\" style=\"width:50%\"", html);
        Assert.Contains("<td class=\"num\"><b>100</b></td>", html);
    }

    [Fact] public void Hms_And_CutFor()
    {
        Assert.Equal("0:00:00", CausePages.Hms(-5)); Assert.Equal("1:01:01", CausePages.Hms(3661.4)); Assert.Equal("25:00:00", CausePages.Hms(90000));
        var c = CausePages.CutFor(" 교착 B005 ", 12.5, -3, 3725, "c1");
        Assert.Equal(CutKind.Hold, c.Kind); Assert.Equal("교착 B005", c.Name); Assert.Equal(3665, c.StartSec); Assert.Equal(120, c.HoldSec);
        Assert.Equal(new Rect(-2.5, -18, 30, 30), c.A);
        Assert.Equal(0, CausePages.CutFor("x", 0, 0, 20, "c1").StartSec); // never before 0
        Assert.Equal(60, CausePages.CutFor(new string('가', 90), 0, 0, 20, "c1").Name.Length);
        Assert.Null(CutStore.Validate(new[] { c })); // the editor and the save API accept it
    }

    // JobView of a finished fake job has no layout (no .arc): the page is patched like ConclusionTests does
    static void Set(JobView v, string prop, object? value) => typeof(JobView).GetProperty(prop)!.SetValue(v, value);

    [Fact] public async Task Tab_Renders_For_A_Job_Without_And_With_A_Layout()
    {
        using var env = new JobEnv(); var id = await env.SendFiled("라인 증설", "A안"); await env.WaitFinished(id);
        var aid = env.Factory.Services.GetRequiredService<AnalysisStore>().All().Single().Id;
        var first = await env.Http.GetStringAsync($"/analyses/{aid}?tab=cause");
        Assert.Contains("?tab=cause\" class=\"on\" aria-current=\"page\">원인</a>", first); Assert.Contains("name=\"job\"", first);
        var v = JobView.For(env.Store, env.Store.Get(id)!);
        Set(v, "Layout", Lay());
        var html = await env.Http.GetStringAsync($"/analyses/{aid}?tab=cause&job={id}&run=nope");
        Assert.Contains("원인 없음", html); // the analysers are empty here: the state, not an error
        Assert.DoesNotContain("분석 오류", html);
        Set(v, "Layout", null);
        Assert.Contains("자료 없음", await env.Http.GetStringAsync($"/analyses/{aid}?tab=cause"));
    }

    // ---------------------------------------------------------------- the cut endpoint

    static FormUrlEncodedContent Form(params (string, string)[] kv) => new(kv.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2)));

    [Fact] public async Task Cause_Cut_Is_Added_To_The_List_And_The_Editor_Opens()
    {
        using var env = new JobEnv(worker: false);
        var st = env.Factory.Services.GetRequiredService<AnalysisStore>(); var cs = env.Factory.Services.GetRequiredService<CutStore>();
        var a = st.CreateAnalysis("분석", "");
        var http = env.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var r = await http.PostAsync($"/analyses/{a.Id}/cuts/cause", Form(("job", "j 1"), ("name", "교착 B005"), ("x", "12.5"), ("y", "-3"), ("at", "3725")));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal($"/analyses/{a.Id}/cuts?job=j%201", r.Headers.Location!.OriginalString);
        var c = Assert.Single(cs.Get(a.Id));
        Assert.Equal(CutKind.Hold, c.Kind); Assert.Equal("교착 B005", c.Name); Assert.Equal(3665, c.StartSec); Assert.Equal(120, c.HoldSec);
        Assert.Equal(new Rect(-2.5, -18, 30, 30), c.A);
        await http.PostAsync($"/analyses/{a.Id}/cuts/cause", Form(("name", "둘째"), ("x", "0"), ("y", "0"), ("at", "10")));
        Assert.Equal(new[] { "교착 B005", "둘째" }, cs.Get(a.Id).Select(x => x.Name)); // appended after the others
    }

    [Fact] public async Task Cause_Cut_Rejects_Bad_Input_And_Other_Sites()
    {
        using var env = new JobEnv(worker: false);
        var st = env.Factory.Services.GetRequiredService<AnalysisStore>(); var cs = env.Factory.Services.GetRequiredService<CutStore>();
        var a = st.CreateAnalysis("분석", "");
        var good = new[] { ("name", "n"), ("x", "1"), ("y", "2"), ("at", "100") };
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Http.PostAsync($"/analyses/{a.Id}/cuts/cause", Form(("name", "n"), ("x", "abc"), ("y", "2"), ("at", "1")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Http.PostAsync($"/analyses/{a.Id}/cuts/cause", Form(("name", "n"), ("x", "1"), ("y", "2")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await env.Http.PostAsync($"/analyses/{a.Id}/cuts/cause", Form(("name", "n"), ("x", "NaN"), ("y", "2"), ("at", "1")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.PostAsync("/analyses/a-nope0000/cuts/cause", Form(good))).StatusCode);
        var evil = new HttpRequestMessage(HttpMethod.Post, $"/analyses/{a.Id}/cuts/cause") { Content = Form(good) };
        evil.Headers.Add("Origin", "http://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await env.Http.SendAsync(evil)).StatusCode);
        Assert.Empty(cs.Get(a.Id));
        // a full list is a 400 with the reason, the saved list stays
        cs.Save(a.Id, Enumerable.Range(0, CutStore.MaxCuts).Select(i => CausePages.CutFor("c" + i, 0, 0, 100, "c" + i)).ToList());
        var full = await env.Http.PostAsync($"/analyses/{a.Id}/cuts/cause", Form(good));
        Assert.Equal(HttpStatusCode.BadRequest, full.StatusCode); Assert.Contains("컷은 200개까지", await full.Content.ReadAsStringAsync());
        Assert.Equal(CutStore.MaxCuts, cs.Get(a.Id).Count);
    }
}
