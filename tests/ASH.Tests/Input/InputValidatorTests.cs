using ASH.Core.Input;
using ASH.Core.Layout;

namespace ASH.Tests.Input;

public class InputValidatorTests
{
    static readonly string[] Cps = { "S", "L", "U" };

    static ProjectInput Good()
    {
        var p = new ProjectInput();
        p.AgvTypes.Add(new AgvType("T1", "G1", 1, 1, .5, .5, 2, 20, 20, 0, 0, 1, 1, "Blue"));
        p.Groups.Add(new AgvGroup("G1", "T1", 1, 3, new List<string> { "S" }, "Nearest"));
        p.Materials.Add(new MaterialInfo("M1", "ST1", "G1", 60, 1, 10, 2, 1, null, null, true));
        p.Routes.Add(new Route("M1", "L", "U", null, null, null, 0));
        p.Stations.Add(new StationInfo("ST1", "U", 4));
        return p;
    }

    [Fact] public void Validate_Passes_Consistent_Input() => Assert.Empty(InputValidator.Validate(Good(), Cps));

    [Fact]
    public void Validate_Flags_Route_Cp_Missing()
    {
        var p = Good();
        p.Routes[0] = p.Routes[0] with { SupplyToCP = "NOPE", BufferCP = "ALSO" };
        var f = InputValidator.Validate(p, Cps);
        Assert.Equal(2, f.Count);
        Assert.All(f, x => { Assert.Equal(FindingCode.InputError, x.Code); Assert.Equal("Routes!3", x.EntityId); });
        Assert.Empty(InputValidator.Validate(p, null)); // no layout -> CP check skipped
    }

    [Fact]
    public void Validate_Flags_Unknown_Group_And_Type()
    {
        var p = Good();
        p.Groups[0] = p.Groups[0] with { Type = "TX" };
        p.Materials[0] = p.Materials[0] with { Group = "GX", TaktTime = 0 };
        var ids = InputValidator.Validate(p, Cps).Select(x => x.EntityId).ToList();
        Assert.Contains("AGV_Groups!3", ids);
        Assert.Equal(2, ids.Count(i => i == "Materials!3"));
    }

    [Fact]
    public void Validate_Flags_Counts_And_Duplicates()
    {
        var p = Good();
        p.Groups[0] = p.Groups[0] with { MinCount = 3, MaxCount = 2 };
        p.Stations.Add(p.Stations[0]);
        var f = InputValidator.Validate(p, Cps);
        Assert.Contains(f, x => x.EntityId == "AGV_Groups!3");
        Assert.Contains(f, x => x.EntityId == "Stations!4");
    }

    [Fact]
    public void Validate_Reports_Source_Row_After_Blank_Row()
    {
        var p = Good();
        p.Routes[0] = p.Routes[0] with { Material = "NOPE", Row = 7 };
        Assert.Equal("Routes!7", Assert.Single(InputValidator.Validate(p, Cps)).EntityId);
    }

    [Fact]
    public void Validate_Flags_Blank_Required_Fields()
    {
        var p = Good();
        p.Routes[0] = p.Routes[0] with { SupplyFromCP = "", SupplyToCP = " " };
        p.Stations[0] = p.Stations[0] with { CPName = "" };
        p.Materials[0] = p.Materials[0] with { Station = "" };
        foreach (var cps in new IReadOnlyCollection<string>?[] { Cps, null })
        {
            var f = InputValidator.Validate(p, cps);
            Assert.Equal(2, f.Count(x => x.EntityId == "Routes!3"));
            Assert.Contains(f, x => x.EntityId == "Stations!3");
            Assert.Contains(f, x => x.EntityId == "Materials!3");
        }
    }

    [Fact]
    public void Validate_Flags_Enums_And_Settings()
    {
        var p = Good();
        p.Settings = p.Settings with { InitStockMode = "X", TrafficMode = "Y", WorkDefinition = "Z", Replications = 0, WarmupSec = 99999 };
        p.Groups[0] = p.Groups[0] with { Dispatch = "Random" };
        p.Materials[0] = p.Materials[0] with { TaktTime = double.NaN };
        var f = InputValidator.Validate(p, Cps);
        Assert.Equal(7, f.Count(x => x.Code == FindingCode.InputError));
    }

    [Fact]
    public void Validate_Warns_On_Type_Group_Mismatch_And_Unknown_Station()
    {
        var p = Good();
        p.AgvTypes[0] = p.AgvTypes[0] with { Group = "OTHER" };
        p.Materials[0] = p.Materials[0] with { Station = "NOSUCH" };
        var f = InputValidator.Validate(p, Cps);
        Assert.Contains(f, x => x.Code == FindingCode.InputWarning && x.EntityId == "AGV_Types!3");
        Assert.Contains(f, x => x.Code == FindingCode.InputError && x.EntityId == "Materials!3");
    }
}
