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
