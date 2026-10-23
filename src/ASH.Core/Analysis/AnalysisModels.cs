using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace ASH.Core.Analysis;

/// <summary>A study ("분석") that groups plans (안); the server saves these in analyses.json.</summary>
[DataContract]
public sealed class AnalysisInfo
{
    [DataMember] public string Id { get; set; } = "";
    [DataMember] public string Name { get; set; } = "";
    [DataMember] public string Note { get; set; } = "";
    [DataMember] public DateTime CreatedUtc { get; set; }
    [DataMember] public string Corp { get; set; } = "";
    [DataMember] public string ProductLine { get; set; } = "";
    [DataMember] public string Line { get; set; } = "";
    [DataMember] public List<PlanInfo> Plans { get; set; } = new List<PlanInfo>();
}

[DataContract]
public sealed class PlanInfo
{
    [DataMember] public string Id { get; set; } = "";
    [DataMember] public string Name { get; set; } = "";
    [DataMember] public string Note { get; set; } = "";
}

public static class AnalysisNames
{
    const string Bad = "\\/:*?\"<>|";

    public const string NameRuleHelp = "이름 규칙(권장): <법인/동> <대상> <검토 목적> — 예 \"P동 1층 AGV 대수 검토\"";
    public const string PlanNotePlaceholder = "이 안이 다른 안과 무엇이 다른지";

    /// <summary>"A안"… 중 아직 없는 다음 글자; Z까지 다 쓰면 "새 안".</summary>
    public static string NextPlanName(IEnumerable<string> existing)
    {
        var used = new HashSet<char>();
        foreach (var n in existing)
            if (n != null && n.Length == 2 && n[0] >= 'A' && n[0] <= 'Z' && n[1] == '안') used.Add(n[0]);
        for (var c = 'A'; c <= 'Z'; c++) if (!used.Contains(c)) return c + "안";
        return "새 안";
    }

    /// <summary>A name usable inside a file name: characters Windows forbids and whitespace become '_', at most 60 characters.</summary>
    public static string SafeFileName(string name)
    {
        var s = new string((name ?? "").Select(c => c < ' ' || char.IsWhiteSpace(c) || Bad.IndexOf(c) >= 0 ? '_' : c).ToArray());
        if (s.Length > 60) s = s.Substring(0, 60);
        return s.Length == 0 ? "_" : s;
    }
}
