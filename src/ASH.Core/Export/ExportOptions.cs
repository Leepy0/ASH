using System.Collections.Generic;
using ASH.Core.Geometry;

namespace ASH.Core.Export;

public sealed record VehicleGroupSpec(string Group, string TypeName, int MaxCount, double Speed, double Length, double Width);

public sealed record ExportOptions(string Units = "Meters", Point2 Origin = default, double Scale = 1.0,
    IReadOnlyList<VehicleGroupSpec>? Groups = null, string Version = "12.6.1.19",
    string PathType = "DefaultGuidePath", string CpType = "DefaultControlPoint");

public sealed record ExportSummary(int PathsAdded, int PathsRemoved, int PathsMoved, int CpsAdded, int CpsRemoved, int CpsMoved);

public sealed record ExportResult(string AsyPath, string? BackupPath, string TrafficTsvPath, ExportSummary Summary);
