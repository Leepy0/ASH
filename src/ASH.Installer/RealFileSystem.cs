using System.Collections.Generic;
using System.IO;
using ASH.Core.Install;

namespace ASH.Installer;

sealed class RealFileSystem : IFileSystem, IFileOps
{
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public IEnumerable<string> EnumerateDirectories(string parent, string pattern) =>
        Directory.Exists(parent) ? Directory.GetDirectories(parent, pattern) : new string[0];

    public void Copy(string source, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, true);
    }

    public void Unblock(string path) => Installer.Unblock.File(path);

    public void DeleteDir(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
