using ASH.Core.Install;

namespace ASH.Tests.Install;

public class UserDataPathsTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";
    private static FakeRegistry Reg(string? profile)
    {
        var r = new FakeRegistry();
        r.Add(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\S-1-5-18", "ProfileImagePath", @"C:\Windows\system32\config\systemprofile");
        if (profile != null) r.Add(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\" + Sid, "ProfileImagePath", profile);
        return r;
    }

    [Fact] public void Same_User_Uses_Process_AppData() => Assert.Equal(@"C:\Users\me\AppData\Roaming", UserDataPaths.AppData(null, false, Reg(null), @"C:\Users\me\AppData\Roaming"));

    [Fact] public void Other_User_Uses_Their_Profile() =>
        Assert.Equal(@"D:\Users\cad\AppData\Roaming", UserDataPaths.AppData(Sid, false, Reg(@"D:\Users\cad"), @"C:\Users\admin\AppData\Roaming"));

    [Fact] public void Other_User_Without_Profile_Is_Null_Never_The_Admins() => Assert.Null(UserDataPaths.AppData(Sid, false, Reg(null), @"C:\Users\admin\AppData\Roaming"));

    [Fact] public void No_User_Is_Null() => Assert.Null(UserDataPaths.AppData(null, true, Reg(null), @"C:\Windows\system32\config\systemprofile\AppData\Roaming"));
}
