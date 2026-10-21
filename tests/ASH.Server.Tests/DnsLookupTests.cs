using System.Net;

namespace ASH.Server.Tests;

public class DnsLookupTests
{
    [Fact]
    public void Servers_parse_from_a_list()
        => Assert.Equal(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.1"), IPAddress.Parse("1.1.1.1") },
            DnsLookup.ParseServers(" 8.8.8.8, 10.0.0.1;1.1.1.1 "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_means_the_pcs_dns(string? s) => Assert.Empty(DnsLookup.ParseServers(s));

    [Theory]
    [InlineData("dns.google")]
    [InlineData("8.8.8.8:53")]
    public void Names_and_ports_are_rejected(string s) => Assert.Throws<ArgumentException>(() => DnsLookup.ParseServers(s));

    [Fact]
    public void Query_is_a_recursive_A_question()
        => Assert.Equal(new byte[] { 0x12, 0x34, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0, 6, (byte)'g', (byte)'i', (byte)'t', (byte)'h', (byte)'u', (byte)'b', 3, (byte)'c', (byte)'o', (byte)'m', 0, 0, 1, 0, 1 },
            DnsLookup.Query(0x1234, "github.com"));

    // response: question "a.io", answers: CNAME a.io -> b.io (name by pointer), then A b.io 1.2.3.4 and A 5.6.7.8
    private static byte[] Response(ushort id, byte rcode = 0)
    {
        var q = new byte[] { 1, (byte)'a', 2, (byte)'i', (byte)'o', 0, 0, 1, 0, 1 };
        var cname = new byte[] { 0xC0, 12, 0, 5, 0, 1, 0, 0, 0, 60, 0, 4, 1, (byte)'b', 0xC0, 14 };
        var a1 = new byte[] { 0xC0, 12, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 1, 2, 3, 4 };
        var a2 = new byte[] { 0xC0, 12, 0, 1, 0, 1, 0, 0, 0, 60, 0, 4, 5, 6, 7, 8 };
        var h = new byte[] { (byte)(id >> 8), (byte)id, 0x81, (byte)(0x80 | rcode), 0, 1, 0, 3, 0, 0, 0, 0 };
        return h.Concat(q).Concat(cname).Concat(a1).Concat(a2).ToArray();
    }

    [Fact]
    public void A_records_are_read_through_a_cname_chain()
        => Assert.Equal(new[] { IPAddress.Parse("1.2.3.4"), IPAddress.Parse("5.6.7.8") }, DnsLookup.ParseAnswer(Response(7), 7));

    [Fact]
    public void Other_query_id_is_ignored() => Assert.Null(DnsLookup.ParseAnswer(Response(7), 8));

    [Fact]
    public void Error_code_is_invalid_data() => Assert.Throws<InvalidDataException>(() => DnsLookup.ParseAnswer(Response(7, rcode: 3), 7));

    [Fact]
    public void Truncated_answer_is_invalid_data()
    {
        var r = Response(7);
        Assert.Throws<InvalidDataException>(() => DnsLookup.ParseAnswer(r[..^3], 7));
    }

    [Fact]
    public async Task Server_that_never_answers_gives_nothing_within_the_timeout()
    {
        var t = DateTime.UtcNow;
        Assert.Empty(await DnsLookup.ResolveAsync("github.com", new[] { IPAddress.Parse("127.0.0.1") }, default)); // nothing listens on 127.0.0.1:53
        Assert.True(DateTime.UtcNow - t < TimeSpan.FromSeconds(5));
    }
}
