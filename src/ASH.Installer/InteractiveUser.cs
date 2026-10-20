using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace ASH.Installer;

/// <summary>
/// The user logged on to this session. Differs from the process token user when a standard user elevates with
/// another admin account (UAC credentials prompt); then HKCU would be the admin's hive, not the CAD user's.
/// </summary>
static class InteractiveUser
{
    const int WtsUserName = 5, WtsDomainName = 7, WtsCurrentSession = -1;

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool WTSQuerySessionInformationW(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr memory);

    public static (string Account, SecurityIdentifier Sid)? Find()
    {
        try
        {
            string? user = Query(WtsUserName), domain = Query(WtsDomainName);
            if (!string.IsNullOrEmpty(user))
            {
                var account = string.IsNullOrEmpty(domain) ? user! : domain + "\\" + user;
                return (account, (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier)));
            }
        }
        catch { /* fall through to explorer.exe owner */ }
        try
        {
            var session = Process.GetCurrentProcess().SessionId;
            using var q = new ManagementObjectSearcher($"SELECT * FROM Win32_Process WHERE Name='explorer.exe' AND SessionId={session}");
            foreach (ManagementObject p in q.Get())
            {
                var r = p.InvokeMethod("GetOwnerSid", null, null);
                if (r?["Sid"] is string s && s.Length > 0)
                {
                    var sid = new SecurityIdentifier(s);
                    string account;
                    try { account = sid.Translate(typeof(NTAccount)).Value; } catch { account = s; }
                    return (account, sid);
                }
            }
        }
        catch { /* unknown: caller keeps HKCU */ }
        return null;
    }

    static string? Query(int infoClass)
    {
        if (!WTSQuerySessionInformationW(IntPtr.Zero, WtsCurrentSession, infoClass, out var buf, out _)) return null;
        try { return Marshal.PtrToStringUni(buf); }
        finally { WTSFreeMemory(buf); }
    }
}
