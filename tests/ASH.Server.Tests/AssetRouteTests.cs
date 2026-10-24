using System.Net;
using ASH.Core.Catalog;
using ASH.Server;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ASH.Server.Tests;

public class AssetRouteTests
{
    const string Token = "test-admin-token-1234567890";
    const string Stl = "solid body\nfacet normal 0 0 1\n outer loop\n  vertex 0 0 0\n  vertex 1000 0 0\n  vertex 0 1000 500\n endloop\nendfacet\nendsolid body\n";

    static JobEnv Env(string? maxMb = null)
    {
        var env = new JobEnv(worker: false, adminToken: Token, assetMaxMb: maxMb);
        Assert.Null(env.Types.Save(new CatalogType { Name = "T_AGV", Kind = "Standard", Defaults = { Capacity = 1 } }, "t"));
        return env;
    }

    static HttpClient NoRedirect(JobEnv env) => env.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    static async Task<HttpResponseMessage> PostAsset(JobEnv env, string type, string name, byte[] bytes, string fileName, bool admin = true, string url = "/admin/assets")
    {
        var form = new MultipartFormDataContent { { new StringContent(type), "type" }, { new StringContent(name), "name" }, { new StringContent("n"), "note" }, { new StringContent("0"), "conveyorDir" } };
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        if (admin) req.Headers.Add(ReleaseStore.HeaderName, Token);
        return await NoRedirect(env).SendAsync(req);
    }

    static Task<HttpResponseMessage> PostForm(JobEnv env, string url, params (string, string)[] f) => PostForm(env, url, true, f);

    static async Task<HttpResponseMessage> PostForm(JobEnv env, string url, bool admin, params (string, string)[] f)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(f.Select(x => new KeyValuePair<string, string>(x.Item1, x.Item2))) };
        if (admin) req.Headers.Add(ReleaseStore.HeaderName, Token);
        return await NoRedirect(env).SendAsync(req);
    }

    static string IdFrom(Uri loc) => loc.OriginalString.Split("#asset-")[1];
    static byte[] StlBytes => System.Text.Encoding.ASCII.GetBytes(Stl);

    [Fact] public async Task Upload_Then_Cel_And_List()
    {
        using var env = Env();
        var r = await PostAsset(env, "T_AGV", "agv_a", StlBytes, "agv.stl");
        Assert.Equal(HttpStatusCode.SeeOther, r.StatusCode);
        var id = IdFrom(r.Headers.Location!);
        var cel = await env.Http.GetStringAsync($"/api/catalog/assets/{id}/asset.cel");
        Assert.StartsWith("10000 ", cel);
        Assert.Contains("<svg", await env.Http.GetStringAsync($"/api/catalog/assets/{id}/preview.svg"));
        var list = await env.Http.GetStringAsync("/api/catalog/assets?type=T_AGV");
        Assert.Contains("\"agv_a\"", list);
        Assert.Contains(id, env.Types.Get("T_AGV")!.Assets);
    }

    [Fact] public async Task Upload_Without_Admin_Is_401()
    {
        using var env = Env();
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostAsset(env, "T_AGV", "agv_a", StlBytes, "agv.stl", admin: false)).StatusCode);
    }

    [Fact] public async Task Reconvert_And_Delete_Without_Admin_Are_401()
    {
        using var env = Env();
        var id = IdFrom((await PostAsset(env, "T_AGV", "agv_a", StlBytes, "agv.stl")).Headers.Location!);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostForm(env, $"/admin/assets/{id}/reconvert", false, ("name", "agv_b"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await PostForm(env, $"/admin/assets/{id}/delete", false)).StatusCode);
        Assert.Contains("\"agv_a\"", await env.Http.GetStringAsync("/api/catalog/assets?type=T_AGV")); // untouched
    }

    [Fact] public async Task NaN_Mesh_Is_400()
    {
        using var env = Env();
        var r = await PostAsset(env, "T_AGV", "bad", System.Text.Encoding.ASCII.GetBytes(Stl.Replace("vertex 1000 0 0", "vertex 1e999 0 0")), "bad.stl");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("숫자가 아닌 좌표", await r.Content.ReadAsStringAsync());
    }

    [Fact] public async Task Over_Limit_Is_413()
    {
        using var env = Env("0.001");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await PostAsset(env, "T_AGV", "big", new byte[2048], "big.stl")).StatusCode);
    }

    [Fact] public async Task Bad_Extension_Is_400()
    {
        using var env = Env();
        var r = await PostAsset(env, "T_AGV", "x", new byte[10], "x.step");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Contains("STL", await r.Content.ReadAsStringAsync());
    }

    [Fact] public async Task Bad_Option_Is_400_And_Unknown_Type_Is_400()
    {
        using var env = Env();
        var form = new MultipartFormDataContent { { new StringContent("T_AGV"), "type" }, { new StringContent("a"), "name" }, { new StringContent("abc"), "maxBoxes" } };
        form.Add(new ByteArrayContent(StlBytes), "file", "a.stl");
        var req = new HttpRequestMessage(HttpMethod.Post, "/admin/assets") { Content = form };
        req.Headers.Add(ReleaseStore.HeaderName, Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await NoRedirect(env).SendAsync(req)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsset(env, "T_NOPE", "a", StlBytes, "a.stl")).StatusCode);
    }

    [Fact] public async Task Reconvert_Changes_Options_And_Delete_Removes_Files_And_Type_Link()
    {
        using var env = Env();
        var id = IdFrom((await PostAsset(env, "T_AGV", "agv_a", StlBytes, "agv.stl")).Headers.Location!);
        var re = await PostForm(env, $"/admin/assets/{id}/reconvert", ("name", "agv_b"), ("note", ""), ("conveyorDir", "90"), ("origin", "Front"));
        Assert.Equal(HttpStatusCode.SeeOther, re.StatusCode);
        Assert.Contains("\"agv_b\"", await env.Http.GetStringAsync("/api/catalog/assets?type=T_AGV"));
        Assert.Equal(HttpStatusCode.BadRequest, (await PostForm(env, $"/admin/assets/{id}/reconvert", ("name", "agv_b"), ("rotateDeg", "45"))).StatusCode);

        var del = await PostForm(env, $"/admin/assets/{id}/delete");
        Assert.Equal(HttpStatusCode.SeeOther, del.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await env.Http.GetAsync($"/api/catalog/assets/{id}/asset.cel")).StatusCode);
        Assert.DoesNotContain(id, env.Types.Get("T_AGV")!.Assets);
    }

    [Fact] public async Task Admin_Types_Page_Shows_Section()
    {
        using var env = Env();
        await PostAsset(env, "T_AGV", "agv_a", StlBytes, "agv.stl");
        var req = new HttpRequestMessage(HttpMethod.Get, "/admin/types");
        req.Headers.Add(ReleaseStore.HeaderName, Token);
        var html = await (await env.Http.SendAsync(req)).Content.ReadAsStringAsync();
        Assert.Contains("id=\"assets\"", html);
        Assert.Contains("<svg", html);
        Assert.Contains("agv_a", html);
        Assert.Contains("STEP은 CAD에서 STL로", html);
    }
}
