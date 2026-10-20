using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ASH.Core.Settings;

/// <summary>Per-PC (not per-drawing) settings at %APPDATA%\ASH\ash.user.tsv, "key\tvalue" lines. Unknown keys are ignored.</summary>
public sealed record UserSettings(string ServerUrl = "")
{
    public const string FileName = "ash.user.tsv";

    /// <summary>&lt;appData&gt;\ASH\ash.user.tsv</summary>
    public static string PathIn(string appData) => System.IO.Path.Combine(appData, "ASH", FileName);

    public static string DefaultPath => PathIn(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>Missing file gives defaults. IO errors are thrown for the caller to handle.</summary>
    public static UserSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        if (!File.Exists(path)) return new UserSettings();
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path))
        {
            int tab = line.IndexOf('\t');
            if (tab > 0) kv[line.Substring(0, tab).Trim()] = line.Substring(tab + 1).Trim();
        }
        return new UserSettings(kv.TryGetValue(nameof(ServerUrl), out var u) ? u : "");
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"{nameof(ServerUrl)}\t{ServerUrl}\r\n", new UTF8Encoding(false));
    }

    /// <summary>Absolute http/https URL without whitespace, control characters, userinfo, query or fragment; <paramref name="url"/> is the trimmed text.</summary>
    public static bool TryNormalizeUrl(string? input, out string url)
    {
        url = (input ?? "").Trim();
        if (url.Length == 0) return false;
        foreach (var c in url) if (char.IsWhiteSpace(c) || char.IsControl(c)) return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) && u.Host.Length > 0
            && u.UserInfo.Length == 0 && url.IndexOfAny(new[] { '?', '#', '@' }) < 0; // "?"/"#" even when empty, "@" = userinfo
    }
}
