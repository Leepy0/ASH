using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ASH.Core.Update;
using Xunit;

namespace ASH.Tests.Update;

public sealed class NoticeFeedTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ash-nt-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private sealed class Fake : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Fn = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        public string? LastUrl;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) { LastUrl = r.RequestUri!.AbsoluteUri; return Fn(r, ct); }
    }

    private static Notice V(string ver, bool important = false, string title = "", params string[] lines) =>
        new() { Id = "v" + ver, Version = ver, Important = important, Title = title, Lines = lines.ToList() };

    private static Notice G(int n, string published, bool important = false, string title = "", params string[] lines) =>
        new() { Id = "n" + n, Important = important, Title = title, Lines = lines.ToList(), Published = published };

    [Fact]
    public void Key_version_is_id_general_adds_published()
    {
        Assert.Equal("v0.31.0", NoticeFeed.Key(V("0.31.0")));
        Assert.Equal("n3@2026-10-29T01:02:03Z", NoticeFeed.Key(G(3, "2026-10-29T01:02:03Z")));
        Assert.NotEqual(NoticeFeed.Key(G(3, "2026-10-29T01:02:03Z")), NoticeFeed.Key(G(3, "2026-11-01T00:00:00Z")));
    }

    [Fact]
    public void Seen_roundtrip_and_edited_general_notice_shows_again()
    {
        var path = Path.Combine(_dir, "sub", "notices.seen");
        Assert.Empty(NoticeFeed.LoadSeen(path));
        var v = V("0.31.0", true); var g = G(3, "2026-10-29T01:02:03Z");
        NoticeFeed.MarkSeen(path, new[] { v, g });
        NoticeFeed.MarkSeen(path, new[] { v });                       // no duplicate line
        Assert.Equal(new[] { "v0.31.0", "n3@2026-10-29T01:02:03Z" }, File.ReadAllLines(path));

        var seen = NoticeFeed.LoadSeen(path);
        Assert.Empty(NoticeFeed.Unseen(new[] { v, g }, seen));
        var edited = G(3, "2026-11-01T00:00:00Z");
        Assert.Equal(new[] { "n3" }, NoticeFeed.Unseen(new[] { v, edited }, seen).Select(n => n.Id));
    }

    [Fact]
    public void Unseen_skips_seen_and_idless_and_keeps_order()
    {
        var a = V("0.32.0"); var b = V("0.31.0"); var noId = new Notice { Title = "x" };
        var r = NoticeFeed.Unseen(new[] { a, b, noId }, new HashSet<string> { "v0.31.0" });
        Assert.Equal(new[] { "v0.32.0" }, r.Select(n => n.Id));
    }

    [Fact]
    public void ShouldPopup_only_when_an_unseen_one_is_important()
    {
        Assert.False(NoticeFeed.ShouldPopup(new List<Notice>()));
        Assert.False(NoticeFeed.ShouldPopup(new[] { V("0.32.0"), G(1, "t") }));
        Assert.True(NoticeFeed.ShouldPopup(new[] { V("0.32.0"), V("0.31.0", true) }));
        Assert.True(NoticeFeed.ShouldPopup(new[] { G(1, "t", true) }));
    }

    [Theory]
    [InlineData("0.32.0", "0.31.0", true)]
    [InlineData("0.31.0", "0.31.0+abc", false)]
    [InlineData("0.30.0", "0.31.0", false)]
    [InlineData("", "0.31.0", false)]
    public void UpdateAvailable(string latest, string current, bool want) => Assert.Equal(want, NoticeFeed.UpdateAvailable(latest, current));

    [Fact]
    public void Text_versions_first_newest_as_given_then_general()
    {
        var list = new[] { G(2, "t", false, "점검", "금요일 18시"), V("0.32.0", true, "입력 표", "열 이름 한글", "저장 속도"), V("0.31.0", false, "", "경로 수정") };
        Assert.Equal("v0.32.0 · 입력 표\r\n• 열 이름 한글\r\n• 저장 속도\r\n\r\nv0.31.0\r\n• 경로 수정\r\n\r\n공지 · 점검\r\n• 금요일 18시", NoticeFeed.Text(list));
        Assert.Equal("", NoticeFeed.Text(Array.Empty<Notice>()));
        Assert.Equal("공지 3건 · AGVNOTICE로 보기", NoticeFeed.Summary(3));
    }

    [Fact]
    public async Task Fetch_sends_current_and_parses()
    {
        var body = new NoticeList { Latest = "0.32.0", Notices = { V("0.32.0", true, "t", "a") } }.ToJson();
        var h = new Fake { Fn = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }) };
        var l = await NoticeFeed.FetchAsync(new Uri("http://srv:8082"), "0.31.0+abc def", TimeSpan.FromSeconds(3), h);
        Assert.Equal("http://srv:8082/api/notices?current=0.31.0%2Babc%20def", h.LastUrl);
        Assert.Equal("0.32.0", l.Latest);
        Assert.Single(l.Notices);
    }

    [Fact]
    public async Task Fetch_failures_throw()
    {
        var bad = new Fake();   // 404
        await Assert.ThrowsAsync<HttpRequestException>(() => NoticeFeed.FetchAsync(new Uri("http://srv/"), "0.1.0", TimeSpan.FromSeconds(3), bad));
        var junk = new Fake { Fn = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>") }) };
        await Assert.ThrowsAsync<InvalidDataException>(() => NoticeFeed.FetchAsync(new Uri("http://srv/"), "0.1.0", TimeSpan.FromSeconds(3), junk));
        var slow = new Fake { Fn = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NoticeFeed.FetchAsync(new Uri("http://srv/"), "0.1.0", TimeSpan.FromMilliseconds(100), slow));
    }
}
