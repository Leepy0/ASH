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
}
