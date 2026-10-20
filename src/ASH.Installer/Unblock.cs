using System.Runtime.InteropServices;

namespace ASH.Installer;

static class Unblock
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool DeleteFileW(string path);

    /// <summary>Removes the Zone.Identifier stream (the "blocked" flag); a missing stream is not an error.</summary>
    public static void File(string path) => DeleteFileW(path + ":Zone.Identifier");
}
