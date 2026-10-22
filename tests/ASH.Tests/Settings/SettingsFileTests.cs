using System;
using System.IO;
using ASH.Core.Export;
using ASH.Core.Settings;
using Xunit;

namespace ASH.Tests.Settings;

public class SettingsFileTests
{
    [Fact] public void AmHome_Default_Is_Clean() { var h = new ProjectSettings().AmHome; Assert.Equal(@"C:\AutoMod\12_6", h); Assert.DoesNotContain(h, char.IsControl); }
    [Fact] public void Missing_File_Gives_Defaults() => Assert.Equal(new ProjectSettings(), SettingsFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".tsv")));
    [Fact] public void Save_Then_Load_RoundTrips()
    {
        var p = Path.GetTempFileName(); var s = new ProjectSettings(SafetyGap: 0.8, ClaimDist: 1.0, Origin: new(100,200), ModelName: "plantA", AmHome: @"D:\AM");
        SettingsFile.Save(s, p); Assert.Equal(s, SettingsFile.Load(p));
    }
    [Fact] public void Lines_Then_Parse_RoundTrips_And_Bad_Field_Names_Its_Key()
    {
        var s = new ProjectSettings(BlockReach: 3.5, Origin: new(-1.25, 7), ModelName: "m2", Units: "Feet");
        Assert.Equal(s, SettingsFile.Parse(SettingsFile.Lines(s)));
        var bad = SettingsFile.Lines(s).Select(l => l.StartsWith("OriginY\t") ? "OriginY\t1,5" : l);
        Assert.Contains(" OriginY ", Assert.Throws<FormatException>(() => SettingsFile.Parse(bad)).Message); // the dialog focuses the field by this
    }
    [Theory]
    [InlineData("plantA", true)] [InlineData("", false)] [InlineData("a b", false)] [InlineData("..", false)] [InlineData(@"x\y", false)] [InlineData("c:", false)]
    public void Model_Name_Must_Be_A_Plain_Folder_Name(string name, bool ok) => Assert.Equal(ok, ASH.Core.Model.ModelBuilder.IsValidModelName(name));
    [Fact] public void ParseGroups_Rejects_Bad_MaxCount()
    {
        Assert.Throws<FormatException>(() => SettingsFile.ParseGroups("G1:Tug:0:1.0:2.0:1.0"));
        Assert.Throws<FormatException>(() => SettingsFile.ParseGroups("G1:Tug:1.5:1.0:2.0:1.0"));
        Assert.Throws<FormatException>(() => SettingsFile.ParseGroups("G1:Tug:2:0:2.0:1.0"));
    }
    [Fact] public void Unknown_Key_Ignored_And_Blank_Gives_Null()
    {
        var p = Path.GetTempFileName();
        File.WriteAllLines(p, new[] { "Bogus\t42", "ClaimDist\t", "SafetyGap\t0.7", "ModelName\t" });
        Assert.Equal(new ProjectSettings(SafetyGap: 0.7, ClaimDist: null), SettingsFile.Load(p));
    }
    [Fact] public void Groups_Parse_And_RoundTrip()
    {
        var p = Path.GetTempFileName(); var s = new ProjectSettings(Groups: "G1:Tug:5:1.2:2.0:1.0;G2:Fork:3:0.8:2.5:1.2");
        SettingsFile.Save(s, p);
        Assert.Equal(new[] { new VehicleGroupSpec("G1", "Tug", 5, 1.2, 2.0, 1.0), new VehicleGroupSpec("G2", "Fork", 3, 0.8, 2.5, 1.2) },
            SettingsFile.ParseGroups(SettingsFile.Load(p).Groups));
    }
    [Fact] public void UnitScale_Mm_Drawing_Gives_1000_Units_Per_Meter() => Assert.Equal(1000, UnitScale.FromSettings(new ProjectSettings(Scale: 0.001)), 9);
    [Theory]
    [InlineData("SafetyGap	0")] [InlineData("VehicleLength	-1")] [InlineData("VehicleWidth	0")] [InlineData("Scale	0")]
    public void Non_Positive_Sizes_Rejected(string line)
    {
        var p = Path.GetTempFileName(); File.WriteAllLines(p, new[] { line });
        Assert.Throws<FormatException>(() => SettingsFile.Load(p));
    }
}
