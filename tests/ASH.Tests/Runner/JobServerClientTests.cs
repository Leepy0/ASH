using System.Net;
using System.Net.Http;
using ASH.Core.Runner;

namespace ASH.Tests.Runner;

public class JobServerClientTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public HttpRequestMessage? Seen; public string Body = "";
        public Func<HttpResponseMessage> Reply = () => new HttpResponseMessage(HttpStatusCode.OK);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen = r;
            if (r.Content != null) Body = await r.Content.ReadAsStringAsync();
            return Reply();
        }
    }

    private static HttpResponseMessage Json(string s, HttpStatusCode c = HttpStatusCode.OK) => new(c) { Content = new StringContent(s) };

    [Fact] public void Queue_json_is_read_including_nulls()
    {
        var q = JobServerClient.ParseQueue("{\"ahead\":3,\"estimatedStart\":\"2026-10-21T00:57:15Z\",\"typicalMinutes\":12.5,\"enabled\":true}");
        Assert.Equal(3, q.Ahead); Assert.Equal("2026-10-21T00:57:15Z", q.EstimatedStart); Assert.Equal(12.5, q.TypicalMinutes); Assert.True(q.Enabled);
        var n = JobServerClient.ParseQueue("{\"ahead\":0,\"estimatedStart\":null,\"typicalMinutes\":null,\"enabled\":false}");
        Assert.Null(n.EstimatedStart); Assert.Null(n.TypicalMinutes); Assert.False(n.Enabled);
    }

    [Fact] public void Garbage_is_an_InvalidDataException()
    {
        Assert.Throws<InvalidDataException>(() => JobServerClient.ParseQueue("<html>"));
        Assert.Throws<InvalidDataException>(() => JobServerClient.ParseReceipt("{\"url\":\"/jobs/1\"}")); // no id
    }

    [Fact] public async Task Queue_is_fetched_from_the_api_path()
    {
        var f = new Fake { Reply = () => Json("{\"ahead\":1,\"enabled\":true}") };
        using var c = new JobServerClient(new Uri("http://srv:8082/"), f);
        Assert.Equal(1, (await c.GetQueueAsync()).Ahead);
        Assert.Equal("http://srv:8082/api/jobs/queue", f.Seen!.RequestUri!.AbsoluteUri);
    }

    [Fact] public async Task Submit_posts_the_fields_and_the_package_part()
    {
        var zip = Path.GetTempFileName();
        try
        {
            File.WriteAllText(zip, "PKDATA");
            var f = new Fake { Reply = () => Json("{\"id\":\"j1\",\"url\":\"/jobs/j1\",\"ahead\":2}") };
            using var c = new JobServerClient(new Uri("http://srv:8082"), f);
            var r = await c.SubmitAsync(zip, new JobSubmission("프로젝트", "plan1", "M1", "kim"));
            Assert.Equal("j1", r.Id); Assert.Equal(2, r.Ahead);
            Assert.Equal("http://srv:8082/jobs/j1", c.PageUri(r)!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, f.Seen!.Method);
            Assert.Equal("http://srv:8082/api/jobs", f.Seen.RequestUri!.AbsoluteUri);
            foreach (var part in new[] { "name=project", "name=plan", "name=model", "name=submitter", "name=impact", "name=package", "filename=package.zip", "프로젝트", "PKDATA" })
                Assert.Contains(part, f.Body);
        }
        finally { File.Delete(zip); }
    }

    [Fact] public async Task A_refusal_carries_status_and_reason()
    {
        var f = new Fake { Reply = () => Json("모델 이름이 잘못되었습니다.", HttpStatusCode.BadRequest) };
        using var c = new JobServerClient(new Uri("http://srv:8082/"), f);
        var e = await Assert.ThrowsAsync<JobServerException>(() => c.GetQueueAsync());
        Assert.Equal(400, e.Status); Assert.Equal("모델 이름이 잘못되었습니다.", e.Body);
    }

    [Theory]
    [InlineData("http://other/x", "http://other/x")]
    [InlineData("file:///c:/windows/notepad.exe", null)]
    [InlineData("", null)]
    public void Only_http_pages_are_opened(string url, string? expected)
    {
        using var c = new JobServerClient(new Uri("http://srv:8082/"));
        Assert.Equal(expected, c.PageUri(new JobReceipt { Id = "x", Url = url })?.AbsoluteUri);
    }
}
