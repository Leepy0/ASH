using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Install;
using Microsoft.Win32;

namespace ASH.Installer;

/// <summary>
/// Registry64 view only: ZwcadLocator/InstallPlanner already probe SOFTWARE\WOW6432Node paths explicitly,
/// and HKCU\Software is not redirected, so a second Registry32 view would only duplicate those reads.
/// With <c>userSid</c>, the "HKCU" alias (reads, writes, deletes) maps to HKEY_USERS\&lt;sid&gt; so plan targets stay "HKCU\…".
/// </summary>
sealed class Win32Registry : IRegistryReader, IRegistryWriter
{
    readonly string? _userSid;

    public Win32Registry(string? userSid = null) => _userSid = userSid;

    /// <summary>True when HKEY_USERS\&lt;sid&gt; is loaded (the user is logged on).</summary>
    public static bool UserHiveLoaded(string sid)
    {
        using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
        using var k = users.OpenSubKey(sid);
        return k != null;
    }

    RegistryKey Base(string hive, bool writable)
    {
        if (hive == "HKCU" && _userSid != null)
        {
            using var users = RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64);
            return users.OpenSubKey(_userSid, writable) ?? throw new InvalidOperationException("사용자 하이브가 로드되지 않음: " + _userSid);
        }
        return RegistryKey.OpenBaseKey(
            hive == "HKLM" ? RegistryHive.LocalMachine : hive == "HKCU" ? RegistryHive.CurrentUser : throw new ArgumentException("hive " + hive),
            RegistryView.Registry64);
    }

    T Read<T>(string hive, string path, Func<RegistryKey, T> f, T empty)
    {
        using var b = Base(hive, false);
        using var k = b.OpenSubKey(path);
        return k == null ? empty : f(k);
    }

    public string[] SubKeys(string hive, string path) => Read(hive, path, k => k.GetSubKeyNames(), new string[0]);

    public string? GetString(string hive, string path, string name) => Read(hive, path, k => k.GetValue(name) as string, null);

    public IEnumerable<(string Name, object Value)> Values(string hive, string path) =>
        Read(hive, path, k => k.GetValueNames().Select(n => (n, k.GetValue(n))).Where(p => p.Item2 != null).Select(p => (p.n, p.Item2!)).ToList(),
             new List<(string, object)>());

    public void SetString(string hive, string path, string name, string value) => Write(hive, path, k => k.SetValue(name, value, RegistryValueKind.String));

    public void SetDword(string hive, string path, string name, int value) => Write(hive, path, k => k.SetValue(name, value, RegistryValueKind.DWord));

    public void DeleteKey(string hive, string path)
    {
        using var b = Base(hive, true);
        b.DeleteSubKeyTree(path, false);
    }

    void Write(string hive, string path, Action<RegistryKey> f)
    {
        using var b = Base(hive, true);
        using var k = b.CreateSubKey(path, true);
        f(k);
    }
}
