using ASH.Core.Input;
using ASH.Core.Layout;

namespace ASH.Tests.Input;

public class InputValidatorTests
{
    static readonly string[] Cps = { "S", "L", "U" };

    static ProjectInput Good()
    {
        var p = new ProjectInput();
        p.AgvTypes.Add(new AgvType("T1", 1, 1, 0, 0, 1, 20, 20, 0, 0, 1, 1, "Blue"));
        p.Groups.Add(new AgvGroup("G1", "T1", 1, 3, new List<string> { "S" }, "Nearest"));
        p.Materials.Add(new MaterialInfo("M1", "ST1", "G1", 60, 1, 10, 4, 1, null, null, true));
        p.Routes.Add(new Route("M1", "L", "U", null, null, null, 0));
        p.Stations.Add(new StationInfo("ST1", "U", 0));
        return p;
    }

    static List<Finding> Warnings(ProjectInput p, IReadOnlyDictionary<string, double>? len = null) =>
        InputValidator.Validate(p, Cps, len).Where(x => x.Code == FindingCode.InputWarning).ToList();

    [Fact]
    public void Validate_Warns_Call_Threshold_At_Or_Below_Safety_Stock()
    {
        var p = Good();
        p.Materials[0] = p.Materials[0] with { CallThreshold = 2 };          // Settings SafetyStock 2
        var w = Assert.Single(Warnings(p));
        Assert.Contains("호출 임계값이 안전재고 이하", w.Message); Assert.Equal("Materials!3", w.EntityId);
        p.Materials[0] = p.Materials[0] with { CallThreshold = 2, SafetyStock = 1 }; // material override wins
        Assert.Empty(Warnings(p));
    }

    [Fact]
    public void Validate_Warns_Supply_Cycle_Over_Container_Time()
    {
        // container lasts 60 s × 10 / 1 = 600 s; cycle = 400 m / 1 m/s + 20 + 20 = 440 s
        Assert.Empty(Warnings(Good(), new Dictionary<string, double> { ["M1"] = 400 }));
        var w = Assert.Single(Warnings(Good(), new Dictionary<string, double> { ["M1"] = 600 }));
        Assert.Contains("자재 M1: 한 사이클 공급 상한 초과 — 대수와 무관", w.Message);
        Assert.Empty(Warnings(Good(), new Dictionary<string, double>()));       // unknown length: skipped
    }

    [Fact]
    public void Validate_Warns_Ignored_Fields_Once_Per_Row()
    {
        var p = Good();
        p.AgvTypes[0] = p.AgvTypes[0] with { SpeedEmpty = 1.2, Capacity = 2, MTTR = 30 };
        p.Groups[0] = p.Groups[0] with { Dispatch = "FIFO" };
        p.Stations[0] = p.Stations[0] with { BufferCapacity = 4 };
        p.Ext.Add(new ExtHook("FExtOnCall", true, ""));
        p.Settings = p.Settings with { TrafficMode = "CPOnly", WorkDefinition = "CallToReturn" };
        var w = Warnings(p);
        Assert.Equal(6, w.Count);
        // SpeedEmpty and Capacity are modelled now; MTTR without MTBF is the one AGV_Types warning
        Assert.All(w.Where(x => x.EntityId != "AGV_Types!3"), x => Assert.Contains("v2 미반영", x.Message));
        Assert.Contains("MTBF와 MTTR을 둘 다", Assert.Single(w, x => x.EntityId == "AGV_Types!3").Message);
    }

    [Theory]
    [InlineData("type", "타입1")] [InlineData("type", "T-1")] [InlineData("group", "그룹")] [InlineData("group", "1G")]
    [InlineData("material", "자재1")] [InlineData("material", "M\"1")]
    public void Validate_Rejects_Names_AutoMod_Cannot_Use(string which, string name)
    {
        var p = Good();
        if (which == "type") { p.AgvTypes[0] = p.AgvTypes[0] with { Type = name }; p.Groups[0] = p.Groups[0] with { Type = name }; }
        if (which == "group") { p.Groups[0] = p.Groups[0] with { Group = name }; p.Materials[0] = p.Materials[0] with { Group = name }; }
        if (which == "material") { p.Materials[0] = p.Materials[0] with { Material = name }; p.Routes[0] = p.Routes[0] with { Material = name }; }
        var e = Assert.Single(InputValidator.Validate(p, Cps), x => x.Code == FindingCode.InputError);
        Assert.Contains("AutoMod에서 쓸 수 없습니다", e.Message);
        Assert.Equal(which == "type" ? "AGV_Types!3" : which == "group" ? "AGV_Groups!3" : "Materials!3", e.EntityId);
    }

    [Fact]
    public void Validate_Flags_Material_Without_Route_And_Duplicate_Route()
    {
        var p = Good();
        p.RouteSheet = "Routes"; // an older workbook: a Routes sheet of its own can hold two rows for one material
        p.Routes.Add(p.Routes[0] with { Row = 5 });
        Assert.Equal("Routes!5", Assert.Single(InputValidator.Validate(p, Cps)).EntityId);
        p.Routes.Clear();
        var e = Assert.Single(InputValidator.Validate(p, Cps));
        Assert.Equal(("Materials!3", FindingCode.InputError), (e.EntityId, e.Code));
        Assert.Contains("Routes 행이 없습니다", e.Message);
    }

    [Fact] public void Validate_Passes_Consistent_Input() => Assert.Empty(InputValidator.Validate(Good(), Cps));

    [Fact]
    public void Validate_Flags_Route_Cp_Missing()
    {
        var p = Good();
        p.Routes[0] = p.Routes[0] with { SupplyToCP = "NOPE", BufferCP = "ALSO" };
        var f = InputValidator.Validate(p, Cps);
        Assert.Equal(2, f.Count);
        Assert.All(f, x => { Assert.Equal(FindingCode.InputError, x.Code); Assert.Equal("Materials!3", x.EntityId); }); // the route is on the material's row
        p.RouteSheet = "Routes";
        Assert.All(InputValidator.Validate(p, Cps), x => Assert.Equal("Routes!3", x.EntityId));
        Assert.Empty(InputValidator.Validate(p, null)); // no layout -> CP check skipped
    }

    [Fact]
    public void Validate_Warns_Call_Threshold_Below_Cycle_Need()
    {
        var p = Good();
        p.Materials[0] = p.Materials[0] with { CallThreshold = 0, UnitsPerCycle = 2, UnitsPerContainer = 1 };
        var f = InputValidator.Validate(p, Cps);
        Assert.All(f, x => Assert.Equal(FindingCode.InputWarning, x.Code));
        Assert.Single(f, x => x.Message.Contains("호출 임계값이 1회 소비량보다 작습니다"));
    }

    [Fact]
    public void Validate_Allows_A_Type_Shared_By_Two_Groups()
    {
        var p = Good();
        p.Groups.Add(new AgvGroup("G2", "T1", 1, 3, new List<string> { "S" }, "Nearest"));
        Assert.DoesNotContain(InputValidator.Validate(p, Cps), x => x.Code == FindingCode.InputError);
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
        p.RouteSheet = "Routes";
        p.Routes.Add(p.Routes[0] with { Material = "NOPE", Row = 7 });
        Assert.Equal("Routes!7", Assert.Single(InputValidator.Validate(p, Cps)).EntityId);
    }

    [Fact]
    public void Validate_Flags_Blank_Required_Fields()
    {
        var p = Good();
        p.Routes[0] = p.Routes[0] with { SupplyFromCP = "", SupplyToCP = " " };
        p.Stations[0] = p.Stations[0] with { CPName = "" };
        foreach (var cps in new IReadOnlyCollection<string>?[] { Cps, null })
        {
            var f = InputValidator.Validate(p, cps);
            Assert.Equal(2, f.Count(x => x.EntityId == "Materials!3"));
            Assert.Contains(f, x => x.EntityId == "Stations!3");
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
    public void Validate_Ignores_Station_Name()
    {
        var p = Good();
        p.Materials[0] = p.Materials[0] with { Station = "NOSUCH" };
        var f = InputValidator.Validate(p, Cps);
        Assert.DoesNotContain(f, x => x.Code == FindingCode.InputError); // Station is a memo: no Stations row needed
    }
}
