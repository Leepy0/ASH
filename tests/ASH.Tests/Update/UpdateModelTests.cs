using System;
using System.IO;
using System.Text;
using ASH.Core.Update;
using Xunit;

namespace ASH.Tests.Update;

public class UpdateModelTests
{
    private static SemVersion V(string s) { Assert.True(SemVersion.TryParse(s, out var v)); return v!; }
    private static readonly string Sha = new string('a', 64);

    [Fact] public void SemVer_Order()
    {
        string[] asc = { "0.1.0", "0.1.1", "0.2.0-rc1", "0.2.0", "0.10.0", "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0" };
        for (int i = 0; i + 1 < asc.Length; i++)
        {
            Assert.True(V(asc[i]).CompareTo(V(asc[i + 1])) < 0, asc[i] + " < " + asc[i + 1]);
            Assert.True(V(asc[i + 1]).CompareTo(V(asc[i])) > 0);
        }
        Assert.Equal(0, V("1.2.3").CompareTo(V("1.2.3+build5")));
        Assert.True(V("1.0.0-9").CompareTo(V("1.0.0-10")) < 0);
        Assert.True(V("1.0.0-9").CompareTo(V("1.0.0-a")) < 0);
    }

    [Theory]
    [InlineData("")] [InlineData("1.2")] [InlineData("1.2.3.4")] [InlineData("a.b.c")] [InlineData("1.2.3-")] [InlineData("1.-2.3")] [InlineData("v1.2.3")]
    [InlineData("01.2.3")] [InlineData("1.02.3")] [InlineData("1.2.03")] [InlineData("1.2.3-01")] [InlineData("1.2.3-a..b")] [InlineData("1.2.3-.a")] [InlineData("1.2.3-a.")]
    [InlineData("1.2.3+")] [InlineData("1.2.3+a..b")] [InlineData("1.2.3-a_b")] [InlineData(" 1.2.3")] [InlineData("1.2.3 ")] [InlineData("1.2.3-é")] [InlineData("99999999999.0.0")]
    public void SemVer_Rejects_Bad(string s) => Assert.False(SemVersion.TryParse(s, out _));

    [Theory] [InlineData("0.0.0")] [InlineData("1.2.3-0")] [InlineData("1.2.3-0.a-b.10")] [InlineData("1.2.3+01.x")] [InlineData("1.2.3-rc1+sha.5114f85")]
    public void SemVer_Accepts_Good(string s) => Assert.True(SemVersion.TryParse(s, out _));

    private static ReleaseManifest M(string version = "0.2.0", string min = "0.1.0") => new() { Version = version, MinSupported = min, File = "ASH-Setup-" + version + ".exe", Sha256 = Sha, Size = 10 };

    [Fact] public void Evaluate_UpToDate() => Assert.Equal(UpdateState.UpToDate, UpdateCheck.Evaluate("0.2.0", M()).State);
    [Fact] public void Evaluate_Available() => Assert.Equal(UpdateState.Available, UpdateCheck.Evaluate("0.1.5", M()).State);
    [Fact] public void Evaluate_Prerelease_Is_Older_Than_Release() => Assert.Equal(UpdateState.Available, UpdateCheck.Evaluate("0.2.0-rc1", M()).State);
    [Fact] public void Evaluate_Below_Minimum()
    {
        var s = UpdateCheck.Evaluate("0.0.9", M());
        Assert.Equal(UpdateState.BelowMinimum, s.State);
        Assert.Contains("0.1.0", s.Message);
    }
    [Fact] public void Evaluate_ServerOlder() => Assert.Equal(UpdateState.ServerOlder, UpdateCheck.Evaluate("0.3.0", M()).State);
    [Fact] public void Evaluate_Blank_Min_Means_None() => Assert.Equal(UpdateState.Available, UpdateCheck.Evaluate("0.0.1", M(min: "")).State);
    [Fact] public void Evaluate_Other_Product_Or_Newer_Format_Is_Incompatible()
    {
        var other = M(); other.Product = "Other";
        Assert.Equal(UpdateState.Incompatible, UpdateCheck.Evaluate("0.0.1", other).State);
        var newer = M(); newer.FormatVersion = ReleaseManifest.CurrentFormatVersion + 1;
        Assert.Equal(UpdateState.Incompatible, UpdateCheck.Evaluate("0.0.1", newer).State);
        var noProduct = ReleaseManifest.Parse(Encoding.UTF8.GetBytes("{\"version\":\"9.0.0\",\"file\":\"ASH-Setup-9.0.0.exe\",\"sha256\":\"" + Sha + "\",\"size\":1}"));
        Assert.Equal(UpdateState.Incompatible, UpdateCheck.Evaluate("0.0.1", noProduct).State);
    }
    [Fact] public void Evaluate_Bad_Versions_Throw()
    {
        Assert.Throws<ArgumentException>(() => UpdateCheck.Evaluate("x", M()));
        Assert.Throws<InvalidDataException>(() => UpdateCheck.Evaluate("0.1.0", M(version: "junk")));
        Assert.Throws<InvalidDataException>(() => UpdateCheck.Evaluate("0.1.0", M(min: "junk")));
    }

    [Fact] public void Manifest_RoundTrips_With_CamelCase_Keys()
    {
        var m = new ReleaseManifest { Version = "0.2.0", MinSupported = "0.1.0", File = "ASH-Setup-0.2.0.exe", Sha256 = Sha, Size = 1234567890123, Published = "2026-10-21T01:02:03Z", Notes = "한글 \"노트\" / x" };
        byte[] json = m.ToJson();
        string text = Encoding.UTF8.GetString(json);
        foreach (var k in new[] { "product", "version", "minSupported", "formatVersion", "rid", "file", "sha256", "size", "published", "notes" })
            Assert.Contains("\"" + k + "\":", text);
        var back = ReleaseManifest.Parse(json);
        Assert.Equal("ASH", back.Product);
        Assert.Equal(1, back.FormatVersion);
        Assert.Equal("win-x64", back.Rid);
        Assert.Equal(m.Version, back.Version);
        Assert.Equal(m.MinSupported, back.MinSupported);
        Assert.Equal(m.File, back.File);
        Assert.Equal(m.Sha256, back.Sha256);
        Assert.Equal(m.Size, back.Size);
        Assert.Equal(m.Published, back.Published);
        Assert.Equal(m.Notes, back.Notes);
    }

    private static byte[] J(string file = "ASH-Setup-1.0.0.exe", string? sha = null, long size = 3) =>
        Encoding.UTF8.GetBytes("{\"version\":\"1.0.0\",\"file\":\"" + file + "\",\"sha256\":\"" + (sha ?? Sha) + "\",\"size\":" + size + ",\"future\":true}");

    [Fact] public void Manifest_Parse_Ignores_Unknown_Fields_And_Rejects_Garbage()
    {
        Assert.Equal("1.0.0", ReleaseManifest.Parse(J()).Version);
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Parse(Encoding.UTF8.GetBytes("not json")));
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Parse(Encoding.UTF8.GetBytes("{\"version\":\"1.0.0\"}")));
    }

    [Fact] public void Manifest_Parse_Validates_File_Sha_And_Size()
    {
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Parse(J(file: "setup.exe")));
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Parse(J(file: "..\\\\ASH-Setup-1.exe")));
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Parse(J(sha: new string('A', 64))));   // uppercase
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Parse(J(sha: new string('a', 63))));
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Parse(J(sha: new string('g', 64))));
        Assert.Throws<InvalidDataException>(() => ReleaseManifest.Parse(J(size: -1)));
        Assert.Equal(0, ReleaseManifest.Parse(J(size: 0)).Size);
    }

    [Fact] public void FormatPublished_Is_Utc_Iso()
        => Assert.Equal("2026-10-21T01:02:03Z", ReleaseManifest.FormatPublished(new DateTime(2026, 10, 21, 1, 2, 3, DateTimeKind.Utc)));
}
