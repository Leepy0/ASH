using ASH.Core.Settings;

namespace ASH.Tests.Settings;

public class UserSettingsTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "ash-us-" + Guid.NewGuid().ToString("N"), "ASH", "ash.user.tsv");

    [Fact] public void Missing_File_Gives_Default() => Assert.Equal("", UserSettings.Load(Tmp()).ServerUrl);

    [Fact] public void Save_Creates_Folder_And_RoundTrips()
    {
        var p = Tmp();
        new UserSettings("http://srv:5080").Save(p);
        Assert.Equal("http://srv:5080", UserSettings.Load(p).ServerUrl);
        Assert.Equal("ServerUrl\thttp://srv:5080\r\n", File.ReadAllText(p));
    }

    [Fact] public void LayerColors_RoundTrip_Beside_The_Other_Keys()
    {
        var p = Tmp();
        new UserSettings("http://srv:5080", 0.5, "AGV_PATH=1;AGV_CP_STANDBY=30").Save(p);
        var u = UserSettings.Load(p);
        Assert.Equal("AGV_PATH=1;AGV_CP_STANDBY=30", u.LayerColors);
        Assert.Equal("http://srv:5080", u.ServerUrl);
        Assert.Equal(0.5, u.FilletRadiusM);
        Assert.Contains("LayerColors\tAGV_PATH=1;AGV_CP_STANDBY=30\r\n", File.ReadAllText(p));
    }

    [Fact] public void LayerColors_Default_Writes_No_Line_And_Bad_Text_Is_Normalised()
    {
        var p = Tmp();
        new UserSettings("http://a:1").Save(p);
        Assert.DoesNotContain("LayerColors", File.ReadAllText(p));
        Assert.Equal("", UserSettings.Load(p).LayerColors);
        new UserSettings("http://a:1", 0, "junk;AGV_DIR=8;AGV_PATH=300;agv_block=1").Save(p); // AGV_DIR=8 is the default, AGV_PATH=300 is no colour
        Assert.Equal("AGV_BLOCK=1", UserSettings.Load(p).LayerColors);
    }

    [Fact] public void LayerColors_Survive_Saving_Another_Key()
    {
        var p = Tmp();
        new UserSettings("", 0, "AGV_PATH=1").Save(p);
        (UserSettings.Load(p) with { ServerUrl = "http://x:2" }).Save(p);
        Assert.Equal("AGV_PATH=1", UserSettings.Load(p).LayerColors);
    }

    [Fact] public void SaveCheck_Is_On_By_Default_Writes_A_Line_Only_When_Off_And_Survives_Other_Saves()
    {
        var p = Tmp();
        new UserSettings("http://a:1").Save(p);
        Assert.DoesNotContain("SaveCheck", File.ReadAllText(p));
        Assert.True(UserSettings.Load(p).SaveCheck);
        (UserSettings.Load(p) with { SaveCheck = false }).Save(p);
        Assert.Contains("SaveCheck\t0\r\n", File.ReadAllText(p));
        Assert.False(UserSettings.Load(p).SaveCheck);
        (UserSettings.Load(p) with { ServerUrl = "http://x:2" }).Save(p);
        Assert.False(UserSettings.Load(p).SaveCheck);
        File.WriteAllText(p, "SaveCheck\toff\n");
        Assert.False(UserSettings.Load(p).SaveCheck);
    }

    [Fact] public void Load_Ignores_Unknown_Keys_And_Blank_Lines()
    {
        var p = Tmp();
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "\nFoo\tbar\nserverurl\t http://a:1 \nno tab line\n");
        Assert.Equal("http://a:1", UserSettings.Load(p).ServerUrl);
    }

    [Fact] public void Load_Falls_Back_To_Machine_File_When_User_Url_Is_Empty()
    {
        var u = Tmp(); var m = Tmp();
        new UserSettings("http://machine:5080").Save(m);
        Assert.Equal("http://machine:5080", UserSettings.Load(u, m).ServerUrl); // no user file
        new UserSettings("").Save(u);
        Assert.Equal("http://machine:5080", UserSettings.Load(u, m).ServerUrl); // empty user value
    }

    [Fact] public void Load_User_Url_Wins_Over_Machine_File()
    {
        var u = Tmp(); var m = Tmp();
        new UserSettings("http://user:1").Save(u);
        new UserSettings("http://machine:2").Save(m);
        Assert.Equal("http://user:1", UserSettings.Load(u, m).ServerUrl);
    }

    [Fact] public void Load_Explicit_Path_Alone_Ignores_Machine_File() => Assert.Equal("", UserSettings.Load(Tmp()).ServerUrl);

    [Fact] public void MachinePath_Is_ProgramData_ASH_File() =>
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ASH", "ash.machine.tsv"), UserSettings.MachinePath);

    [Fact] public void PathIn_Is_AppData_ASH_File() => Assert.Equal(Path.Combine("C:\\x", "ASH", "ash.user.tsv"), UserSettings.PathIn("C:\\x"));

    [Theory]
    [InlineData("http://server:5080", true)] [InlineData(" https://srv/ ", true)] [InlineData("http://10.0.0.5:5080/base", true)]
    [InlineData("", false)] [InlineData("server:5080", false)] [InlineData("ftp://srv", false)] [InlineData("srv", false)]
    [InlineData("http://a b", false)] [InlineData("http://a\tb", false)] [InlineData("http://user:pw@srv:5080", false)] [InlineData("http://srv:5080/?x=1", false)] [InlineData("http://srv:5080/#f", false)] [InlineData("http://srv?", false)] [InlineData("http://srv#", false)] [InlineData("file:///c:/x", false)] [InlineData(null, false)]
    public void TryNormalizeUrl(string? input, bool ok)
    {
        Assert.Equal(ok, UserSettings.TryNormalizeUrl(input, out var u));
        if (ok) Assert.Equal(input!.Trim(), u);
    }
}
