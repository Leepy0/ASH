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
    /// <summary>GET /api/catalog/assets, then /api/catalog/assets/{id}/asset.cel; 3 s per call; saves to &lt;model&gt;.arc\assets\&lt;name&gt;.cel and returns its text;
    /// null when the server is unset, unreachable, or the name is missing (<paramref name="why"/> says which; null when the name is simply not in the catalog).</summary>
    public static string? Fetch(string serverUrl, string arcDir, string typeName, string assetName, out string? why)
    {
        why = null;
        if (!UserSettings.TryNormalizeUrl(serverUrl, out var url)) { why = "서버 주소가 설정되지 않았습니다(AGVSERVER)"; return null; }
        try
        {
            var cel = new AssetCatalogClient(new Uri(url)).FetchCel(typeName, assetName);
            if (cel == null) { why = "카탈로그에 없는 이름입니다"; return null; }
            var dir = Path.Combine(arcDir, "assets");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(assetName.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c));
            File.WriteAllText(Path.Combine(dir, safe + ".cel"), cel, new System.Text.UTF8Encoding(false));
            return cel;
        }
        catch (System.Exception e) when (e is HttpRequestException || e is TimeoutException || e is OperationCanceledException || e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
        {
            why = e is HttpRequestException || e is TimeoutException || e is OperationCanceledException ? "서버에 연결하지 못했습니다" : e.Message;
            return null;
        }
    }

    /// <summary>Asset names of all types from GET /api/catalog/assets; null when the server is unset or unreachable.</summary>
    public static System.Collections.Generic.IReadOnlyList<string>? AssetNames(string serverUrl)
    {
        if (!UserSettings.TryNormalizeUrl(serverUrl, out var url)) return null;
        try { return new AssetCatalogClient(new Uri(url)).Names(); }
        catch (System.Exception e) when (e is HttpRequestException || e is TimeoutException || e is OperationCanceledException || e is InvalidDataException) { return null; }
    }
}
