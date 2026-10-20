using System.Collections.Generic;
using ASH.Core.Geometry;

namespace ASH.Core.Export;

public sealed record VehicleGroupSpec(string Group, string TypeName, int MaxCount, double Speed, double Length, double Width, double RotateDegPerSec = 30);

/// <summary>VehicleGroupSpec.RotateDegPerSec: the vehicle template line `rotate 0 N Seconds` is N degrees per second (observed: 2 -> 45 s per 90 degree turn; not in the manual), default 30 = 3 s per 90 degrees. Applies to the `load Default` line; `load Empty` keeps `rotate 1 0` (first number 1 = inherit from Default, 0 = explicit value).
/// ClaimBlockOnNextCp adds OldBlockClaim to the AGVSDEF line (claim the next block on reaching the CP before it).
/// Groups is required by LayoutExporter.ToAgvs; each group needs at least one standby/buffer CP.</summary>
public sealed record ExportOptions(string Units = "Meters", Point2 Origin = default, double Scale = 1.0,
    IReadOnlyList<VehicleGroupSpec>? Groups = null, string Version = "12.6.1.19",
    string PathType = "DefaultGuidePath", string CpType = "DefaultControlPoint", bool ClaimBlockOnNextCp = true);

public sealed record ExportSummary(int PathsAdded, int PathsRemoved, int PathsMoved, int CpsAdded, int CpsRemoved, int CpsMoved);

public sealed record ExportResult(string AsyPath, string? BackupPath, string TrafficTsvPath, ExportSummary Summary, IReadOnlyList<string> Warnings);
