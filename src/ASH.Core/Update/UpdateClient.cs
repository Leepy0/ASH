using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ASH.Core.Update;

/// <summary>The exact manifest bytes the server sent, their signature (if any) and the manifest parsed from those same bytes.</summary>
public sealed class FetchedManifest
{
    public byte[] Json { get; }
    public byte[]? Sig { get; }
    /// <summary>Always <c>ReleaseManifest.Parse(Json)</c>; it cannot disagree with <see cref="Json"/>.</summary>
    public ReleaseManifest Manifest { get; }

    internal FetchedManifest(byte[] json, byte[]? sig)
    {
        Json = json;
        Sig = sig;
        Manifest = ReleaseManifest.Parse(json);
    }
}

/// <summary>Talks to ASH.Server. Failures (timeout, connection, HTTP status) are thrown as-is for the caller to catch.</summary>
public sealed class UpdateClient : IDisposable
{
    private const int MaxManifestBytes = 1 << 20;

    private readonly string _base;
    private readonly TimeSpan _timeout;
    private readonly HttpClient _http;

    /// <param name="timeout">Each manifest request, the wait for download response headers, and each download read (idle timeout, not a total limit).</param>
    public UpdateClient(Uri server, TimeSpan timeout, HttpMessageHandler? handler = null)
    {
        _base = server.AbsoluteUri.TrimEnd('/');
        _timeout = timeout;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            MaxResponseContentBufferSize = MaxManifestBytes,
        };
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
        return new FetchedManifest(json, sig);
    }

    /// <summary>
    /// Signature policy lives here. With <paramref name="key"/>, a missing or wrong signature over the exact manifest bytes is rejected;
    /// with null the caller is on the unsigned path (SHA-256 only) and must warn. The manifest is always re-parsed from <c>f.Json</c>.
    /// Downloads to &lt;destDir&gt;\&lt;file&gt;.part, checks size and SHA-256, then renames; a mismatch deletes the file.
    /// </summary>
    /// <exception cref="InvalidDataException">Signature check failed, unsafe file name, or size/hash mismatch.</exception>
    /// <exception cref="IOException">The final name is an installer that is still running (Delete+Move cannot replace it).</exception>
    public async Task<string> DownloadVerifiedAsync(FetchedManifest f, ManifestSignature? key, string destDir, CancellationToken ct = default)
    {
        if (key != null && (f.Sig == null || !key.Verify(f.Json, f.Sig))) throw new InvalidDataException("서명 확인 실패");
        var m = ReleaseManifest.Parse(f.Json); // validates file name and sha256 format
        Directory.CreateDirectory(destDir);
        string final = Path.Combine(destDir, m.File), part = final + ".part";
        try
        {
            string hex;
            long total = 0;
            using (var cts = Limited(ct))
            using (var r = await _http.GetAsync(_base + "/download/" + Uri.EscapeDataString(m.File), HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
            {
                r.EnsureSuccessStatusCode();
                // disposing the response aborts a stalled body read (net48 ignores the token while reading)
                using var reg = cts.Token.Register(() => r.Dispose());
                try
                {
                    using var sha = SHA256.Create();
                    using var src = await r.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    using var dst = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                    var buf = new byte[81920];
                    while (true)
                    {
                        cts.CancelAfter(_timeout); // idle timeout per read
                        int n = await src.ReadAsync(buf, 0, buf.Length, cts.Token).ConfigureAwait(false);
                        if (n <= 0) break;
                        total += n;
                        if (total > m.Size) throw new InvalidDataException($"설치 파일이 배포 정보의 크기({m.Size})보다 큽니다.");
                        sha.TransformBlock(buf, 0, n, null, 0);
                        dst.Write(buf, 0, n);
                    }
                    sha.TransformFinalBlock(buf, 0, 0);
                    hex = ToHex(sha.Hash!);
                }
                catch (Exception e) when (!(e is OperationCanceledException) && !(e is InvalidDataException) && cts.IsCancellationRequested)
                {
                    throw new OperationCanceledException("다운로드 응답이 없어 중단했습니다.", e, cts.Token); // ObjectDisposed/IO error caused by our own abort
                }
            }
            if (total != m.Size) throw new InvalidDataException($"설치 파일 크기가 다릅니다 (받음 {total}, 배포 정보 {m.Size}).");
            if (!string.Equals(hex, m.Sha256, StringComparison.Ordinal)) throw new InvalidDataException("설치 파일의 SHA-256이 배포 정보와 다릅니다.");
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
