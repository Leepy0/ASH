using System;
using System.Threading;
using System.Net;
using System.Net.Http;
using ASH.Core.Cel;
using ASH.Core.Export;
using ASH.Core.Input;
using Xunit;

namespace ASH.Tests.Cel;

public class AssetOverrideTests
{
    static Box[] Two() => new[] { new Box("body", 0, 0, 0, 0.8, 1.2, 0.5), new Box("wheel", 0.4, -0.4, 0, 0.05, 0.1, 0.1, 0) };

    static string[] Template()
    {
        using var s = typeof(LayoutExporter).Assembly.GetManifestResourceStream("ASH.Core.Export.Templates.agvs_vehseg.txt")!;
        return new StreamReader(s).ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
    }

    static AgvType Type(string t, string asset) => new(t, 1, 1, 1, 1, 1, 0, 0, 0, 0, 1.2, 0.8, "", Asset: asset);

    [Fact] public void Asset_Type_Gets_Override_Others_Not() { var t = new[]{ Type("T_A", "agv_a"), Type("T_B", "") }; var w = new List<string>(); var o = AssetOverrides.Build(t, Template(), (ty, n) => CelWriter.Cel("c", Two()), w); Assert.Single(o); Assert.StartsWith("AGVVEHSEG name DefSegment", o["T_A"].VehSegText); Assert.Empty(w); }
    [Fact] public void Missing_Asset_Warns_And_Falls_Back() { var w = new List<string>(); var o = AssetOverrides.Build(new[]{ Type("T_A","nope") }, Template(), (_, _) => null, w); Assert.Empty(o); Assert.Contains(w, x => x.Contains("nope") && x.Contains("기본 그림")); }
    [Fact] public void Fetch_Exception_Is_A_Warning_Not_A_Throw() { var w = new List<string>(); AssetOverrides.Build(new[]{ Type("T_A","a") }, Template(), (_, _) => throw new IOException("x"), w); Assert.Single(w); }
    [Fact] public void Garbage_Cel_Is_A_Warning() { var w = new List<string>(); var o = AssetOverrides.Build(new[]{ Type("T_A","a") }, Template(), (_, _) => "junk", w); Assert.Empty(o); Assert.Single(w); }

    [Fact] public void Overrides_Are_Keyed_By_Group_For_The_Exporter()
    {
        var byType = AssetOverrides.Build(new[]{ Type("T_A", "a") }, Template(), (_, _) => CelWriter.Cel("c", Two()), new List<string>());
        var g = new[] { new AgvGroup("G1", "t_a", 1, 2, new(), ""), new AgvGroup("G2", "T_B", 1, 1, new(), "") };
        Assert.Equal(new[] { "G1" }, AssetOverrides.ForGroups(byType, g).Keys);
    }

    sealed class Fake : HttpMessageHandler
    {
        public List<string> Seen = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var p = r.RequestUri!.AbsolutePath;
            Seen.Add(p);
            HttpResponseMessage R(string s, HttpStatusCode c = HttpStatusCode.OK) => new(c) { Content = new StringContent(s) };
            return Task.FromResult(p == "/api/catalog/assets" ? R("{\"assets\":[{\"id\":\"a1\",\"typeName\":\"T_A\",\"name\":\"agv_a\"},{\"id\":\"a2\",\"typeName\":\"T_B\",\"name\":\"x,y\"}]}")
                : p == "/api/catalog/assets/a1/asset.cel" ? R("CEL") : R("no", HttpStatusCode.NotFound));
        }
    }

    [Fact] public void Client_Lists_Names_And_Downloads_By_Name()
    {
        var f = new Fake(); var c = new AssetCatalogClient(new Uri("http://srv:8080"), f);
        Assert.Equal(new[] { "agv_a" }, c.Names()); // "x,y" would split the Excel list
        Assert.Equal("CEL", c.FetchCel("T_A", "AGV_A"));
        Assert.Null(c.FetchCel("T_A", "nope"));
        Assert.Equal(1, f.Seen.Count(x => x == "/api/catalog/assets")); // the list is read once
    }

    sealed class ErrHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("x") });
    }

    [Fact] public void Client_Http_Error_Throws() => Assert.Throws<HttpRequestException>(() => new AssetCatalogClient(new Uri("http://srv"), new ErrHandler()).Names());
}

public class CatalogDiffAssetNamesTests
{
    [Fact] public void Names_From_The_Asset_Client_Reach_CatalogDiff()
    {
        var input = new ProjectInput();
        input.AgvTypes.Add(new AgvType("T_A", 1, 1, 1, 1, 1, 0, 0, 0, 0, 1.2, 0.8, "", Row: 3, Asset: "agv_a"));
        input.AgvTypes.Add(new AgvType("T_B", 1, 1, 1, 1, 1, 0, 0, 0, 0, 1.2, 0.8, "", Row: 4, Asset: "gone"));
        var d = InputValidator.CatalogDiff(input, new List<ASH.Core.Catalog.CatalogType>(), new[] { "agv_a" });
        Assert.Single(d); Assert.Contains("gone", d[0].Message);
        Assert.Empty(InputValidator.CatalogDiff(input, new List<ASH.Core.Catalog.CatalogType>(), null)); // server unreachable: no asset warning
    }
}

public class AssetServerDownTests
{
    sealed class Hang : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { Interlocked.Increment(ref Calls); await Task.Delay(Timeout.Infinite, ct); return null!; }
    }
    static AgvType Type(string t, string asset) => new(t, 1, 1, 1, 1, 1, 0, 0, 0, 0, 1.2, 0.8, "", Asset: asset);

    [Fact] public void Timeout_Is_A_TimeoutException()
        => Assert.Throws<TimeoutException>(() => new AssetCatalogClient(new Uri("http://srv"), new Hang(), TimeSpan.FromMilliseconds(100)).Names());

    [Fact] public void Unreachable_Server_Is_Asked_Once_And_Warned_Once()
    {
        var h = new Hang(); var c = new AssetCatalogClient(new Uri("http://srv"), h, TimeSpan.FromMilliseconds(100));
        var w = new List<string>();
        var o = AssetOverrides.Build(new[] { Type("A", "a"), Type("B", "b"), Type("C", "c") }, new[] { "x" }, (ty, n) => c.FetchCel(ty, n), w);
        Assert.Empty(o); Assert.Equal(1, h.Calls); Assert.Single(w); Assert.Contains("3개 타입", w[0]);
    }
}

public class AssetCelTimeoutTests
{
    sealed class HangOnCel : HttpMessageHandler
    {
        public int List, Cel;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (r.RequestUri!.AbsolutePath == "/api/catalog/assets") { Interlocked.Increment(ref List); return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("{\"assets\":[{\"id\":\"1\",\"typeName\":\"A\",\"name\":\"a\"},{\"id\":\"2\",\"typeName\":\"B\",\"name\":\"b\"}]}") }; }
            Interlocked.Increment(ref Cel); await Task.Delay(Timeout.Infinite, ct); return null!;
        }
    }
    static AgvType Type(string t, string asset) => new(t, 1, 1, 1, 1, 1, 0, 0, 0, 0, 1.2, 0.8, "", Asset: asset);

    [Fact] public void Hanging_Cel_Download_Stalls_Once()
    {
        var h = new HangOnCel(); var c = new AssetCatalogClient(new Uri("http://srv"), h, TimeSpan.FromMilliseconds(150));
        var w = new List<string>();
        var o = AssetOverrides.Build(new[] { Type("A", "a"), Type("B", "b") }, new[] { "x" }, (ty, n) => c.FetchCel(ty, n), w);
        Assert.Empty(o); Assert.Equal(1, h.List); Assert.Equal(1, h.Cel); Assert.Single(w); Assert.Contains("2개 타입", w[0]);
    }

    [Fact] public void Cancellation_From_The_Fetch_Counts_As_Server_Down()
    {
        var w = new List<string>(); int calls = 0;
        AssetOverrides.Build(new[] { Type("A", "a"), Type("B", "b") }, new[] { "x" }, (_, _) => { calls++; throw new OperationCanceledException(); }, w);
        Assert.Equal(1, calls); Assert.Single(w);
    }
}
