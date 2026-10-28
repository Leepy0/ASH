using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using ASH.Core.Cel;
using ASH.Core.Settings;

namespace ASH.Plugin.Commands;

/// <summary>Plugin side of the asset catalog: the HTTP and parsing live in Core (AssetCatalogClient); this only resolves the address (SourceBuild.Overrides keeps the .cel in the model's .arc).</summary>
internal static class AssetClient
{
    /// <summary>The client for the configured server, or null when no address is set.</summary>
    public static AssetCatalogClient? Open(string serverUrl) => UserSettings.TryNormalizeUrl(serverUrl, out var url) ? new AssetCatalogClient(new Uri(url)) : null;

    /// <summary>Asset names of all types from GET /api/catalog/assets; null when the server is unset or unreachable.</summary>
    public static System.Collections.Generic.IReadOnlyList<string>? AssetNames(string serverUrl)
    {
        var c = Open(serverUrl);
        if (c == null) return null;
        try { return c.Names(); }
        catch (System.Exception e) when (e is HttpRequestException || e is TimeoutException || e is OperationCanceledException || e is InvalidDataException) { return null; }
    }
}
