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
        var ok = s.SavePrices("Type,UnitPrice,Currency,Note\nT_A,\"1,000\",,a note\nT_B,2000,USD,");
        Assert.Equal(2, ok.Changed);
        Assert.Equal(1000m, s.Prices()["T_A"].UnitPrice);
        Assert.Equal("KRW", s.Prices()["T_A"].Currency);
        Assert.Equal("USD", s.Prices()["T_B"].Currency);
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
        const string Token = "test-admin-token-1234567890";
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
        await env.WaitFinished(id);
        env.Factory.Services.GetRequiredService<CatalogStore>().SavePrices("Type,UnitPrice,Currency,Note\nT_AGV,500,KRW,\n");
        Assert.Contains("투자비(원)", await env.Http.GetStringAsync("/jobs/" + id));
    }
}
