using System.Collections.Generic;

namespace ASH.Core.Asy;

public sealed class AgvsSystem
{
    public string Version { get; set; } = "";
    public string[] UnitsTokens { get; set; } = new string[0];
    /// <summary>SYSDEF, FLAGS, AGVSDEF, AGVSTOL blocks verbatim.</summary>
    public List<string> HeaderLines { get; } = new List<string>();
    public List<string> PathTypeLines { get; } = new List<string>();
    public List<GuidePath> Paths { get; } = new List<GuidePath>();
    public List<string> CpTypeLines { get; } = new List<string>();
    public List<ControlPoint> ControlPoints { get; } = new List<ControlPoint>();
    public Dictionary<string, List<string>> NameLists { get; } = new Dictionary<string, List<string>>();
    public List<string> ParkListLines { get; } = new List<string>();
    public List<string> WorkListLines { get; } = new List<string>();
    /// <summary>SRCHLST (load search lists: a waiting load claims an idle vehicle).</summary>
    public List<string> SearchListLines { get; } = new List<string>();
    /// <summary>AGVVEHSEG up to the next top-level keyword, verbatim.</summary>
    public List<string> VehicleSegmentLines { get; } = new List<string>();
    public List<string> VehicleLines { get; } = new List<string>();
}
