using System.Net;
using System.Text;
using ASH.Core.Update;
using ASH.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ASH.Server.Tests;

public sealed class NoticeStoreTests : IDisposable
{
    readonly string _d = Path.Combine(Path.GetTempPath(), "ash-notice-" + Guid.NewGuid().ToString("N"));
    readonly NoticeStore _s;
    public NoticeStoreTests() => _s = new NoticeStore(_d, NullLogger.Instance);
    public void Dispose() { try { Directory.Delete(_d, true); } catch (IOException) { } }

    static VersionNote Note(string v, string title, bool imp = false, params string[] lines) => new() { Version = v, Title = title, Important = imp, Lines = lines.ToList() };
    static NotesFile File(params VersionNote[] n) => new() { Notes = n.ToList() };

    [Fact]
    public void Pulled_note_is_a_draft_and_reaches_nobody_until_approved()
    {
        Assert.Equal(1, _s.Pull(File(Note("0.2.0", "새 기능", false, "a", "b"))));
        var n = _s.Get("v0.2.0")!;
        Assert.Equal(NoticeStore.Draft, n.State);
        Assert.Equal(new[] { "a", "b" }, n.Lines);
        Assert.Empty(_s.For("0.1.0", "0.2.0", DateTime.UtcNow).Notices);
        _s.SetState("v0.2.0", NoticeStore.Approved);
        Assert.Single(_s.For("0.1.0", "0.2.0", DateTime.UtcNow).Notices);
    }

    [Fact]
    public void Repull_updates_an_unedited_draft_but_keeps_an_edited_one_and_flags_it()
    {
        _s.Pull(File(Note("0.2.0", "하나", false, "a"), Note("0.3.0", "둘", false, "x")));
        Assert.Null(_s.Edit("v0.3.0", "내 제목", false, new() { "x" }));
        Assert.Equal(2, _s.Pull(File(Note("0.2.0", "하나 바뀜", true, "a", "c"), Note("0.3.0", "둘", false, "y"))));
        var a = _s.Get("v0.2.0")!; var b = _s.Get("v0.3.0")!;
        Assert.Equal("하나 바뀜", a.Title); Assert.True(a.Important); Assert.False(a.Changed);
        Assert.Equal("내 제목", b.Title); Assert.Equal(new[] { "x" }, b.Lines); Assert.True(b.Changed); Assert.True(b.Edited);
        Assert.Equal(new[] { "y" }, b.Github!.Lines);
        Assert.Equal(0, _s.Pull(File(Note("0.2.0", "하나 바뀜", true, "a", "c"), Note("0.3.0", "둘", false, "y")))); // nothing new
        Assert.True(_s.TakeGithub("v0.3.0"));
        b = _s.Get("v0.3.0")!;
        Assert.Equal("둘", b.Title); Assert.Equal(new[] { "y" }, b.Lines); Assert.False(b.Changed); Assert.False(b.Edited);
    }

    [Fact]
    public void Approved_note_is_not_rewritten_silently()
    {
        _s.Pull(File(Note("0.2.0", "하나", false, "a")));
        _s.SetState("v0.2.0", NoticeStore.Approved);
        _s.Pull(File(Note("0.2.0", "하나", false, "a", "새 줄")));
        var n = _s.Get("v0.2.0")!;
        Assert.Equal(new[] { "a" }, n.Lines); Assert.True(n.Changed); Assert.Equal(NoticeStore.Approved, n.State);
        Assert.Equal(new[] { "a" }, _s.For("0.1.0", "0.2.0", DateTime.UtcNow).Notices[0].Lines.ToArray());
    }

    [Fact]
    public void Editing_back_to_githubs_text_clears_the_edit()
    {
        _s.Pull(File(Note("0.2.0", "하나", false, "a")));
        _s.Edit("v0.2.0", "다름", false, new() { "a" });
        Assert.True(_s.Get("v0.2.0")!.Edited);
        _s.Edit("v0.2.0", "하나", false, new() { "a" });
        Assert.False(_s.Get("v0.2.0")!.Edited);
    }

    [Fact]
    public void Invalid_version_or_empty_title_in_a_pull_is_skipped()
    {
        Assert.Equal(0, _s.Pull(File(Note("abc", "x"), Note("0.2.0", ""))));
        Assert.Empty(_s.All());
    }

    [Fact]
    public void General_notice_validation_ids_and_delete()
    {
        Assert.Null(_s.Add("", false, new(), "", "", out var e1)); Assert.NotNull(e1);
        Assert.Null(_s.Add("제목", false, new() { new string('가', 201) }, "", "", out var e2)); Assert.NotNull(e2);
        Assert.Null(_s.Add("제목", false, new(), "2026-02-01", "2026-01-01", out var e3)); Assert.NotNull(e3);
        var n1 = _s.Add("첫째", false, new() { "a" }, "", "", out _)!; var n2 = _s.Add("둘째", true, new(), "", "", out _)!;
        Assert.Equal(new[] { "n1", "n2" }, new[] { n1.Id, n2.Id });
        Assert.True(_s.Delete("n1")); Assert.False(_s.Delete("n1"));
        Assert.Equal("n3", _s.Add("셋째", false, new(), "", "", out _)!.Id); // never reuses the highest number
        _s.Pull(File(Note("0.2.0", "하나")));
        Assert.False(_s.Delete("v0.2.0")); // version notes are hidden, not deleted
    }

    [Fact]
    public void For_filters_by_current_and_latest_newest_first()
    {
        _s.Pull(File(Note("0.29.0", "a"), Note("0.30.0", "b"), Note("0.31.0", "c"), Note("0.32.0", "d"), Note("0.10.0", "e")));
        foreach (var v in new[] { "0.29.0", "0.30.0", "0.31.0", "0.32.0" }) _s.SetState("v" + v, NoticeStore.Approved);
        _s.SetState("v0.10.0", NoticeStore.Hidden);
        string[] Versions(string? cur, string latest) => _s.For(cur, latest, DateTime.UtcNow).Notices.Select(n => n.Version).ToArray();
        Assert.Equal(new[] { "0.31.0", "0.30.0" }, Versions("0.29.0", "0.31.0")); // not above latest, not current itself
        Assert.Equal(new[] { "0.31.0", "0.30.0", "0.29.0" }, Versions("0.28.9", "0.31.0"));
        Assert.Empty(Versions("0.31.0", "0.31.0"));
        Assert.Equal(new[] { "0.31.0", "0.30.0", "0.29.0" }, Versions(null, "0.31.0"));
        Assert.Equal(new[] { "0.31.0", "0.30.0", "0.29.0" }, Versions("zzz", "0.31.0")); // unknown current: everything
        Assert.Equal(new[] { "0.31.0", "0.30.0", "0.29.0" }, Versions("", "0.31.0"));
        Assert.Equal(new[] { "0.31.0", "0.30.0" }, Versions("0.29.0+abc123", "0.31.0")); // the plugin sends its InformationalVersion: +build is ignored
        Assert.Equal(new[] { "0.31.0" }, Versions("0.30.0+abc123", "0.31.0"));
        Assert.Equal(new[] { "0.31.0" }, Versions("0.30.0 abc123", "0.31.0")); // an unencoded + arrives as a space
        Assert.Empty(Versions("0.31.0+abc123", "0.31.0"));
        Assert.Empty(Versions("0.1.0", "")); // nothing published
        Assert.Equal("0.31.0", _s.For("0.1.0", "0.31.0", DateTime.UtcNow).Latest);
    }

    [Fact]
    public void General_notices_follow_their_period_and_come_after_version_notes_newest_first()
    {
        var now = new DateTime(2026, 6, 15, 3, 0, 0, DateTimeKind.Utc); // 12:00 in Seoul
        var open = _s.Add("기한 없음", false, new(), "", "", out _)!;
        var inP = _s.Add("기간 안", true, new() { "x" }, "2026-06-01", "2026-06-15", out _)!;
        var past = _s.Add("지남", false, new(), "2026-05-01", "2026-06-14", out _)!;
        var future = _s.Add("아직", false, new(), "2026-06-16", "", out _)!;
        var draft = _s.Add("대기", false, new(), "", "", out _)!;
        _s.Pull(File(Note("0.2.0", "v")));
        _s.SetState("v0.2.0", NoticeStore.Approved);
        foreach (var n in new[] { open, inP, past, future }) { _s.SetState(n.Id, NoticeStore.Approved); Thread.Sleep(1100); } // Published has 1 s resolution
        var ids = _s.For("0.1.0", "0.2.0", now).Notices.Select(n => n.Id).ToArray();
        Assert.Equal(new[] { "v0.2.0", inP.Id, open.Id }, ids);
        Assert.DoesNotContain(draft.Id, ids);
        Assert.Equal(new[] { "v0.2.0", future.Id, open.Id }, _s.For("0.1.0", "0.2.0", now.AddDays(1)).Notices.Select(n => n.Id)); // Seoul 06-16: 기간 안 ended, 아직 started
    }

    [Fact]
    public void Survives_a_restart_and_a_damaged_file_is_set_aside()
    {
        _s.Pull(File(Note("0.2.0", "하나", true, "가")));
        var again = new NoticeStore(_d, NullLogger.Instance);
        Assert.Equal("가", again.Get("v0.2.0")!.Lines[0]);
        var f = Path.Combine(_d, "notices", "notices.json");
        System.IO.File.WriteAllText(f, "{ not json");
        Assert.Empty(again.All());
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_d, "notices"), "notices.json.corrupt-*"));
    }
}

public class NoticeRouteTests
{
    static void Publish(SettingsEnv e, string version)
    {
        var dir = Path.Combine(e.Dir, "releases", "current");
        Directory.CreateDirectory(dir);
        var m = new ReleaseManifest { Version = version, MinSupported = "0.1.0", File = $"ASH-Setup-{version}.exe", Sha256 = new string('a', 64), Size = 1, Published = ReleaseManifest.FormatPublished(DateTime.UtcNow) };
        System.IO.File.WriteAllBytes(Path.Combine(dir, "ASH.json"), m.ToJson());
    }

    static NoticeStore Store(SettingsEnv e) => e.Factory.Services.GetRequiredService<NoticeStore>();

    static async Task<NoticeList> Api(SettingsEnv e, string query)
    {
        var r = await e.Http.GetAsync("/api/notices" + query);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
        return NoticeList.Parse(await r.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Api_needs_no_auth_and_serves_only_approved_up_to_the_published_version()
    {
        using var e = new SettingsEnv();
        Publish(e, "0.31.0");
        var s = Store(e);
        s.Pull(new NotesFile { Notes = { new() { Version = "0.30.0", Title = "a", Lines = { "x" } }, new() { Version = "0.31.0", Title = "b", Important = true }, new() { Version = "0.32.0", Title = "c" } } });
        foreach (var v in new[] { "0.30.0", "0.32.0" }) s.SetState("v" + v, NoticeStore.Approved);
        var l = await Api(e, "?current=0.29.0");
        Assert.Equal("0.31.0", l.Latest);
        Assert.Equal(new[] { "0.30.0" }, l.Notices.Select(n => n.Version).ToArray()); // 0.31.0 is a draft, 0.32.0 is above the published version
        s.SetState("v0.31.0", NoticeStore.Approved);
        l = await Api(e, "?current=0.30.0");
        Assert.Equal(new[] { "0.31.0" }, l.Notices.Select(n => n.Version).ToArray());
        Assert.True(l.Notices[0].Important);
        Assert.Equal(2, (await Api(e, "")).Notices.Count); // no current: everything up to latest
        Assert.Empty((await Api(e, "?current=0.31.0%2Babc123")).Notices); // +build from the plugin version
    }

    [Fact]
    public async Task Api_without_a_published_version_has_no_version_notes_but_general_ones()
    {
        using var e = new SettingsEnv();
        var s = Store(e);
        s.Pull(new NotesFile { Notes = { new() { Version = "0.30.0", Title = "a" } } }); s.SetState("v0.30.0", NoticeStore.Approved);
        var g = s.Add("일반", false, new() { "x" }, "", "", out _)!; s.SetState(g.Id, NoticeStore.Approved);
        var l = await Api(e, "?current=0.1.0");
        Assert.Equal("", l.Latest);
        Assert.Equal(new[] { g.Id }, l.Notices.Select(n => n.Id).ToArray());
    }

    [Fact]
    public async Task Admin_page_needs_login_and_posts_need_auth_and_same_origin()
    {
        using var e = new SettingsEnv();
        Store(e).Pull(new NotesFile { Notes = { new() { Version = "0.30.0", Title = "제목", Lines = { "항목" } } } });
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Http.GetAsync("/admin/notices")).StatusCode);
        var form = SettingsEnv.Form(("title", "x"), ("lines", "a"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Post("/admin/notices", form)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Post("/admin/notices/v0.30.0/approve", SettingsEnv.Form())).StatusCode);
        Assert.Equal(NoticeStore.Draft, Store(e).Get("v0.30.0")!.State);

        await e.Login();
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Post("/admin/notices/v0.30.0/approve", SettingsEnv.Form(), origin: "http://evil.example")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Post("/admin/notices/v0.30.0/approve", SettingsEnv.Form(), origin: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await e.Post("/admin/notices", form, origin: "http://evil.example")).StatusCode);
        Assert.Equal(NoticeStore.Draft, Store(e).Get("v0.30.0")!.State);
        Assert.Empty(Store(e).All().Where(n => n.General));
    }

    [Fact]
    public async Task Admin_flow_edit_approve_hide_add_delete()
    {
        using var e = new SettingsEnv();
        Store(e).Pull(new NotesFile { Notes = { new() { Version = "0.30.0", Title = "제목", Lines = { "항목" } } } });
        await e.Login();
        var page = await (await e.Http.GetAsync("/admin/notices")).Content.ReadAsStringAsync();
        foreach (var s in new[] { "검토 대기", "0.30.0", "제목", ">공지<", ">숨김<", "일반 공지", "href=\"/admin/notices\"" }) Assert.Contains(s, page);

        Assert.Equal(HttpStatusCode.OK, (await e.Post("/admin/notices/v0.30.0/approve", SettingsEnv.Form(("title", "고친 제목"), ("important", "1"), ("lines", "하나\r\n\r\n 둘 \n")))).StatusCode);
        var n = Store(e).Get("v0.30.0")!;
        Assert.Equal(NoticeStore.Approved, n.State); Assert.Equal("고친 제목", n.Title); Assert.True(n.Important); Assert.True(n.Edited);
        Assert.Equal(new[] { "하나", "둘" }, n.Lines);

        Assert.Equal(HttpStatusCode.OK, (await e.Post("/admin/notices/v0.30.0/hide", SettingsEnv.Form(("title", "고친 제목"), ("lines", "하나")))).StatusCode);
        Assert.Equal(NoticeStore.Hidden, Store(e).Get("v0.30.0")!.State);
        Assert.False(Store(e).Get("v0.30.0")!.Important); // the unchecked box was posted
        Assert.Equal(HttpStatusCode.Conflict, (await e.Post("/admin/notices/v0.30.0/delete")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await e.Post("/admin/notices", SettingsEnv.Form(("title", "점검"), ("lines", "토요일"), ("from", "2026-07-01"), ("to", "2026-07-02")))).StatusCode);
        var g = Store(e).Get("n1")!;
        Assert.Equal(("2026-07-01", "2026-07-02", NoticeStore.Draft), (g.From, g.To, g.State));
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Post("/admin/notices", SettingsEnv.Form(("title", "x"), ("from", "내일")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await e.Post("/admin/notices", SettingsEnv.Form(("title", ""), ("lines", "a")))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await e.Post("/admin/notices/n1/delete")).StatusCode);
        Assert.Null(Store(e).Get("n1"));
        Assert.Equal(HttpStatusCode.NotFound, (await e.Post("/admin/notices/n9/save")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await e.Post("/admin/notices/v0.30.0/zap")).StatusCode);
    }

    [Fact]
    public async Task Changed_note_shows_the_flag_and_github_button()
    {
        using var e = new SettingsEnv();
        var s = Store(e);
        s.Pull(new NotesFile { Notes = { new() { Version = "0.30.0", Title = "제목", Lines = { "항목" } } } });
        s.Edit("v0.30.0", "내 제목", false, new() { "항목" });
        s.Pull(new NotesFile { Notes = { new() { Version = "0.30.0", Title = "제목", Lines = { "항목", "더" } } } });
        await e.Login();
        var page = await (await e.Http.GetAsync("/admin/notices")).Content.ReadAsStringAsync();
        Assert.Contains("GitHub 내용 바뀜", page); Assert.Contains("GitHub 것으로", page); Assert.Contains("<li>더</li>", page);
        Assert.Equal(HttpStatusCode.OK, (await e.Post("/admin/notices/v0.30.0/github")).StatusCode);
        Assert.Equal("제목", s.Get("v0.30.0")!.Title);
    }

    [Fact]
    public async Task Admin_page_html_encodes_note_text()
    {
        using var e = new SettingsEnv();
        Store(e).Pull(new NotesFile { Notes = { new() { Version = "0.30.0", Title = "<script>alert(1)</script>", Lines = { "<b>x</b>" } } } });
        await e.Login();
        var page = await (await e.Http.GetAsync("/admin/notices")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("<script>alert(1)", page);
        Assert.Contains("&lt;script&gt;", page);
    }
}

public class GitHubNotesPullTests
{
    const string Base = "https://github.com/o/r";

    sealed class Fake : HttpMessageHandler
    {
        public byte[]? Notes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
            Task.FromResult(req.RequestUri!.AbsoluteUri == $"{Base}/releases/latest/download/ASH.Notes.json" && Notes != null
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Notes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Check_pulls_ASH_Notes_json_into_the_store_and_404_is_not_an_error()
    {
        using var e = new Env();
        var dir = Path.Combine(Path.GetTempPath(), "ash-notice-sync-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new NoticeStore(dir, NullLogger.Instance);
            var f = new Fake();
            using var s = new GitHubSync(e.Store, Base, TimeSpan.Zero, new HttpClient(f), NullLogger.Instance, notices: store);
            await s.CheckOnceAsync(default);
            Assert.Contains("없음", s.NotesResult); Assert.Empty(store.All());

            f.Notes = new NotesFile { Notes = { new() { Version = "0.31.0", Title = "t", Lines = { "a" } } } }.ToJson();
            await s.CheckOnceAsync(default);
            Assert.StartsWith("받음", s.NotesResult);
            Assert.Equal(NoticeStore.Draft, store.Get("v0.31.0")!.State);

            f.Notes = Encoding.UTF8.GetBytes("not json");
            await s.CheckOnceAsync(default);
            Assert.StartsWith("실패", s.NotesResult); Assert.Single(store.All());
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
