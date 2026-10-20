using System.IO;

namespace ASH.Core.Install;

public static class UserDataPaths
{
    /// <summary>
    /// The %APPDATA% of the user the install is for, mirroring the HKCU mapping. No <paramref name="sid"/> = the process's own user
    /// (<paramref name="currentAppData"/>); with one, the profile folder comes from ProfileList (null when unknown).
    /// <paramref name="noUser"/> (SYSTEM, unknown user) gives null: nobody's AppData to write to.
    /// shortcut: assumes the default roaming location &lt;profile&gt;\AppData\Roaming (no folder redirection), upgrade if a site redirects AppData.
    /// </summary>
    public static string? AppData(string? sid, bool noUser, IRegistryReader reg, string currentAppData)
    {
        if (noUser) return null;
        if (sid == null) return currentAppData;
        var profile = reg.GetString("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + sid, "ProfileImagePath");
        return string.IsNullOrWhiteSpace(profile) ? null : Path.Combine(profile!.Trim(), "AppData", "Roaming");
    }
}
