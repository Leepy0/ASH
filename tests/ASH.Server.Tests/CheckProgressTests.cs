using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ASH.Server.Tests;

/// <summary>"최신 버전 지금 확인": the steps (plugin, Runner, notes, server) and the bytes of a download, as the settings page polls them.</summary>
public class CheckProgressTests
{
    private const string Base = "https://github.com/o/r";

    // the setup stream gives its first half, then waits for the gate: the test looks at the check while it downloads
    private sealed class GatedStream(byte[] data, Task gate) : Stream
    {
        private int _pos;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_pos >= data.Length / 2) await gate.WaitAsync(ct);
            var n = Math.Min(buffer.Length, _pos < data.Length / 2 ? data.Length / 2 - _pos : data.Length - _pos);
            data.AsMemory(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }
        public override int Read(byte[] b, int o, int c) => ReadAsync(b.AsMemory(o, c)).AsTask().GetAwaiter().GetResult();
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => data.Length; public override long Position { get => _pos; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    private sealed class Fake(Task gate, string setupUrl) : HttpMessageHandler
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public byte[] Setup = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var u = req.RequestUri!.AbsoluteUri;
            if (u == setupUrl) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new GatedStream(Setup, gate)) });
            return Task.FromResult(Files.TryGetValue(u, out var b)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(b) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static JsonObject Step(GitHubSync s, string name) =>
        JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(s.Progress.Snapshot()))!["steps"]!.AsArray().Select(x => x!.AsObject()).Single(x => (string?)x["name"] == name);

    private static async Task Until(Func<bool> ok)
    {
        for (int i = 0; i < 500 && !ok(); i++) await Task.Delay(10);
        Assert.True(ok());
    }

    [Fact]
    public async Task Steps_move_to_done_with_the_downloaded_bytes_and_a_second_press_starts_nothing()
    {
        using var e = new Env();
        var r = e.MakeRelease("0.2.0", new string('x', 200_000));
        var url = $"{Base}/releases/download/v0.2.0/{r.File}";
        var gate = new TaskCompletionSource();
        var f = new Fake(gate.Task, url) { Setup = r.Setup };
        f.Files[$"{Base}/releases/latest/download/ASH.json"] = r.Json;
        f.Files[$"{Base}/releases/download/v0.2.0/ASH.json.sig"] = r.Sig;
        using var s = new GitHubSync(e.Store, Base, TimeSpan.Zero, new HttpClient(f), NullLogger.Instance);

        Assert.False(s.Progress.Running);
        Assert.True(s.StartCheck(default));
        await Until(() => (long)Step(s, "플러그인 받기")["bytes"]! > 0); // half of the setup came, the rest waits

        Assert.True(s.Progress.Running);
        Assert.False(s.StartCheck(default)); // already running: nothing new starts
        var get = Step(s, "플러그인 받기");
        Assert.Equal("진행", (string?)get["state"]);
        Assert.Equal(r.Setup.Length, (long)get["total"]!);
        Assert.Equal(r.Setup.Length / 2, (long)get["bytes"]!);
        Assert.Equal("완료", (string?)Step(s, "플러그인 확인")["state"]);
        Assert.Equal("대기", (string?)Step(s, "서버 확인")["state"]);

        gate.SetResult();
        await Until(() => !s.Progress.Running);

        get = Step(s, "플러그인 받기");
        Assert.Equal("완료", (string?)get["state"]);
        Assert.Equal(r.Setup.Length, (long)get["bytes"]!);
        Assert.StartsWith("가져옴: 0.2.0", s.LastResult);
        Assert.StartsWith("가져옴: 0.2.0", (string?)get["text"]);
        Assert.Equal(r.Setup, await e.Get("/download/" + r.File)); // what is stored is unchanged by the counting
        Assert.Equal("건너뜀", (string?)Step(s, "Runner 확인")["state"]); // no Runner store here
        Assert.Equal("건너뜀", (string?)Step(s, "서버 준비")["state"]);
        Assert.True(s.StartCheck(default)); // free again
        await Until(() => !s.Progress.Running);
        var again = Step(s, "플러그인 확인");
        Assert.Equal("완료", (string?)again["state"]);
        Assert.StartsWith("최신(0.2.0)", (string?)again["text"]);
        Assert.Equal("건너뜀", (string?)Step(s, "플러그인 받기")["state"]);
    }

    [Fact]
    public async Task A_failing_download_step_shows_the_reason()
    {
        using var e = new Env();
        var f = new Fake(Task.CompletedTask, "");
        using var s = new GitHubSync(e.Store, Base, TimeSpan.Zero, new HttpClient(f), NullLogger.Instance);
        f.Files[$"{Base}/releases/latest/download/ASH.json"] = [1, 2, 3]; // not a manifest

        await s.CheckOnceAsync(default);

        var st = Step(s, "플러그인 확인");
        Assert.Equal("실패", (string?)st["state"]);
        Assert.StartsWith("실패", (string?)st["text"]);
        Assert.Equal("건너뜀", (string?)Step(s, "플러그인 받기")["state"]);
        Assert.False(s.Progress.Running);
    }

    [Fact]
    public async Task Status_json_needs_an_admin_and_has_the_eight_steps()
    {
        using var e = new SettingsEnv();
        Assert.Equal(HttpStatusCode.Unauthorized, (await e.Http.GetAsync("/admin/check-updates/status")).StatusCode);
        await e.Login();
        var r = await e.Http.GetAsync("/admin/check-updates/status");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("no-store", r.Headers.CacheControl?.ToString());
        var j = JsonNode.Parse(await r.Content.ReadAsStringAsync())!;
        Assert.False((bool)j["running"]!);
        Assert.Equal(0, (int)j["run"]!);
        Assert.Equal(["플러그인 확인", "플러그인 받기", "Runner 확인", "Runner 받기", "공지 받기", "서버 확인", "서버 받기", "서버 준비"],
            j["steps"]!.AsArray().Select(x => (string)x!["name"]!).ToArray());
        Assert.All(j["steps"]!.AsArray(), x => Assert.Equal("대기", (string?)x!["state"]));
    }

    [Fact]
    public async Task Button_returns_at_once_and_the_check_runs_in_the_background()
    {
        using var e = new SettingsEnv(gitHubUrl: "http://127.0.0.1:1"); // nothing listens: the first step fails
        await e.Login();
        var r = await e.Post("/admin/check-updates");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); // the redirect to the settings page
        Assert.Equal("/admin/settings", r.RequestMessage!.RequestUri!.AbsolutePath);
        Assert.Contains("id=\"chk\"", await r.Content.ReadAsStringAsync());

        JsonNode? j = null;
        for (int i = 0; i < 300; i++)
        {
            j = JsonNode.Parse(await e.Http.GetStringAsync("/admin/check-updates/status"));
            if ((int)j!["run"]! > 0 && !(bool)j["running"]!) break;
            await Task.Delay(50);
        }
        Assert.False((bool)j!["running"]!);
        Assert.Equal("실패", (string?)j["steps"]![0]!["state"]);
    }
}
