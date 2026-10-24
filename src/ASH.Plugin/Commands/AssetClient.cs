using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using ASH.Core.Cel;
using ASH.Core.Settings;

namespace ASH.Plugin.Commands;

/// <summary>Plugin side of the asset catalog: the HTTP and parsing live in Core (AssetCatalogClient); this only resolves the address and keeps the .cel in the model's .arc.</summary>
internal static class AssetClient
{
    /// <summary>The client for the configured server, or null when no address is set.</summary>
    public static AssetCatalogClient? Open(string serverUrl) => UserSettings.TryNormalizeUrl(serverUrl, out var url) ? new AssetCatalogClient(new Uri(url)) : null;

    /// <summary>Downloads the asset's .cel with <paramref name="client"/> (3 s per call), checks it has at least one box, then saves it to &lt;model&gt;.arc\assets\&lt;name&gt;.cel and returns its text.
    /// null with <paramref name="why"/> when the name is not in the catalog, the file is unusable (nothing is written) or the disk fails.
    /// HttpRequestException/TimeoutException from an unreachable server are thrown so the caller can stop asking.</summary>
    public static string? Fetch(AssetCatalogClient client, string arcDir, string typeName, string assetName, out string? why)
    {
        why = null;
        try
        {
            var cel = client.FetchCel(typeName, assetName);
            if (cel == null) { why = "카탈로그에 없는 이름입니다"; return null; }
            try { if (CelReader.ReadBoxes(cel).Count == 0) { why = "에셋 파일에 상자가 없습니다"; return null; } }
            catch (FormatException) { why = "에셋 파일을 읽을 수 없습니다"; return null; } // never ship a bad file in the AGVSEND package
            var dir = Path.Combine(arcDir, "assets");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(assetName.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c));
            File.WriteAllText(Path.Combine(dir, safe + ".cel"), cel, new System.Text.UTF8Encoding(false));
            return cel;
        }
        catch (System.Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
        {
            why = e.Message;
            return null;
        }
    }

    /// <summary>Asset names of all types from GET /api/catalog/assets; null when the server is unset or unreachable.</summary>
    public static System.Collections.Generic.IReadOnlyList<string>? AssetNames(string serverUrl)
    {
        var c = Open(serverUrl);
        if (c == null) return null;
        try { return c.Names(); }
        catch (System.Exception e) when (e is HttpRequestException || e is TimeoutException || e is OperationCanceledException || e is InvalidDataException) { return null; }
    }
}
