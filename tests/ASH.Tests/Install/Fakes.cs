using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Install;

namespace ASH.Tests.Install;

public sealed class FakeRegistry : IRegistryReader
{
    readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);

    // fullPath: "HKLM\SOFTWARE\..."; creates intermediate keys.
    public void Add(string fullPath, string name, object value)
    {
        var parts = fullPath.Split('\\');
        for (int i = 1; i <= parts.Length; i++)
        {
            var k = string.Join("\\", parts.Take(i));
            if (!_keys.ContainsKey(k)) _keys[k] = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        }
        _keys[fullPath][name] = value;
    }

    public string[] SubKeys(string hive, string path)
    {
        var prefix = hive + "\\" + path + "\\";
        return _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k.IndexOf('\\', prefix.Length) < 0)
                         .Select(k => k.Substring(prefix.Length)).ToArray();
    }

    public string? GetString(string hive, string path, string name) =>
        _keys.TryGetValue(hive + "\\" + path, out var v) && v.TryGetValue(name, out var o) ? o as string : null;

    public IEnumerable<(string Name, object Value)> Values(string hive, string path) =>
        _keys.TryGetValue(hive + "\\" + path, out var v) ? v.Select(p => (p.Key, p.Value)).ToList() : new List<(string, object)>();
}

public sealed class FakeFs : IFileSystem
{
    readonly HashSet<string> _dirs, _files;
    public FakeFs(IEnumerable<string> dirs, IEnumerable<string> files)
    {
        _dirs = new HashSet<string>(dirs, StringComparer.OrdinalIgnoreCase);
        _files = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
    }
    public bool DirectoryExists(string path) => _dirs.Contains(path);
    public bool FileExists(string path) => _files.Contains(path);
    public IEnumerable<string> EnumerateDirectories(string parent, string pattern)
    {
        // only "<prefix>*" patterns are needed
        var pre = pattern.TrimEnd('*');
        return _dirs.Where(d => string.Equals(System.IO.Path.GetDirectoryName(d), parent, StringComparison.OrdinalIgnoreCase)
                              && System.IO.Path.GetFileName(d).StartsWith(pre, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}

/// <summary>Records file and registry side effects; paths in <see cref="Denied"/> throw UnauthorizedAccessException on Copy.</summary>
public sealed class FakeOps : IFileOps, IRegistryWriter
{
    public readonly List<string> Log = new();
    public readonly HashSet<string> Denied = new(StringComparer.OrdinalIgnoreCase);
    public void Copy(string source, string dest)
    {
        if (Denied.Any(d => dest.StartsWith(d, StringComparison.OrdinalIgnoreCase))) throw new UnauthorizedAccessException("denied " + dest);
        Log.Add("copy " + dest);
    }
    public void Unblock(string path) => Log.Add("unblock " + path);
    public void DeleteDir(string path)
    {
        if (Denied.Contains(path)) throw new System.IO.IOException("locked " + path);
        Log.Add("rmdir " + path);
    }
    public void SetString(string hive, string path, string name, string value) => Log.Add($"sz {hive}|{path}|{name}={value}");
    public void SetDword(string hive, string path, string name, int value) => Log.Add($"dw {hive}|{path}|{name}={value}");
    public void DeleteKey(string hive, string path) => Log.Add($"delkey {hive}|{path}");
}
