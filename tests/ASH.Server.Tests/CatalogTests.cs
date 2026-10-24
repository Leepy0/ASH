using System.Text.Json;
using ASH.Core.Catalog;
using Microsoft.Extensions.DependencyInjection;

namespace ASH.Server.Tests;

public class CatalogTests
{
    static CatalogType T(string name, bool hidden = false) => new()
    {
        Name = name, Label = name + " label", Category = "AGV", Kind = "Standard", Hidden = hidden,
        Defaults = new CatalogDefaults { SpeedLoadedStraight = 1.5, Capacity = 1 }, Assets = { "front" },
    };

    static CatalogStore Store(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "ash-cat-test-" + Guid.NewGuid().ToString("N"));
        return new CatalogStore(dir, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
    }

    [Fact]
    public void AddAsset_RemoveAsset_Edit_Type_Assets()
    {
        var s = Store(out _);
        Assert.Null(s.Save(T("T_A"), "me"));
        Assert.True(s.AddAsset("T_A", "agv_a"));
        Assert.False(s.AddAsset("T_A", "AGV_A"));
        Assert.False(s.AddAsset("T_none", "x"));
        Assert.Equal(new[] { "front", "agv_a" }, s.Get("T_A")!.Assets);
        Assert.True(s.RemoveAsset("T_A", "agv_a"));
        Assert.False(s.RemoveAsset("T_A", "agv_a"));
        Assert.Equal(new[] { "front" }, s.Get("T_A")!.Assets);
        Assert.True(s.RemoveAsset("T_A", "front"));
        Assert.Empty(s.Get("T_A")!.Assets);
    }

    [Fact]
    public void Save_Then_Types_RoundTrips_And_Hides_Hidden()
    {
        var s = Store(out _);
        Assert.Null(s.Save(T("T_A"), "me"));
        Assert.False(File.Exists(Path.Combine(s.Dir, "types.json.bak")));
        Assert.Null(s.Save(T("T_B", hidden: true), "me"));
        Assert.Equal(new[] { "T_A" }, s.Types().Select(t => t.Name));
        Assert.Equal(new[] { "T_A", "T_B" }, s.Types(true).Select(t => t.Name));
        Assert.True(File.Exists(Path.Combine(s.Dir, "types.json.bak")));
        Assert.Equal("me", s.Get("T_A")!.UpdatedBy);
    }

    [Fact]
    public void Save_Rejects_Bad_Input()
    {
        var s = Store(out _);
        Assert.NotNull(s.Save(T("한글"), "me"));
        var k = T("T_K"); k.Kind = "Nope"; Assert.NotNull(s.Save(k, "me"));
        var c = T("T_C"); c.Defaults.Capacity = 0; Assert.NotNull(s.Save(c, "me"));
        Assert.Empty(s.Types(true));
    }

    [Fact]
    public void SavePrices_Rejects_Whole_File_On_Bad_Row()
    {
        var s = Store(out _);
        s.Save(T("T_A"), "me"); s.Save(T("T_B"), "me");
        var bad = s.SavePrices("Type,UnitPrice,Currency,Note\nT_A,1000,KRW,\nT_X,abc,,");
        Assert.Equal(0, bad.Changed);
        Assert.Contains(bad.Errors, e => e.Row == 3 && e.Why == "타입 T_X 없음");
        Assert.Contains(bad.Errors, e => e.Row == 3 && e.Why == "숫자가 아닙니다");
        Assert.Empty(s.Prices());
        var ok = s.SavePrices("Type,UnitPrice,Currency,Note\nT_A,\"1,000\",,a note\nT_B,2000,KRW,");
        Assert.Equal(2, ok.Changed);
        Assert.Equal(1000m, s.Prices()["T_A"].UnitPrice);
        Assert.Equal("KRW", s.Prices()["T_A"].Currency);
        Assert.Equal("KRW", s.Prices()["T_B"].Currency);
        Assert.Contains(s.SavePrices("Type,UnitPrice,Currency,Note\nT_A,1,USD,").Errors, e => e.Why.Contains("KRW"));
    }

    [Fact]
    public void SavePrices_Rejects_Negative_Price()
    {
        var s = Store(out _);
        s.Save(T("T_A"), "me");
        Assert.Contains(s.SavePrices("Type,UnitPrice,Currency,Note\nT_A,-5,KRW,").Errors, e => e.Row == 2 && e.Why == "음수 단가");
        Assert.Empty(s.Prices());
    }

    [Fact]
    public void Load_Drops_Entries_Without_Name()
    {
        var s = Store(out _);
        File.WriteAllText(Path.Combine(s.Dir, "types.json"), "{\"types\":[{\"name\":null},{\"label\":\"x\"},null,{\"name\":\"T_A\",\"defaults\":{}}]}");
        Assert.Equal(new[] { "T_A" }, s.Types(true).Select(t => t.Name));
        Assert.Null(s.Get("nope"));
    }

    [Fact]
    public async Task Api_Types_Has_Stable_Shape()
    {
        using var env = new JobEnv();
        var store = env.Factory.Services.GetRequiredService<CatalogStore>();
        Assert.Null(store.Save(T("T_A"), "me"));
        Assert.Null(store.Save(T("T_H", hidden: true), "me"));
        var res = await env.Http.GetAsync("/api/catalog/types");
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var types = doc.RootElement.GetProperty("types");
        Assert.Equal(1, types.GetArrayLength());
        var x = types[0];
        Assert.Equal("T_A", x.GetProperty("name").GetString());
        Assert.Equal("T_A label", x.GetProperty("label").GetString());
        Assert.Equal("AGV", x.GetProperty("category").GetString());
        Assert.Equal("Standard", x.GetProperty("kind").GetString());
        Assert.Equal(1.5, x.GetProperty("defaults").GetProperty("speedLoadedStraight").GetDouble());
        Assert.Equal("front", x.GetProperty("assets")[0].GetString());
        Assert.Equal(new[] { "assets", "category", "defaults", "kind", "label", "name" }, x.EnumerateObject().Select(p => p.Name).OrderBy(n => n));
        Assert.False(x.TryGetProperty("hidden", out _) || x.TryGetProperty("updatedUtc", out _) || x.TryGetProperty("updatedBy", out _));
    }

    [Fact]
    public async Task Api_Empty_Catalog_Is_Exactly_Empty_Types()
    {
        using var env = new JobEnv();
        Assert.Equal("{\"types\":[]}", await env.Http.GetStringAsync("/api/catalog/types"));
    }

    [Fact]
    public async Task Admin_Types_Needs_Login_And_Saves_With_Header_Token()
    {
        const string Token = "test!Admin12";
        using var env = new JobEnv(worker: false, adminToken: Token);
        var store = env.Factory.Services.GetRequiredService<CatalogStore>();
        var get = await env.Http.GetAsync("/admin/types");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, get.StatusCode);
        Assert.Contains("/admin/login", await get.Content.ReadAsStringAsync());

        async Task<HttpResponseMessage> Post(string url, params (string, string)[] f)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(f.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2))) };
            req.Headers.Add(ReleaseStore.HeaderName, Token);
            return await env.Http.SendAsync(req);
        }
        (string, string)[] Form(string name) => new[] { ("name", name), ("label", "L"), ("category", "AGV"), ("kind", "Standard"), ("speedLoadedStraight", "1"), ("speedLoadedCurve", "0.5"),
            ("speedEmptyStraight", "1"), ("speedEmptyCurve", "0.5"), ("accel", "0.5"), ("decel", "0.5"), ("capacity", "1"), ("loadTime", "0"), ("unloadTime", "0"), ("mtbf", "0"), ("mttr", "0"),
            ("length", "1"), ("width", "1"), ("color", ""), ("unitPrice", "1200") };

        Assert.Equal(System.Net.HttpStatusCode.OK, (await Post("/admin/types", Form("T_A"))).StatusCode);
        Assert.Equal(1, store.Get("T_A")!.Defaults.SpeedLoadedStraight);
        Assert.Equal(1200m, store.Prices()["T_A"].UnitPrice);

        var bad = await Post("/admin/types", Form("한글"));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("AutoMod", await bad.Content.ReadAsStringAsync());

        var badNum = Form("T_B").Select(x => x.Item1 == "width" ? ("width", "abc") : x).ToArray();
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, (await Post("/admin/types", badNum)).StatusCode);
        Assert.Null(store.Get("T_B"));

        var pr = await Post("/admin/types/prices", ("csv", "Type,UnitPrice,Currency,Note\nT_A,1,,\nT_X,2,,"));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, pr.StatusCode);
        Assert.Contains("3행", await pr.Content.ReadAsStringAsync());

        var page = await env.Http.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/admin/types?edit=T_A") { Headers = { { ReleaseStore.HeaderName, Token } } });
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("id=\"assets\"", html);
        Assert.Contains("T_A", html);
    }

    [Fact]
    public async Task Template_Round_Trips_And_A_Paste_Saves_All_Rows_Or_None()
    {
        const string Token = "test!Admin12";
        using var env = new JobEnv(worker: false, adminToken: Token);
        var store = env.Factory.Services.GetRequiredService<CatalogStore>();
        store.Save(T("T_OLD"), "me"); store.AddAsset("T_OLD", "front");
        async Task<HttpResponseMessage> Send(HttpMethod m, string url, string? rows = null)
        {
            var req = new HttpRequestMessage(m, url) { Content = rows == null ? null : new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("rows", rows) }) };
            req.Headers.Add(ReleaseStore.HeaderName, Token);
            return await env.Http.SendAsync(req);
        }

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await env.Http.GetAsync("/admin/types/template.xlsx")).StatusCode);
        var x = await Send(HttpMethod.Get, "/admin/types/template.xlsx");
        Assert.Equal(System.Net.HttpStatusCode.OK, x.StatusCode);
        using (var wb = new ClosedXML.Excel.XLWorkbook(await x.Content.ReadAsStreamAsync()))
        {
            var ws = wb.Worksheet("타입");
            Assert.Equal("이름", ws.Cell(1, 1).GetString());
            Assert.Equal("T_OLD", ws.Cell(2, 1).GetString());
            Assert.Equal(CatalogSheet.HeaderLine, string.Join("\t", ws.Row(1).CellsUsed().Select(c => c.GetString())));
        }

        // a bad row (unknown kind) and the good one beside it: nothing saved
        var head = CatalogSheet.HeaderLine;
        var bad = await Send(HttpMethod.Post, "/admin/types/paste", head + "\r\nT_NEW\t새 차\tAGV\tNoSuchKind\t1\t0.5\t1\t0.5\t0.3\t0.3\t2\t5\t5\t0\t0\t1.2\t0.8\t\t\t\r\n");
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("2행", await bad.Content.ReadAsStringAsync());
        Assert.Null(store.Get("T_NEW"));

        // Excel's copy: tab-separated, CRLF; a Korean kind label; a price with a thousands comma; an update that keeps the assets
        var ok = await Send(HttpMethod.Post, "/admin/types/paste",
            head + "\r\nT_NEW\t새 차\tAGV\t표준\t1\t0.5\t1\t0.5\t0.3\t0.3\t2\t5\t5\t0\t0\t1.2\t0.8\t\t15,000,000\t\r\n" +
                   "T_OLD\t바뀜\tAGV\tStandard\t2\t1\t2\t1\t0.5\t0.5\t1\t0\t0\t0\t0\t1\t1\t\t\tY\r\n");
        Assert.Equal(System.Net.HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("타입 2개를 저장했습니다(단가 1개 포함)", await ok.Content.ReadAsStringAsync());
        Assert.Equal(2, store.Get("T_NEW")!.Defaults.Capacity);
        Assert.Equal("Standard", store.Get("T_NEW")!.Kind);
        Assert.Equal(15_000_000m, store.Prices()["T_NEW"].UnitPrice);
        Assert.Equal("바뀜", store.Get("T_OLD")!.Label);
        Assert.True(store.Get("T_OLD")!.Hidden);
        Assert.Equal(new[] { "front" }, store.Get("T_OLD")!.Assets);
        using (var wb = new ClosedXML.Excel.XLWorkbook(await (await Send(HttpMethod.Get, "/admin/types/template.xlsx")).Content.ReadAsStreamAsync()))
        {
            var row = wb.Worksheet("타입").RowsUsed().Single(r => r.Cell(1).GetString() == "T_NEW");
            Assert.Equal(15_000_000, row.Cell(CatalogSheet.HeaderLine.Split('\t').ToList().IndexOf("단가(원)") + 1).GetDouble()); // the price comes back with its type
        }

        // only some columns: the rest of an existing type stays
        Assert.Equal(System.Net.HttpStatusCode.OK, (await Send(HttpMethod.Post, "/admin/types/paste", "이름\t라벨\nT_NEW\t짧게")).StatusCode);
        Assert.Equal("짧게", store.Get("T_NEW")!.Label);
        Assert.Equal(1.2, store.Get("T_NEW")!.Defaults.Length);

        Assert.Contains("알 수 없습니다", await (await Send(HttpMethod.Post, "/admin/types/paste", "T_NEW\t짧게")).Content.ReadAsStringAsync()); // no header row
    }

    [Fact]
    public async Task Settings_Has_Three_Tabs_And_Types_Is_The_Middle_One()
    {
        const string Token = "test!Admin12";
        using var env = new JobEnv(worker: false, adminToken: Token);
        async Task<string> Get(string url) => await (await env.Http.SendAsync(new HttpRequestMessage(HttpMethod.Get, url) { Headers = { { ReleaseStore.HeaderName, Token } } })).Content.ReadAsStringAsync();
        var jobs = await Get("/admin/settings");
        foreach (var s in new[] { "작업 현황", "기준 정보", "서버 설정", "최근 작업", "로그아웃" }) Assert.Contains(s, jobs);
        Assert.DoesNotContain("서비스 다시 시작", jobs);
        var server = await Get("/admin/settings?tab=server");
        Assert.Contains("서비스 다시 시작", server); Assert.Contains("서버 상태", server); Assert.DoesNotContain("최근 작업", server);
        var types = await Get("/admin/types");
        Assert.Contains("href=\"/admin/types\" class=\"on\"", types);
        Assert.Contains("엑셀에서 붙여넣기", types);
    }

    [Fact]
    public async Task Corrupt_TypesJson_Is_Not_Overwritten()
    {
        using var env = new JobEnv();
        var store = env.Factory.Services.GetRequiredService<CatalogStore>();
        var file = Path.Combine(store.Dir, "types.json");
        File.WriteAllText(file, "{ not json");
        Assert.Equal("{\"types\":[]}", await env.Http.GetStringAsync("/api/catalog/types"));
        Assert.NotNull(store.LoadError);
        Assert.StartsWith("types.json을 읽을 수 없어 저장하지 않습니다", store.Save(T("T_A"), "me"));
        Assert.Equal("{ not json", File.ReadAllText(file));
        Assert.Single(Directory.GetFiles(store.Dir, "types.json.corrupt-*"));
    }

    [Fact]
    public void Investment_Lists_Unpriced_Types_And_Sums_Known_Only()
    {
        ASH.Core.Input.AgvGroup G(string g, string t) => new(g, t, 1, 5, new List<string>(), "x");
        var input = new ASH.Core.Input.ProjectInput { Groups = { G("G1", "T_A"), G("G2", "T_B"), G("G3", "T_A") } };
        var inv = JobView.Investment(input, new Dictionary<string, int> { ["G1"] = 2, ["G2"] = 3, ["G3"] = 1 },
            new Dictionary<string, PriceRow> { ["T_A"] = new("T_A", 1000m, "KRW", "") });
        Assert.Equal(3000m, inv.Total);
        Assert.Equal(new[] { "T_B" }, inv.Unpriced);
        Assert.Equal("KRW", inv.Currency);
    }

    [Fact]
    public async Task Summary_Shows_Investment_Card()
    {
        using var env = new JobEnv();
        var body = await (await env.Send(JobEnv.MakePackage(env.Dir))).Content.ReadAsStringAsync();
        var id = JsonDocument.Parse(body).RootElement.GetProperty("id").GetString()!;
        var job = await env.WaitFinished(id);
        var type = JobView.For(env.Store, job).Input!.Groups[0].Type;
        var cat = env.Factory.Services.GetRequiredService<CatalogStore>();
        cat.Save(T(type), "me"); cat.Save(T("T_OTHER"), "me");
        var total = job.Decided!.Values.Sum() * 500m;
        cat.SavePrices($"Type,UnitPrice,Currency,Note\n{type},500,KRW,\n");
        var priced = await env.Http.GetStringAsync("/jobs/" + id);
        Assert.Contains(total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), priced);
        Assert.DoesNotContain("단가 없음", priced);
        cat.SavePrices("Type,UnitPrice,Currency,Note\nT_OTHER,500,KRW,\n");
        Assert.Contains("단가 없음: " + type, await env.Http.GetStringAsync("/jobs/" + id));
        Assert.Contains("<div class=\"t3\">투자비(원)</div><div class=\"t1\">-</div>", await env.Http.GetStringAsync("/jobs/" + id)); // nothing the job uses is priced: "-", not "0"
    }
}
