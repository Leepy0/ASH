using System.Collections.Generic;

namespace ASH.Core.Install;

public interface IFileSystem
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    IEnumerable<string> EnumerateDirectories(string parent, string pattern);
}
