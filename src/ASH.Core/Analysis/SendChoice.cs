using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Core.Runner;

namespace ASH.Core.Analysis;

/// <summary>Which analysis and plan a drawing is sent to; kept in &lt;drawing folder&gt;\ash.send.tsv (key TAB value) so the next send starts from it.
/// An empty id with a name means "create it"; all four empty means unclassified.</summary>
public sealed record SendChoice
{
    public const string FileName = "ash.send.tsv";
    public string AnalysisId { get; init; } = "";
    public string PlanId { get; init; } = "";
    public string AnalysisName { get; init; } = "";
    public string PlanName { get; init; } = "";

    public static SendChoice? Load(string dir)
    {
        var path = Path.Combine(dir, FileName);
        if (!File.Exists(path)) return null;
        var v = new Dictionary<string, string>();
        foreach (var line in File.ReadAllLines(path))
        {
            var a = line.Split(new[] { '\t' }, 2);
            if (a.Length == 2) v[a[0]] = a[1];
        }
        string Get(string k) => v.TryGetValue(k, out var s) ? s : "";
        return new SendChoice { AnalysisId = Get(nameof(AnalysisId)), PlanId = Get(nameof(PlanId)), AnalysisName = Get(nameof(AnalysisName)), PlanName = Get(nameof(PlanName)) };
    }

    public void Save(string dir)
    {
        static string One(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        File.WriteAllLines(Path.Combine(dir, FileName), new[]
        {
            "AnalysisId\t" + One(AnalysisId), "PlanId\t" + One(PlanId), "AnalysisName\t" + One(AnalysisName), "PlanName\t" + One(PlanName),
        });
    }

    /// <summary>The last choice if the server still has it; else the analysis/plan named like the folder/drawing; else new names from them.</summary>
    public static SendChoice Default(IReadOnlyList<AnalysisSummary> list, string folderName, string drawingName, SendChoice? last)
    {
        static bool Same(string a, string b) => a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        static SendChoice Of(AnalysisSummary a, PlanSummary? p, string newPlan)
            => new SendChoice { AnalysisId = a.Id, AnalysisName = a.Name, PlanId = p?.Id ?? "", PlanName = p?.Name ?? newPlan };

        if (last != null)
        {
            var a = list.FirstOrDefault(x => last.AnalysisId.Length > 0 ? x.Id == last.AnalysisId : Same(x.Name, last.AnalysisName));
            if (a != null)
            {
                var p = a.Plans.FirstOrDefault(x => last.PlanId.Length > 0 ? x.Id == last.PlanId : Same(x.Name, last.PlanName));
                if (p != null || last.PlanName.Length > 0) return Of(a, p, last.PlanName);
            }
        }
        var m = list.FirstOrDefault(x => Same(x.Name, folderName)) ?? list.FirstOrDefault(x => Same(x.Name, drawingName));
        if (m != null) return Of(m, m.Plans.FirstOrDefault(x => Same(x.Name, drawingName)) ?? m.Plans.FirstOrDefault(x => Same(x.Name, folderName)), drawingName);
        return new SendChoice { AnalysisName = folderName, PlanName = drawingName };
    }
}
