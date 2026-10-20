using System.Collections.Generic;

namespace ASH.Core.Install;

/// <summary>hive is "HKLM" or "HKCU"; path is the key path below it.</summary>
public interface IRegistryReader
{
    string[] SubKeys(string hive, string path);
    string? GetString(string hive, string path, string name);
    IEnumerable<(string Name, object Value)> Values(string hive, string path);
    bool KeyExists(string hive, string path);
}

public interface IRegistryWriter
{
    void SetString(string hive, string path, string name, string value);
    void SetDword(string hive, string path, string name, int value);
    void DeleteKey(string hive, string path);
}
