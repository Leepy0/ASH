using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ASH.Core.Settings;

/// <summary>
/// Per-PC (not per-drawing) settings at %APPDATA%\ASH\ash.user.tsv, "key\tvalue" lines. Unknown keys are ignored.
/// LayerColors = the ASH layer colours the user changed ("AGV_PATH=1;AGV_CP_STANDBY=30", see LayerColors), kept per PC, not per drawing.
/// SYSTEM deployments (no user profile to write to) keep ServerUrl in %ProgramData%\ASH\ash.machine.tsv instead; the user file wins when its ServerUrl is non-empty.
/// </summary>
public sealed record UserSettings(string ServerUrl = "", double FilletRadiusM = 0, string LayerColors = "")
{
    public const string FileName = "ash.user.tsv";

    /// <summary>&lt;appData&gt;\ASH\ash.user.tsv</summary>
    public static string PathIn(string appData) => System.IO.Path.Combine(appData, "ASH", FileName);

    public const string MachineFileName = "ash.machine.tsv";

    /// <summary>%ProgramData%\ASH\ash.machine.tsv</summary>
    public static string MachinePath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ASH", MachineFileName);

    public static string DefaultPath => PathIn(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    /// <summary>
    /// Missing file gives defaults. IO errors are thrown for the caller to handle.
    /// With no <paramref name="path"/> (the plugin's call) an empty user ServerUrl falls back to the machine file;
    /// an explicit <paramref name="path"/> reads that file alone unless <paramref name="machinePath"/> is given too.
    /// </summary>
    public static UserSettings Load(string? path = null, string? machinePath = null)
    {
        machinePath ??= path == null ? MachinePath : null;
        var u = Read(path ?? DefaultPath);
        return u.ServerUrl.Length == 0 && machinePath != null ? u with { ServerUrl = Read(machinePath).ServerUrl } : u; // the machine file only supplies the address
    }

    static UserSettings Read(string path)
    {
        if (!File.Exists(path)) return new UserSettings();
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in File.ReadAllLines(path))
        {
            int tab = line.IndexOf('\t');
            if (tab > 0) kv[line.Substring(0, tab).Trim()] = line.Substring(tab + 1).Trim();
        }
        double.TryParse(kv.TryGetValue(nameof(FilletRadiusM), out var r) ? r : "", NumberStyles.Float, CultureInfo.InvariantCulture, out var radius);
        // normalised on the way in: a hand-edited value never reaches the layers as anything but valid colours
        var colors = ASH.Core.Layout.LayerColors.Format(ASH.Core.Layout.LayerColors.Parse(kv.TryGetValue(nameof(LayerColors), out var lc) ? lc : ""));
        return new UserSettings(kv.TryGetValue(nameof(ServerUrl), out var u) ? u : "", radius > 0 ? radius : 0, colors);
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var text = $"{nameof(ServerUrl)}\t{ServerUrl}\r\n";
        if (FilletRadiusM > 0) text += $"{nameof(FilletRadiusM)}\t{FilletRadiusM.ToString("R", CultureInfo.InvariantCulture)}\r\n";
        var colors = ASH.Core.Layout.LayerColors.Format(ASH.Core.Layout.LayerColors.Parse(LayerColors));
        if (colors.Length > 0) text += $"{nameof(LayerColors)}\t{colors}\r\n";
        File.WriteAllText(path, text, new UTF8Encoding(false));
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
