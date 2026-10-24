using System.Diagnostics;
using System.Net;
using System.Net.Http;
using ASH.Core.Catalog;

namespace ASH.Tests.Runner;

public class CatalogFetchTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public HttpStatusCode Status = HttpStatusCode.OK; public string Body = ""; public TimeSpan Delay;
        public Uri? Seen;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen = r.RequestUri;
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }

    private const string One = "{\"types\":[{\"name\":\"T_A\",\"label\":\"A\",\"category\":\"c\",\"kind\":\"agv\",\"defaults\":{},\"assets\":[]}]}";

    [Fact] public void TryGet_Returns_Visible_Types_On_200()
    {
        var h = new Fake { Body = One };
        var r = CatalogFetch.TryGet("http://srv:5000", h);
        Assert.NotNull(r); Assert.Single(r!); Assert.Equal("T_A", r![0].Name);
        Assert.Equal("/api/catalog/types", h.Seen!.AbsolutePath);
    }

    [Fact] public void TryGet_Skips_Null_Name_And_Missing_Defaults()
    {
        var body = "{\"types\":[{\"name\":null,\"defaults\":{}},{\"name\":\"T_X\"},{\"name\":\"T_A\",\"defaults\":{}}]}";
        var r = CatalogFetch.TryGet("http://srv:5000", new Fake { Body = body });
        Assert.Equal("T_A", Assert.Single(r!).Name);
        Assert.Null(CatalogFetch.TryGet("http://srv:5000", new Fake { Body = "{\"types\":[null]}" }));
    }

    [Fact] public void TryGet_Falls_Back_On_Timeout_404_Empty_And_BadJson()
    {
        var sw = Stopwatch.StartNew();
        Assert.Null(CatalogFetch.TryGet("http://srv:5000", new Fake { Body = One, Delay = TimeSpan.FromSeconds(5) }));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(6), sw.Elapsed.ToString());
        Assert.Null(CatalogFetch.TryGet("http://srv:5000", new Fake { Status = HttpStatusCode.NotFound, Body = "no" }));
        Assert.Null(CatalogFetch.TryGet("http://srv:5000", new Fake { Body = "{\"types\":[]}" }));
        Assert.Null(CatalogFetch.TryGet("http://srv:5000", new Fake { Body = "not json" }));
        Assert.Null(CatalogFetch.TryGet("", new Fake { Body = One }));
        Assert.Null(CatalogFetch.TryGet(null, new Fake { Body = One }));
        Assert.Null(CatalogFetch.TryGet("ftp://x", new Fake { Body = One }));
    }
}
