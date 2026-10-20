using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Install;
using Microsoft.Win32;

namespace ASH.Installer;

/// <summary>
/// Registry64 view only: ZwcadLocator/InstallPlanner already probe SOFTWARE\WOW6432Node paths explicitly,
/// and HKCU\Software is not redirected, so a second Registry32 view would only duplicate those reads.
/// </summary>
sealed class Win32Registry : IRegistryReader, IRegistryWriter
{
    static RegistryKey Base(string hive) => RegistryKey.OpenBaseKey(
        hive == "HKLM" ? RegistryHive.LocalMachine : hive == "HKCU" ? RegistryHive.CurrentUser : throw new ArgumentException("hive " + hive),
        RegistryView.Registry64);

    static T Read<T>(string hive, string path, Func<RegistryKey, T> f, T empty)
    {
        using var b = Base(hive);
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
        using var b = Base(hive);
        b.DeleteSubKeyTree(path, false);
    }

    static void Write(string hive, string path, Action<RegistryKey> f)
    {
        using var b = Base(hive);
        using var k = b.CreateSubKey(path, true);
        f(k);
    }
}
