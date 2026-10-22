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

    /// <summary>A name usable inside a file name: characters Windows forbids and whitespace become '_', at most 60 characters.</summary>
    public static string SafeFileName(string name)
    {
        var s = new string((name ?? "").Select(c => c < ' ' || char.IsWhiteSpace(c) || Bad.IndexOf(c) >= 0 ? '_' : c).ToArray());
        if (s.Length > 60) s = s.Substring(0, 60);
        return s.Length == 0 ? "_" : s;
    }
}
