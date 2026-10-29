using System.Collections.Generic;
using System.Linq;
using ASH.Core.Input;
using Xunit;

namespace ASH.Tests.Input;

/// <summary>A material with several unload CPs becomes one station material per CP (M001@U002), in its place.</summary>
public class StationExpansionTests
{
    static ProjectInput Input()
    {
        var p = new ProjectInput();
        p.Materials.Add(new MaterialInfo("M000", "", "G1", 60, 1, 10, 4, 1, null, null, false, Row: 2));
        p.Materials.Add(new MaterialInfo("M001", "", "G1", 30, 1, 10, 4, 1, null, null, true, Row: 3));
        p.Materials.Add(new MaterialInfo("M002", "", "G1", 60, 1, 10, 4, 1, null, null, false, Row: 4));
        p.Routes.Add(new Route("M000", "L000", "U000", null, null, null, 0, 2));
        p.Routes.Add(new Route("M001", "L001", "U001", "B1", "RL1", null, 0, 3, SupplyFromCPs: new[] { "L001", "L002" }));
        p.Routes.Add(new Route("M002", "L002", "U009", null, null, null, 0, 4));
        return p;
    }

    [Fact]
    public void Expand_makes_one_station_material_per_unload_cp()
    {
        var p = Input();
        StationExpansion.Expand(p, new Dictionary<string, List<string>> { ["M001"] = new List<string> { "U001", "U002" } });

        Assert.Equal(new[] { "M000", "M001@U001", "M001@U002", "M002" }, p.Materials.Select(m => m.Material));
        var st = p.Materials.Skip(1).Take(2).ToList();
        Assert.All(st, m => Assert.Equal(("M001", "M001", 30.0, 3, true), (m.Source, m.SourceName, m.TaktTime, m.Row, m.EmptyReturn)));

        Assert.Equal(new[] { ("M000", "U000"), ("M001@U001", "U001"), ("M001@U002", "U002"), ("M002", "U009") }, p.Routes.Select(r => (r.Material, r.SupplyToCP)));
        Assert.All(p.Routes.Skip(1).Take(2), r => Assert.Equal((3, "B1", "RL1", "L001|L002"), (r.Row, r.BufferCP, r.ReturnFromCP, string.Join("|", r.FromCps))));
    }

    [Fact]
    public void Single_unload_cp_is_not_expanded()
    {
        var p = Input();
        StationExpansion.Expand(p, new Dictionary<string, List<string>>());
        Assert.Equal(new[] { "M000", "M001", "M002" }, p.Materials.Select(m => m.Material));
        Assert.Equal(new[] { "M000", "M001", "M002" }, p.Materials.Select(m => m.SourceName));
        Assert.All(p.Materials, m => Assert.Equal("", m.Source));
        Assert.Equal(new[] { "M000", "M001", "M002" }, p.Routes.Select(r => r.Material));
    }
}
