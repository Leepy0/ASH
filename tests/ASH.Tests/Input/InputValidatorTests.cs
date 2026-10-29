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

    // ---- supply modes: cycle groups, CP lists, station materials ----

    static readonly string[] CpsUV = { "S", "L", "U", "V", "W" };

    // G2: a Cycle group without standby CPs (AGV_Groups row 4) that holds the named materials, each with a route of its own
    static ProjectInput WithCycleGroup(params string[] materials)
    {
        var p = Good();
        p.Groups.Add(new AgvGroup("G2", "T1", 1, 3, new List<string>(), "Nearest", SupplyMode: "Cycle"));
        foreach (var name in materials)
        {
            p.Materials.Add(p.Materials[0] with { Material = name, Group = "G2" });
            p.Routes.Add(p.Routes[0] with { Material = name });
        }
        return p;
    }

    // M1 (Materials row 3) read with several unload CPs: one station material M1@U, M1@V ... per CP, as the reader does it
    static ProjectInput WithStations(ProjectInput p, string material, params string[] unloads)
    {
        for (int i = 0; i < p.Materials.Count; i++) if (p.Materials[i].Material == material) p.Materials[i] = p.Materials[i] with { Row = 3 };
        for (int i = 0; i < p.Routes.Count; i++) if (p.Routes[i].Material == material) p.Routes[i] = p.Routes[i] with { Row = 3 };
        StationExpansion.Expand(p, new Dictionary<string, List<string>> { [material] = unloads.ToList() });
        return p;
    }

    [Theory]
    [InlineData(2)] [InlineData(0)]
    public void Validate_Rejects_A_Cycle_Group_Without_Exactly_One_Material(int count)
    {
        var p = WithCycleGroup(Enumerable.Range(1, count).Select(k => "C" + k).ToArray());
        var e = Assert.Single(InputValidator.Validate(p, Cps), x => x.Code == FindingCode.InputError);
        Assert.Equal($"그룹 'G2'은(는) 순환(Cycle)인데 자재가 {count}개입니다. 순환 그룹에는 자재 하나만 둡니다.", e.Message);
        Assert.Equal("AGV_Groups!4", e.EntityId);
    }

    [Fact]
    public void Validate_Counts_The_Station_Materials_Of_One_Row_As_One_Cycle_Material()
    {
        var p = WithStations(WithCycleGroup("C1"), "C1", "U", "V", "W");
        p.Groups[1] = p.Groups[1] with { SupplyMode = " cycle " }; // case and spaces do not matter
        Assert.Equal(3, p.Materials.Count(m => m.Group == "G2"));
        Assert.Empty(InputValidator.Validate(p, CpsUV));
    }

    [Fact]
    public void Validate_Warns_That_A_Cycle_Group_Ignores_Standby_And_Finish_CP()
    {
        var p = WithCycleGroup("C1");
        p.Groups[1] = p.Groups[1] with { StandbyCPs = new List<string> { "S" }, FinishCP = "S" };
        Assert.DoesNotContain(InputValidator.Validate(p, Cps), x => x.Code == FindingCode.InputError);
        var w = Warnings(p);
        Assert.Equal(new[] { "그룹 'G2'은(는) 순환이라 대기 CP를 쓰지 않습니다.", "그룹 'G2'은(는) 순환이라 완료 CP를 쓰지 않습니다." }, w.Select(x => x.Message));
        Assert.All(w, x => Assert.Equal("AGV_Groups!4", x.EntityId));
        Assert.Empty(Warnings(Good())); // a Call group keeps its standby CP without a word
    }

    [Theory]
    [InlineData("")] [InlineData("  ")] [InlineData("Call")] [InlineData("call ")]
    public void Validate_Accepts_Call_And_Blank_Supply_Mode(string mode)
    {
        var p = Good();
        p.Groups[0] = p.Groups[0] with { SupplyMode = mode };
        Assert.Empty(InputValidator.Validate(p, Cps));
    }

    [Fact]
    public void Validate_Rejects_An_Unknown_Supply_Mode()
    {
        var p = Good();
        p.Groups[0] = p.Groups[0] with { SupplyMode = "Loop" };
        var e = Assert.Single(InputValidator.Validate(p, Cps));
        Assert.Equal((FindingCode.InputError, "AGV_Groups!3", "그룹 'G1'의 공급 방식 'Loop'은(는) Call 또는 Cycle입니다."), (e.Code, e.EntityId, e.Message));
    }

    [Fact]
    public void Validate_Reports_Cp_List_Tokens_The_Reader_Rejected_On_Their_Cell()
    {
        var p = Good();
        p.CpListErrors.Add(("Materials", 3, "SupplyToCP", "L010-L001"));
        p.CpListErrors.Add(("Routes", 7, "SupplyFromCP", "L001-L2000"));
        var f = InputValidator.Validate(p, Cps);
        Assert.All(f, x => Assert.Equal(FindingCode.InputError, x.Code));
        Assert.Equal(new[] { "Materials!3", "Routes!7" }, f.Select(x => x.EntityId));
        // the column name leads, like the other cell messages: the Excel mark finds the cell by it
        Assert.Equal("SupplyToCP 'L010-L001': 범위가 잘못됐습니다(시작이 끝보다 크거나 1000개 초과).", f[0].Message);
        Assert.StartsWith("SupplyFromCP 'L001-L2000': ", f[1].Message);
    }

    [Fact]
    public void Validate_Rejects_A_Station_Material_Name_Another_Material_Already_Has()
    {
        var p = WithStations(Good(), "M1", "U", "V");
        p.Materials.Add(p.Materials[0] with { Material = "M1@V", Source = "", Row = 5 }); // typed by hand on another row
        p.Routes.Add(p.Routes[0] with { Material = "M1@V", Row = 5 });
        var e = Assert.Single(InputValidator.Validate(p, CpsUV), x => x.Message.StartsWith("공정 자재 이름"));
        Assert.Equal((FindingCode.InputError, "Materials!3", "공정 자재 이름 'M1@V'이(가) 다른 자재 이름과 같습니다."), (e.Code, e.EntityId, e.Message));
    }

    [Fact]
    public void Validate_Accepts_Several_Unload_Cps_On_A_Supply_Material()
    {
        Assert.Empty(InputValidator.Validate(WithStations(Good(), "M1", "U", "V", "W"), CpsUV));
    }

    static ProjectInput FedByConveyor(params string[] unloads)
    {
        var p = Good();
        p.Materials[0] = p.Materials[0] with { ConveyorFed = true };
        p.Conveyors.Add(new ConveyorInfo("C1", "M1", 60, 1, 0.3, null, null, 2)); // the Conveyors row keeps the Materials row's name
        return unloads.Length > 1 ? WithStations(p, "M1", unloads) : p;
    }

    [Fact]
    public void Validate_Joins_A_Conveyor_To_The_Row_Of_Its_Station_Materials_Without_Throwing()
    {
        var p = FedByConveyor("U", "V");
        var f = InputValidator.Validate(p, CpsUV);
        var e = Assert.Single(f, x => x.Code == FindingCode.InputError);
        Assert.Equal((FindingCode.InputError, "Materials!3", "자재 'M1'은(는) 컨베이어/반출 자재라 하차 CP를 하나만 씁니다."), (e.Code, e.EntityId, e.Message)); // once, not per station
        var w = Assert.Single(f, x => x.Code == FindingCode.InputWarning); // the T/T note, also once, under the Materials row's name
        Assert.Contains("자재 'M1': 컨베이어 'C1'가 소비하므로", w.Message);
    }

    [Fact]
    public void Validate_Keeps_A_Conveyor_Material_With_One_Unload_Cp_As_It_Was()
    {
        var f = InputValidator.Validate(FedByConveyor("U"), Cps);
        Assert.DoesNotContain(f, x => x.Code == FindingCode.InputError);
        Assert.Contains("자재 'M1': 컨베이어 'C1'가 소비하므로", Assert.Single(f).Message);
    }

    [Fact]
    public void Validate_Rejects_Several_Unload_Cps_On_An_Output_Material()
    {
        var p = Good();
        p.Materials[0] = p.Materials[0] with { Flow = "Output" };
        var e = Assert.Single(InputValidator.Validate(WithStations(p, "M1", "U", "V"), CpsUV));
        Assert.Equal((FindingCode.InputError, "Materials!3", "자재 'M1'은(는) 컨베이어/반출 자재라 하차 CP를 하나만 씁니다."), (e.Code, e.EntityId, e.Message));
    }

    [Fact]
    public void Validate_Checks_Every_Load_Cp_In_A_List()
    {
        var p = Good();
        p.Routes[0] = p.Routes[0] with { SupplyFromCPs = new[] { "L", "L009" } };
        var e = Assert.Single(InputValidator.Validate(p, Cps));
        Assert.Equal((FindingCode.InputError, "Materials!3", "SupplyFromCP 'L009'이(가) 레이아웃에 없습니다."), (e.Code, e.EntityId, e.Message));
        p.Routes[0] = p.Routes[0] with { SupplyFromCPs = new[] { "L", "S" } };
        Assert.Empty(InputValidator.Validate(p, Cps));
    }

    [Fact]
    public void Validate_Reports_A_Range_Written_With_Spaces_As_One_Unknown_Name()
    {
        // "L001 - L003" is one name for the parser (no range around spaces), so the drawing check names the whole token instead of reading a range
        var errors = new List<string>();
        var from = CpList.Parse("L001 - L003", errors, new List<string>());
        Assert.Equal(new[] { "L001 - L003" }, from);
        var p = Good();
        p.Routes[0] = p.Routes[0] with { SupplyFromCP = from[0] };
        var e = Assert.Single(InputValidator.Validate(p, Cps));
        Assert.Equal((FindingCode.InputError, "SupplyFromCP 'L001 - L003'이(가) 레이아웃에 없습니다."), (e.Code, e.Message));
    }
}
