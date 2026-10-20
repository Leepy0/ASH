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
}
