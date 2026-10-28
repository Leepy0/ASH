using System;
using System.Collections.Generic;
using System.Linq;

namespace ASH.Core.Layout;

/// <summary>One thing left to do. <see cref="Command"/> is what a double click sends ("" = nothing to send).</summary>
public sealed record NextStep(string Text, string Command = "")
{
    /// <summary>Text with the command behind it: "input.xlsx 없음(AGVEXCEL)".</summary>
    public string Label => Command.Length > 0 ? $"{Text}({Command})" : Text;
}

/// <summary>What the plugin knows besides the drawing. Saved = the drawing has a folder; the three files live in that folder.
/// CheckErrors = findings that block a model (the plugin counts them). ExtraGroups = group names the drawing does not carry (the groups of ash.settings.tsv while there is no input.xlsx), null when unknown.</summary>
public sealed record NextStepFacts(bool Saved, bool HasSettings, bool HasInput, bool ModelBuilt, int CheckErrors = 0, IReadOnlyCollection<string>? ExtraGroups = null);

/// <summary>"다음 할 일": short Korean items, in the order the work goes. Empty when nothing is left. Pure, so the palette panel needs no ZWCAD to be tested.</summary>
public static class NextSteps
{
    public static List<NextStep> Of(Layout l, NextStepFacts f)
    {
        var res = new List<NextStep>();
        if (l.Paths.Count == 0) res.Add(new NextStep("경로 없음", "AGVPATH"));
        if (l.Cps.Count == 0) res.Add(new NextStep("CP 없음", "AGVCP"));
        else
        {
            // a group exists where a CP carries it or the settings name it; CPs without a group (traffic) belong to none
            var groups = l.Cps.Select(c => c.Group).Concat(f.ExtraGroups ?? Array.Empty<string>()).Where(g => g.Length > 0)
                .Distinct(StringComparer.Ordinal).OrderBy(g => g, StringComparer.OrdinalIgnoreCase).ThenBy(g => g, StringComparer.Ordinal);
            foreach (var g in groups)
            {
                bool In(Func<LayoutCp, bool> match) => l.Cps.Any(c => c.Group == g && match(c));
                if (!In(c => c.Type == "standby" || c.Type == "buffer")) res.Add(new NextStep($"그룹 {g}: 대기 CP 없음"));
                if (!In(c => c.Type == "load")) res.Add(new NextStep($"그룹 {g}: load 없음"));
            }
        }
        // a block is only needed where paths meet or split
        if (l.Paths.Count > 0 && l.Blocks.Count == 0 && PathGraph.Build(l).Junctions.Any()) res.Add(new NextStep("block 없음", "AGVTRAFFIC"));
        if (!f.Saved) res.Add(new NextStep("도면 저장", "QSAVE"));
        else
        {
            if (!f.HasSettings) res.Add(new NextStep("ash.settings.tsv 없음"));
            if (!f.HasInput) res.Add(new NextStep("input.xlsx 없음", "AGVEXCEL"));
        }
        if (f.CheckErrors > 0) res.Add(new NextStep($"검증 오류 {f.CheckErrors}건", "AGVCHECK"));
        if (f.Saved && f.HasInput && !f.ModelBuilt) res.Add(new NextStep("모델 없음", "AGVMODEL"));
        return res;
    }
}
