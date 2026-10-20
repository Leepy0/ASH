using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ASH.Core.Update;

public sealed record UpdateResult(FetchedManifest Fetched, UpdateStatus Status);

/// <summary>Public key loading and the fetch + verify + evaluate sequence shared by the startup check and AGVUPDATE.</summary>
public static class UpdateFlow
{
    /// <summary>
    /// <paramref name="resource"/> is the embedded update-key.pub (null when the build has none). No resource: unsigned mode (null, null).
    /// A resource that does not parse gives (null, error): the caller must abort, never fall back to unsigned.
    /// </summary>
    public static (ManifestSignature? Key, string? Error) LoadKey(Stream? resource)
    {
        if (resource == null) return (null, null);
        try
        {
            using var r = new StreamReader(resource, Encoding.UTF8);
            return (new ManifestSignature(r.ReadToEnd()), null);
        }
        catch (Exception e) when (e is ArgumentException || e is IOException) { return (null, "서명 키 오류: " + e.Message); }
    }

    /// <exception cref="InvalidDataException">With a key, the signature over the received bytes is missing or wrong.</exception>
    public static async Task<UpdateResult> CheckAsync(UpdateClient client, ManifestSignature? key, string current, CancellationToken ct = default)
    {
        var f = await client.GetManifestAsync(ct).ConfigureAwait(false);
        if (key != null && (f.Sig == null || !key.Verify(f.Json, f.Sig))) throw new InvalidDataException("서명 확인 실패");
        return new UpdateResult(f, UpdateCheck.Evaluate(current, f.Manifest));
    }

    /// <summary>Startup check for a background thread. Never throws: returns the line to show, or null when there is nothing to say.</summary>
    public static async Task<string?> StartupMessageAsync(Uri server, TimeSpan timeout, ManifestSignature? key, string current, HttpMessageHandler? handler = null)
    {
        try
        {
            using var c = new UpdateClient(server, timeout, handler);
            var r = await CheckAsync(c, key, current).ConfigureAwait(false);
            return UpdateMessages.Startup(r.Status, r.Fetched.Manifest);
        }
        catch (Exception e) { return UpdateMessages.CheckFailed(e); }
    }
}
