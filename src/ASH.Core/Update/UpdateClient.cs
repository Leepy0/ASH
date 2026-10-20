using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ASH.Core.Update;

public sealed record FetchedManifest(byte[] Json, byte[]? Sig, ReleaseManifest Manifest);

/// <summary>Talks to ASH.Server. Failures (timeout, connection, HTTP status) are thrown as-is for the caller to catch.</summary>
public sealed class UpdateClient : IDisposable
{
    private readonly string _base;
    private readonly TimeSpan _timeout;
    private readonly HttpClient _http;

    /// <param name="timeout">Applies to the manifest requests and to waiting for download response headers (the body is limited only by the caller's token).</param>
    public UpdateClient(Uri server, TimeSpan timeout, HttpMessageHandler? handler = null)
    {
        _base = server.AbsoluteUri.TrimEnd('/');
        _timeout = timeout;
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    public void Dispose() => _http.Dispose();

    public async Task<FetchedManifest> GetManifestAsync(CancellationToken ct = default)
    {
        using var cts = Limited(ct);
        byte[] json;
        using (var r = await _http.GetAsync(_base + "/api/ash/version", HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false))
        {
            r.EnsureSuccessStatusCode();
            json = await r.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        byte[]? sig = null;
        using (var r = await _http.GetAsync(_base + "/api/ash/version.sig", HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false))
        {
            if (r.StatusCode != HttpStatusCode.NotFound)
            {
                r.EnsureSuccessStatusCode();
                sig = await r.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }
        return new FetchedManifest(json, sig, ReleaseManifest.Parse(json));
    }

    /// <summary>Downloads to &lt;destDir&gt;\&lt;file&gt;.part, checks size and SHA-256, then renames. A mismatch deletes the file.</summary>
    /// <exception cref="InvalidDataException">Unsafe file name, or size/hash mismatch.</exception>
    public async Task<string> DownloadVerifiedAsync(ReleaseManifest m, string destDir, CancellationToken ct = default)
    {
        string name = m.File;
        if (string.IsNullOrEmpty(name) || name == "." || name.Contains("..") || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("배포 정보의 file 이름이 올바르지 않습니다: " + name);
        Directory.CreateDirectory(destDir);
        string final = Path.Combine(destDir, name), part = final + ".part";
        try
        {
            string hex;
            long total = 0;
            using (var cts = Limited(ct))
            using (var r = await _http.GetAsync(_base + "/download/" + Uri.EscapeDataString(name), HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
            {
                r.EnsureSuccessStatusCode();
                cts.CancelAfter(Timeout.InfiniteTimeSpan); // headers arrived in time; the body is bounded by size/ct only
                using var sha = SHA256.Create();
                using var src = await r.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                var buf = new byte[81920];
                int n;
                while ((n = await src.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > m.Size) throw new InvalidDataException($"설치 파일이 배포 정보의 크기({m.Size})보다 큽니다.");
                    sha.TransformBlock(buf, 0, n, null, 0);
                    dst.Write(buf, 0, n);
                }
                sha.TransformFinalBlock(buf, 0, 0);
                hex = ToHex(sha.Hash!);
            }
            if (total != m.Size) throw new InvalidDataException($"설치 파일 크기가 다릅니다 (받음 {total}, 배포 정보 {m.Size}).");
            if (!string.Equals(hex, m.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("설치 파일의 SHA-256이 배포 정보와 다릅니다.");
            if (File.Exists(final)) File.Delete(final);
            File.Move(part, final);
            return final;
        }
        catch
        {
            try { if (File.Exists(part)) File.Delete(part); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private CancellationTokenSource Limited(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        return cts;
    }

    private static string ToHex(byte[] b)
    {
        var sb = new StringBuilder(b.Length * 2);
        foreach (var x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }
}
