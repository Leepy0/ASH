using System.Net;
using System.Net.Http;
using System.Text;
using ASH.Core.Runner;

namespace ASH.Tests.Runner;

[CollectionDefinition("Memory", DisableParallelization = true)] public sealed class MemoryCollection { }

// own collection: nothing else runs beside it, so the memory readings are not other tests' allocations
[Collection("Memory")]
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

    [Fact] public void Queue_generates_defaults_to_false_for_older_servers()
    {
        Assert.False(JobServerClient.ParseQueue("{\"ahead\":0,\"enabled\":true}").Generates);
        Assert.True(JobServerClient.ParseQueue("{\"ahead\":0,\"enabled\":true,\"generates\":true}").Generates);
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

    [Fact] public async Task Analyses_are_fetched_and_read()
    {
        var f = new Fake { Reply = () => Json("[{\"id\":\"a-1\",\"name\":\"분석\",\"note\":\"x\",\"plans\":[{\"id\":\"p-1\",\"name\":\"A안\",\"nextVersion\":3}]},{\"id\":\"a-2\",\"name\":\"빈\",\"plans\":[]}]") };
        using var c = new JobServerClient(new Uri("http://srv:8082/"), f);
        var list = await c.GetAnalysesAsync();
        Assert.Equal("http://srv:8082/api/analyses", f.Seen!.RequestUri!.AbsoluteUri);
        Assert.Equal(2, list.Count);
        Assert.Equal("a-1", list[0].Id); Assert.Equal("분석", list[0].Name);
        Assert.Equal("p-1", list[0].Plans[0].Id); Assert.Equal("A안", list[0].Plans[0].Name); Assert.Equal(3, list[0].Plans[0].NextVersion);
        Assert.Empty(list[1].Plans);
        Assert.Throws<InvalidDataException>(() => JobServerClient.ParseAnalyses("<html>"));
    }

    [Fact] public async Task Submit_sends_analysis_fields_only_when_set_and_reads_the_version()
    {
        var zip = Path.GetTempFileName();
        try
        {
            File.WriteAllText(zip, "PK");
            var f = new Fake { Reply = () => Json("{\"id\":\"j1\",\"url\":\"/jobs/j1\",\"ahead\":0,\"version\":4}") };
            using var c = new JobServerClient(new Uri("http://srv:8082"), f);
            var r = await c.SubmitAsync(zip, new JobSubmission("p", "plan", "M", "kim", AnalysisName: "분석X", PlanName: "B안", VersionNote: "메모"));
            Assert.Equal(4, r.Version);
            foreach (var part in new[] { "name=analysisName", "분석X", "name=planName", "B안", "name=versionNote", "메모" }) Assert.Contains(part, f.Body);
            foreach (var part in new[] { "name=analysisId", "name=planId" }) Assert.DoesNotContain(part, f.Body);

            await c.SubmitAsync(zip, new JobSubmission("p", "plan", "M", "kim")); // a 0.18-style send: none of the new fields
            foreach (var part in new[] { "name=analysisId", "name=planId", "name=analysisName", "name=planName", "name=versionNote" }) Assert.DoesNotContain(part, f.Body);
        }
        finally { File.Delete(zip); }
    }

    /// <summary>Reads a multipart body without keeping it: counts bytes and keeps only the head and the tail, enough to size the "package" part.</summary>
    private sealed class BodyProbe
    {
        public long Total; public byte[] Head = Array.Empty<byte>(); public byte[] Tail = Array.Empty<byte>(); public string ContentType = ""; public long? DeclaredLength;
    }

    private static BodyProbe Drain(HttpListenerRequest req)
    {
        var p = new BodyProbe { ContentType = req.ContentType ?? "", DeclaredLength = req.ContentLength64 >= 0 ? req.ContentLength64 : (long?)null };
        const int headMax = 4096, tailMax = 256;
        var head = new MemoryStream(); var tail = new byte[tailMax]; var tailLen = 0; var buf = new byte[81920];
        int n;
        while ((n = req.InputStream.Read(buf, 0, buf.Length)) > 0)
        {
            if (head.Length < headMax) head.Write(buf, 0, (int)Math.Min(n, headMax - head.Length));
            if (n >= tailMax) { Buffer.BlockCopy(buf, n - tailMax, tail, 0, tailMax); tailLen = tailMax; }
            else
            {
                var keep = Math.Min(tailLen, tailMax - n);
                Buffer.BlockCopy(tail, tailLen - keep, tail, 0, keep);
                Buffer.BlockCopy(buf, 0, tail, keep, n); tailLen = keep + n;
            }
            p.Total += n;
        }
        p.Head = head.ToArray(); p.Tail = new byte[tailLen]; Buffer.BlockCopy(tail, 0, p.Tail, 0, tailLen);
        return p;
    }

    /// <summary>Size of the "package" part's data, or -1 when the part or the closing boundary is not where it should be.</summary>
    private static long PackageBytes(BodyProbe p, out string fields)
    {
        fields = "";
        var m = System.Text.RegularExpressions.Regex.Match(p.ContentType, "boundary=\"?([^\";]+)\"?");
        if (!m.Success) return -1;
        var head = Encoding.UTF8.GetString(p.Head);
        var at = head.IndexOf("name=\"package\"", StringComparison.Ordinal);
        if (at < 0) at = head.IndexOf("name=package", StringComparison.Ordinal);
        if (at < 0) return -1;
        var dataAt = head.IndexOf("\r\n\r\n", at, StringComparison.Ordinal);
        if (dataAt < 0) return -1;
        fields = head.Substring(0, dataAt);
        var dataStart = Encoding.UTF8.GetByteCount(head.Substring(0, dataAt + 4));
        var end = "\r\n--" + m.Groups[1].Value + "--\r\n";
        var tail = Encoding.ASCII.GetString(p.Tail);
        if (!tail.EndsWith(end, StringComparison.Ordinal) || !tail.Substring(0, tail.Length - end.Length).EndsWith("END!", StringComparison.Ordinal)) return -1;
        return p.Total - dataStart - end.Length;
    }

    // Packages reach hundreds of MB and the plugin lives inside ZWCAD, so the body must not be copied into memory. On .NET Framework HttpClient sits on HttpWebRequest, which
    // streams only while the content length is known; a real listener checks the process does not grow by anything near the file size (buffering the content first fails this).
    [SkippableFact, Trait("Category", "Slow")]
    public async Task Submit_streams_a_big_package_without_buffering_it()
    {
        const long size = 200L * 1024 * 1024;
        HttpListener? listener = null; string? prefix = null;
        foreach (var host in new[] { "127.0.0.1", "localhost" })
        {
            var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port; tcp.Stop();
            var l = new HttpListener(); l.Prefixes.Add($"http://{host}:{port}/");
            try { l.Start(); listener = l; prefix = $"http://{host}:{port}/"; break; }
            catch (HttpListenerException) { l.Close(); }
        }
        Skip.If(listener == null, "HttpListener cannot start here (no URL ACL)");

        var zip = Path.Combine(Path.GetTempPath(), "ash-stream-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var w = new FileStream(zip, FileMode.Create, FileAccess.Write))
            {
                w.SetLength(size);
                w.Seek(size - 4, SeekOrigin.Begin); w.Write(Encoding.ASCII.GetBytes("END!"), 0, 4);
            }

            BodyProbe? seen = null;
            var served = Task.Run(() =>
            {
                var ctx = listener!.GetContext();
                seen = Drain(ctx.Request);
                var reply = Encoding.UTF8.GetBytes("{\"id\":\"x\",\"url\":\"http://h/jobs/x\",\"ahead\":0}");
                ctx.Response.StatusCode = 200; ctx.Response.ContentType = "application/json"; ctx.Response.ContentLength64 = reply.Length;
                ctx.Response.OutputStream.Write(reply, 0, reply.Length); ctx.Response.Close();
            });

            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long Mem() { using var pr = System.Diagnostics.Process.GetCurrentProcess(); return pr.PrivateMemorySize64; }
            var baseline = Mem(); var peak = baseline; var gcBase = GC.GetTotalMemory(true); var gcPeak = gcBase;
            using var stop = new CancellationTokenSource();
            var sampler = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested) { peak = Math.Max(peak, Mem()); gcPeak = Math.Max(gcPeak, GC.GetTotalMemory(false)); Thread.Sleep(20); }
            });

            JobReceipt r;
            try
            {
                using var c = new JobServerClient(new Uri(prefix!));
                r = await c.SubmitAsync(zip, new JobSubmission("프로젝트", "plan1", "M1", "kim"));
            }
            finally { stop.Cancel(); await sampler; }
            await served;

            Assert.Equal("x", r.Id);
            Assert.Equal(size, PackageBytes(seen!, out var fields));
            foreach (var part in new[] { "name=\"project\"", "name=\"plan\"", "name=\"model\"", "name=\"submitter\"", "name=\"impact\"", "프로젝트", "filename=package.zip" })
                Assert.Contains(part.Replace("\"", ""), fields.Replace("\"", ""));
            if (seen!.DeclaredLength is long declared) Assert.Equal(seen.Total, declared); // a known length is what lets the request go out unbuffered

            const long limit = 60L * 1024 * 1024;
            Assert.True(peak - baseline < limit, $"private memory grew {(peak - baseline) / 1048576} MB for a {size / 1048576} MB package");
            Assert.True(gcPeak - gcBase < limit, $"managed heap grew {(gcPeak - gcBase) / 1048576} MB for a {size / 1048576} MB package");
        }
        finally { listener!.Close(); File.Delete(zip); }
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

    [Fact] public void Analyses_without_tags_read_empty_and_with_tags_filled()
    {
        var l = JobServerClient.ParseAnalyses("[{\"id\":\"a\",\"name\":\"n\",\"plans\":[]},{\"id\":\"b\",\"name\":\"m\",\"corp\":\"P동\",\"productLine\":\"AGV\",\"line\":\"1층\",\"plans\":[]}]");
        Assert.Equal("", l[0].Corp); Assert.Equal("", l[0].ProductLine); Assert.Equal("", l[0].Line);
        Assert.Equal("P동", l[1].Corp); Assert.Equal("AGV", l[1].ProductLine); Assert.Equal("1층", l[1].Line);
    }

    [Fact] public async Task Submit_sends_the_tag_parts()
    {
        var zip = Path.GetTempFileName();
        try
        {
            File.WriteAllText(zip, "PK");
            var f = new Fake { Reply = () => Json("{\"id\":\"j1\",\"url\":\"/jobs/j1\",\"ahead\":0}") };
            using var c = new JobServerClient(new Uri("http://srv:8082"), f);
            await c.SubmitAsync(zip, new JobSubmission("p", "pl", "M", "k", Corp: "법인X", ProductLine: "제품Y", Line: "라인Z"));
            foreach (var part in new[] { "name=corp", "name=productLine", "name=line", "법인X", "제품Y", "라인Z" }) Assert.Contains(part, f.Body);
        }
        finally { File.Delete(zip); }
    }
}
