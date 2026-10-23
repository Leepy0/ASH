using System.IO;

namespace ASH.Core.Film;

/// <summary>
/// The AutoView program under an AutoMod folder. 12.6 and later ship aview64.exe, 12.4 ships aview.exe (the same split as the build exe,
/// see AmBuilder). aview64 is preferred when both are there. Looked up per film run, so a changed AutoMod folder takes effect at once.
/// </summary>
public static class AutoViewExe
{
    static readonly string[] Names = { "aview64.exe", "aview.exe" };

    /// <summary>The first AutoView program found in &lt;amHome&gt;\bin, or null.</summary>
    public static string? Find(string amHome)
    {
        foreach (var n in Names)
        {
            var p = Path.Combine(amHome, "bin", n);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>The paths that were looked at, for the message when none is found.</summary>
    public static string Candidates(string amHome) =>
        string.Join(" 또는 ", System.Array.ConvertAll(Names, n => Path.Combine(amHome, "bin", n)));
}
