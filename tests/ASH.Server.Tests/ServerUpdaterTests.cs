using System.Text;

namespace ASH.Server.Tests;

public class ServerUpdaterTests
{
    private static byte[] J(string s) => Encoding.UTF8.GetBytes(s);
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void Server_manifest_parses()
    {
        var m = ServerManifest.Parse(J($"{{\"product\":\"ASH.Server\",\"version\":\"0.3.0\",\"file\":\"ASH-Server-0.3.0-win-x64.zip\",\"sha256\":\"{Sha}\",\"size\":42}}"));
        Assert.Equal(new ServerManifest("0.3.0", "ASH-Server-0.3.0-win-x64.zip", Sha, 42), m);
    }

    [Theory]
    [InlineData("ASH", "0.3.0", "ASH-Server-0.3.0-win-x64.zip", 42)]          // the setup manifest is not a server manifest
    [InlineData("ASH.Server", "x", "ASH-Server-0.3.0-win-x64.zip", 42)]
    [InlineData("ASH.Server", "0.3.0", "..\\ASH-Server-0.3.0-win-x64.zip", 42)]
    [InlineData("ASH.Server", "0.3.0", "ASH-Setup-0.3.0.exe", 42)]
    [InlineData("ASH.Server", "0.3.0", "ASH-Server-0.3.0-win-x64.zip", 0)]
    public void Bad_server_manifest_is_rejected(string product, string version, string file, long size)
        => Assert.Throws<InvalidDataException>(() => ServerManifest.Parse(J(
            $"{{\"product\":\"{product}\",\"version\":\"{version}\",\"file\":\"{file.Replace("\\", "\\\\")}\",\"sha256\":\"{Sha}\",\"size\":{size}}}")));

    [Fact]
    public void Not_json_is_invalid_data() => Assert.Throws<InvalidDataException>(() => ServerManifest.Parse(J("<html>")));

    [Theory]
    [InlineData("\"C:\\Program Files\\ASH.Server\\ASH.Server.exe\"", true)]
    [InlineData("\"c:\\program files\\ash.server\\ash.server.exe\" --x", true)]
    [InlineData("C:\\Program Files\\ASH.Server\\ASH.Server.exe", true)]
    [InlineData("\"D:\\Other\\ASH.Server.exe\"", false)]
    [InlineData(null, false)]
    public void Service_image_path_is_compared_to_this_exe(string? image, bool same)
        => Assert.Equal(same, ServerUpdater.SameExe(image, @"C:\Program Files\ASH.Server\ASH.Server.exe"));

    [Theory]
    [InlineData("http://0.0.0.0:8082", "http://127.0.0.1:8082/healthz/version")]
    [InlineData("http://[::]:8082", "http://127.0.0.1:8082/healthz/version")]
    [InlineData("http://*:9000", "http://127.0.0.1:9000/healthz/version")]
    [InlineData("http://localhost:8082", "http://127.0.0.1:8082/healthz/version")]
    [InlineData("http://10.1.2.3:8082", "http://10.1.2.3:8082/healthz/version")]
    [InlineData("https://0.0.0.0:443", null)]
    public void Health_url_comes_from_the_listen_address(string address, string? expected)
        => Assert.Equal(expected, ServerUpdater.HealthUrl(new[] { address }));

    [Fact]
    public void Batch_arguments_quote_paths()
        => Assert.Equal("12 \"C:\\Program Files\\ASH.Server\" \"C:\\Program Files\\ASH.Server\\update\\v0.3.0\" \"ASH.Server\" \"-\" 0.3.0",
            ServerUpdater.Arguments(12, @"C:\Program Files\ASH.Server\", @"C:\Program Files\ASH.Server\update\v0.3.0", "ASH.Server", null, "0.3.0"));

    [Fact]
    public void Batch_is_ascii() => Assert.All(ServerUpdater.BatchText, c => Assert.True(c < 128, $"non-ASCII {(int)c}"));

    [Fact]
    public async Task Ticket_not_claimed_in_time_is_withdrawn()
    {
        var d = Directory.CreateTempSubdirectory("ash-upd-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(d, ServerUpdater.TicketFile), "");
            Assert.False(await ServerUpdater.WaitStartedAsync(d, TimeSpan.FromMilliseconds(200)));
            Assert.False(File.Exists(Path.Combine(d, ServerUpdater.TicketFile))); // a late batch finds no ticket and quits
            File.WriteAllText(Path.Combine(d, ServerUpdater.StartedFile), "");
            Assert.True(await ServerUpdater.WaitStartedAsync(d, TimeSpan.FromSeconds(1)));
        }
        finally { Directory.Delete(d, true); }
    }

    [Theory]
    [InlineData("http://localhost:9999/o/r", "http://localhost:9999/o/r")]
    [InlineData("http://127.0.0.1:9999/o/r/", "http://127.0.0.1:9999/o/r")]
    public void Plain_http_base_url_only_on_this_pc(string given, string expected) => Assert.Equal(expected, GitHubSync.Url(given));
}
