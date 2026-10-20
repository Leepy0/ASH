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

    [Fact] public void Load_Ignores_Unknown_Keys_And_Blank_Lines()
    {
        var p = Tmp();
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "\nFoo\tbar\nserverurl\t http://a:1 \nno tab line\n");
        Assert.Equal("http://a:1", UserSettings.Load(p).ServerUrl);
    }

    [Fact] public void PathIn_Is_AppData_ASH_File() => Assert.Equal(Path.Combine("C:\\x", "ASH", "ash.user.tsv"), UserSettings.PathIn("C:\\x"));

    [Theory]
    [InlineData("http://server:5080", true)] [InlineData(" https://srv/ ", true)] [InlineData("http://10.0.0.5:5080/base", true)]
    [InlineData("", false)] [InlineData("server:5080", false)] [InlineData("ftp://srv", false)] [InlineData("srv", false)]
    [InlineData("http://a b", false)] [InlineData("http://a\tb", false)] [InlineData("file:///c:/x", false)] [InlineData(null, false)]
    public void TryNormalizeUrl(string? input, bool ok)
    {
        Assert.Equal(ok, UserSettings.TryNormalizeUrl(input, out var u));
        if (ok) Assert.Equal(input!.Trim(), u);
    }
}
