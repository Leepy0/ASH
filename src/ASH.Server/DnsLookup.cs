using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ASH.Server;

/// <summary>
/// Minimal DNS client (A records over UDP) so the GitHub check can use DNS servers other than the PC's (GitHub:Dns),
/// for intranets whose DNS does not resolve github.com. Not a general resolver: no cache, no IPv6, no TCP fallback (GitHub answers fit in UDP).
/// </summary>
public static class DnsLookup
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>"8.8.8.8, 1.1.1.1" (comma, semicolon or space separated) -> addresses; empty = use the PC's DNS.</summary>
    /// <exception cref="ArgumentException">An entry is not an IP address.</exception>
    public static IPAddress[] ParseServers(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return [];
        var list = new List<IPAddress>();
        foreach (var part in s.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            list.Add(IPAddress.TryParse(part, out var ip) ? ip : throw new ArgumentException($"GitHub:Dns에는 DNS 서버 IP 주소만 적습니다(예: 8.8.8.8, 10.0.0.1): {part}"));
        return list.ToArray();
    }

    /// <summary>IPv4 addresses of host from the first server that answers with any; empty when none does.</summary>
    public static async Task<IPAddress[]> ResolveAsync(string host, IReadOnlyList<IPAddress> servers, CancellationToken ct)
    {
        foreach (var server in servers)
        {
            try
            {
                var id = (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue + 1);
                using var udp = new UdpClient(server.AddressFamily);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(Timeout);
                await udp.SendAsync(Query(id, host), new IPEndPoint(server, 53), cts.Token);
                while (true)
                {
                    var r = await udp.ReceiveAsync(cts.Token);
                    if (!r.RemoteEndPoint.Address.Equals(server)) continue; // ignore stray packets
                    var a = ParseAnswer(r.Buffer, id);
                    if (a == null) continue; // not our query id
                    if (a.Length > 0) return a;
                    break;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { } // this server timed out: next
            catch (SocketException) { }
            catch (InvalidDataException) { }
        }
        return [];
    }

    /// <summary>Standard query, recursion desired, one question: host IN A.</summary>
    internal static byte[] Query(ushort id, string host)
    {
        var b = new List<byte> { (byte)(id >> 8), (byte)id, 0x01, 0x00, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in host.TrimEnd('.').Split('.'))
        {
            var l = Encoding.ASCII.GetBytes(label);
            if (l.Length is 0 or > 63) throw new ArgumentException("DNS 이름이 올바르지 않습니다: " + host);
            b.Add((byte)l.Length);
            b.AddRange(l);
        }
        b.AddRange(new byte[] { 0, 0, 1, 0, 1 });
        return b.ToArray();
    }

    /// <summary>A records in the answer section (CNAME chains included: every A answer counts). Null when the id does not match.</summary>
    /// <exception cref="InvalidDataException">Malformed message or an error response code.</exception>
    internal static IPAddress[]? ParseAnswer(byte[] m, ushort id)
    {
        if (m.Length < 12) throw new InvalidDataException("DNS 응답이 짧습니다.");
        if ((m[0] << 8 | m[1]) != id) return null;
        if ((m[2] & 0x80) == 0) throw new InvalidDataException("DNS 응답이 아닙니다.");
        if ((m[3] & 0x0F) != 0) throw new InvalidDataException("DNS 오류 코드 " + (m[3] & 0x0F));
        int qd = m[4] << 8 | m[5], an = m[6] << 8 | m[7], p = 12;
        for (int i = 0; i < qd; i++) p = SkipName(m, p) + 4;
        var list = new List<IPAddress>();
        for (int i = 0; i < an; i++)
        {
            p = SkipName(m, p);
            Need(m, p, 10);
            int type = m[p] << 8 | m[p + 1], len = m[p + 8] << 8 | m[p + 9];
            p += 10;
            Need(m, p, len);
            if (type == 1 && len == 4) list.Add(new IPAddress(m.AsSpan(p, 4)));
            p += len;
        }
        return list.ToArray();
    }

    private static int SkipName(byte[] m, int p)
    {
        while (true)
        {
            Need(m, p, 1);
            int len = m[p];
            if ((len & 0xC0) == 0xC0) { Need(m, p, 2); return p + 2; } // compression pointer ends the name
            if (len == 0) return p + 1;
            p += 1 + len;
        }
    }

    private static void Need(byte[] m, int p, int n)
    {
        if (p < 0 || p + n > m.Length) throw new InvalidDataException("DNS 응답이 잘렸습니다.");
    }
}
