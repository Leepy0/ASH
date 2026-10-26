using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Traffic;

public enum CpViewMode { Highlight, Hide }

/// <summary>How one CP is shown: ByLayer and visible / grey (ACI 8) / not visible.</summary>
public enum CpDisplay { Normal, Gray, Hidden }

/// <summary>One GROUP value in the drawing: its CPs, and how many of them are grey or hidden right now.</summary>
public sealed record CpGroupInfo(string Group, int Count, int Dimmed, int Hidden)
{
    /// <summary>The palette ticks a group while at least one of its CPs is shown normally.</summary>
    public bool Shown => Dimmed < Count;
}

/// <summary>F1 group view: which CPs are shown how for the groups the user ticked. Pure, so it can be tested without ZWCAD.</summary>
public static class CpGroupView
{
    /// <summary>Per CP (same order as <paramref name="cpGroups"/>): a CP of a selected group is normal, any other is grey (highlight) or hidden (hide). Null selection = every group.</summary>
    public static CpDisplay[] Plan(IReadOnlyList<string> cpGroups, ISet<string>? selected, CpViewMode mode) =>
        cpGroups.Select(g => selected == null || selected.Contains(g) ? CpDisplay.Normal : mode == CpViewMode.Hide ? CpDisplay.Hidden : CpDisplay.Gray).ToArray();

    /// <summary>Groups with their CP counts, sorted by name (a CP without a group is the group "").</summary>
    public static IReadOnlyList<CpGroupInfo> Summarize(IEnumerable<(string Group, CpDisplay Shown)> cps) =>
        cps.GroupBy(c => c.Group)
            .Select(g => new CpGroupInfo(g.Key, g.Count(), g.Count(c => c.Shown != CpDisplay.Normal), g.Count(c => c.Shown == CpDisplay.Hidden)))
            .OrderBy(g => g.Group, System.StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Group, System.StringComparer.Ordinal).ToList();

    /// <summary>Groups with at least one hidden CP (what the status line counts).</summary>
    public static int HiddenGroups(IEnumerable<CpGroupInfo> groups) => groups.Count(g => g.Hidden > 0);
}
