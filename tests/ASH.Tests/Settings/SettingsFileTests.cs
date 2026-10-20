using System;
using System.IO;
using ASH.Core.Export;
using ASH.Core.Settings;
using Xunit;

namespace ASH.Tests.Settings;

public class SettingsFileTests
{
    [Fact] public void Missing_File_Gives_Defaults() => Assert.Equal(new ProjectSettings(), SettingsFile.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid()+".tsv")));
    [Fact] public void Save_Then_Load_RoundTrips()
    {
        var p = Path.GetTempFileName(); var s = new ProjectSettings(SafetyGap: 0.8, ClaimDist: 1.0, Origin: new(100,200), ModelName: "plantA");
        SettingsFile.Save(s, p); Assert.Equal(s, SettingsFile.Load(p));
    }
    [Fact] public void Groups_Parse_And_RoundTrip()
    {
        var p = Path.GetTempFileName(); var s = new ProjectSettings(Groups: "G1:Tug:5:1.2:2.0:1.0;G2:Fork:3:0.8:2.5:1.2");
        SettingsFile.Save(s, p);
        Assert.Equal(new[] { new VehicleGroupSpec("G1", "Tug", 5, 1.2, 2.0, 1.0), new VehicleGroupSpec("G2", "Fork", 3, 0.8, 2.5, 1.2) },
            SettingsFile.ParseGroups(SettingsFile.Load(p).Groups));
    }
}
