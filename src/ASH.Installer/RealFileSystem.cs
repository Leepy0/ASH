using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using ASH.Core.Install;

namespace ASH.Installer;

/// <param name="secureRoot">
/// Folders directly below it (the %ProgramData%\ASH\&lt;ver&gt; fallback) are recreated with a locked ACL before the first copy:
/// %ProgramData% lets Users create files, and HKLM LOADER points here, so a user-writable DLL would run for every user.
/// </param>
sealed class RealFileSystem(string? secureRoot = null) : IFileSystem, IFileOps
{
    readonly HashSet<string> _secured = new(StringComparer.OrdinalIgnoreCase);

    public bool DirectoryExists(string path) => Directory.Exists(path);
    public bool FileExists(string path) => File.Exists(path);
    public IEnumerable<string> EnumerateDirectories(string parent, string pattern) =>
        Directory.Exists(parent) ? Directory.GetDirectories(parent, pattern) : new string[0];

    public void Copy(string source, string dest)
    {
        var dir = Path.GetDirectoryName(dest)!;
        if (secureRoot != null && string.Equals(Path.GetDirectoryName(dir), secureRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && _secured.Add(dir))
        {
            // recreate so no pre-planted file or ACE survives
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir, LockedAcl());
        }
        else Directory.CreateDirectory(dir);
        File.Copy(source, dest, true);
    }

    // inheritance off; Administrators/SYSTEM full control, Users read & execute
    internal static DirectorySecurity LockedAcl()
    {
        var s = new DirectorySecurity();
        s.SetAccessRuleProtection(true, false);
        const InheritanceFlags inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        void Allow(WellKnownSidType who, FileSystemRights rights) => s.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(who, null), rights, inherit, PropagationFlags.None, AccessControlType.Allow));
        Allow(WellKnownSidType.BuiltinAdministratorsSid, FileSystemRights.FullControl);
        Allow(WellKnownSidType.LocalSystemSid, FileSystemRights.FullControl);
        Allow(WellKnownSidType.BuiltinUsersSid, FileSystemRights.ReadAndExecute);
        return s;
    }

    public void Unblock(string path) => Installer.Unblock.File(path);

    public void DeleteDir(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
}
