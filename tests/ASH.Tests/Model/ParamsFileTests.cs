using ASH.Core.Input;
using ASH.Core.Model;

namespace ASH.Tests.Model;

public class ParamsFileTests
{
    static ProjectInput Make(string mode, double spread, int mats = 2)
    {
        var p = new ProjectInput { Settings = new ASH.Core.Input.Settings { InitStockMode = mode, InitStockSpread = spread, SafetyStock = 2, SimLengthSec = 28800, WarmupSec = 3600 } };
        p.Groups.Add(new AgvGroup("work", "T", 1, 5, new List<string>(), "Nearest"));
        p.Materials.Add(new MaterialInfo("A", "S1", "work", 60, 1, 4, 3, 5, 10, 1.5, false));
        for (int i = 2; i <= mats; i++)
            p.Materials.Add(new MaterialInfo("M" + i, "S" + i, "work", 90.5, 2, 4, 3, 5, null, null, false));
        return p;
    }

    [Fact]
    public void Render_Matches_Spec_Format()
    {
        var s = ParamsFile.Render(Make("Fixed", 0), new RunScenario(7, new Dictionary<string, int> { ["work"] = 3 }));
        Assert.Equal("SEED 7\nGROUPS 1\n1 3\nMATERIALS 2\n1 3 5 10 1.5 60 1 4\n2 3 5 8 2 90.5 2 4\nSIM 28800 3600\nEND\n", s);
    }

    [Fact]
    public void Uniform_Init_Stock_Is_Seeded_Deterministic()
    {
        var sc = new RunScenario(5, new Dictionary<string, int> { ["work"] = 1 });
        var a = ParamsFile.Render(Make("Uniform", 10, 6), sc);
        Assert.Equal(a, ParamsFile.Render(Make("Uniform", 10, 6), sc));
        Assert.NotEqual(a, ParamsFile.Render(Make("Uniform", 10, 6), sc with { Seed = 6 }));
        foreach (var line in a.Split('\n').Skip(4).Take(6)) // material 2..: base 8, within +-10%
        {
            var init = double.Parse(line.Split(' ')[3], System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(init, line.StartsWith("1 ") ? 9 : 7.2, line.StartsWith("1 ") ? 11 : 8.8);
        }
    }

    [Fact]
    public void Staggered_Offsets_Cycle()
    {
        var lines = ParamsFile.Render(Make("Staggered", 2, 4), new RunScenario(1, new Dictionary<string, int>())).Split('\n');
        var inits = lines.Skip(4).Take(4).Select(l => l.Split(' ')[3]).ToArray();
        Assert.Equal(new[] { "10", "10", "12", "8" }, inits); // bases 10,8,8,8 + offsets 0,2,4,0
    }

    [Fact]
    public void Render_Rejects_Unknown_Group_Key()
    {
        Assert.Throws<ArgumentException>(() => ParamsFile.Render(Make("Fixed", 0), new RunScenario(1, new Dictionary<string, int> { ["nope"] = 1 })));
        Assert.Throws<ArgumentException>(() => ParamsFile.Render(Make("Fixed", 0), new RunScenario(1, new Dictionary<string, int> { ["work"] = -1 })));
    }
}
