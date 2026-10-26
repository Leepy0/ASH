using System.Collections.Generic;
using System.Linq;
using ASH.Core.Traffic;
using Xunit;

namespace ASH.Tests.Traffic;

public class CpGroupViewTests
{
    static readonly string[] Cps = { "G1", "G2", "G1", "", "G3" };

    [Fact] public void Highlight_Greys_Unselected_Groups()
    {
        var plan = CpGroupView.Plan(Cps, new HashSet<string> { "G1" }, CpViewMode.Highlight);
        Assert.Equal(new[] { CpDisplay.Normal, CpDisplay.Gray, CpDisplay.Normal, CpDisplay.Gray, CpDisplay.Gray }, plan);
    }

    [Fact] public void Hide_Hides_Unselected_Groups()
    {
        var plan = CpGroupView.Plan(Cps, new HashSet<string> { "G2", "" }, CpViewMode.Hide);
        Assert.Equal(new[] { CpDisplay.Hidden, CpDisplay.Normal, CpDisplay.Hidden, CpDisplay.Normal, CpDisplay.Hidden }, plan);
    }

    [Fact] public void Null_Selection_Shows_Everything_In_Either_Mode()
    {
        Assert.All(CpGroupView.Plan(Cps, null, CpViewMode.Hide), d => Assert.Equal(CpDisplay.Normal, d));
        Assert.All(CpGroupView.Plan(Cps, null, CpViewMode.Highlight), d => Assert.Equal(CpDisplay.Normal, d));
    }

    [Fact] public void Empty_Selection_Dims_Everything_And_Group_Names_Are_Case_Sensitive()
    {
        Assert.All(CpGroupView.Plan(Cps, new HashSet<string>(), CpViewMode.Hide), d => Assert.Equal(CpDisplay.Hidden, d));
        Assert.Equal(CpDisplay.Gray, CpGroupView.Plan(new[] { "g1" }, new HashSet<string> { "G1" }, CpViewMode.Highlight)[0]);
    }

    [Fact] public void Summarize_Counts_Sorts_And_Counts_Hidden_Groups()
    {
        var shown = new[] { CpDisplay.Normal, CpDisplay.Hidden, CpDisplay.Normal, CpDisplay.Hidden, CpDisplay.Gray };
        var s = CpGroupView.Summarize(Cps.Zip(shown, (g, d) => (g, d)));
        Assert.Equal(new[] { "", "G1", "G2", "G3" }, s.Select(g => g.Group));
        Assert.Equal(new[] { 1, 2, 1, 1 }, s.Select(g => g.Count));
        Assert.Equal(new[] { false, true, false, false }, s.Select(g => g.Shown));
        Assert.Equal(new[] { 1, 0, 1, 0 }, s.Select(g => g.Hidden));
        Assert.Equal(2, CpGroupView.HiddenGroups(s));
    }

    [Fact] public void Mixed_Group_Counts_As_Shown()
    {
        var s = CpGroupView.Summarize(new[] { ("G1", CpDisplay.Normal), ("G1", CpDisplay.Gray) });
        Assert.True(s[0].Shown);
        Assert.Equal(1, s[0].Dimmed);
    }
}
