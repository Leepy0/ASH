using System.Collections.Generic;
using ASH.Core.Geometry;

namespace ASH.Core.Export;

/// <summary>SpeedCurve/SpeedEmptyCurve: arc speeds (m/s) for the vehicle's crvvel/rcrvvel lines; 0 = same as the straight speed.
/// StandbyCps: the group's standby CPs from the Excel (first = home); null, or none of them placed, falls back to the drawing's standby CPs of the group.
/// CycleStartCp: a Cycle group's first load CP (it has no standby CP): its vehicles start on the lane into it; null for a Call group.</summary>
public sealed record VehicleGroupSpec(string Group, string TypeName, int MaxCount, double Speed, double Length, double Width, double RotateDegPerSec = 90.0 / 7, double Accel = 0, double Decel = 0, string Kind = "", double SpeedEmpty = 0,
    double SpeedCurve = 0, double SpeedEmptyCurve = 0, IReadOnlyList<string>? StandbyCps = null, string? CycleStartCp = null);

/// <summary>Verbatim replacement text for a type's `AGVVEHSEG` block / `AGVSVEH` block (server assembly). The exporter still forces the segment name, type, numveh, vehsegs item and start lines.</summary>
public sealed record VehicleTemplateOverride(string? VehSegText = null, string? VehicleBlockText = null);

/// <summary>VehicleGroupSpec.RotateDegPerSec: the vehicle template line `rotate 0 N Seconds` is N degrees per second (observed: 2 -> 45 s per 90 degree turn; not in the manual), default 90/7 = 7 s per 90 degrees. Applies to the `load Default` line; `load Empty` keeps `rotate 1 0` (first number 1 = inherit from Default, 0 = explicit value).
/// ClaimBlockOnNextCp adds OldBlockClaim to the AGVSDEF line (claim the next block on reaching the CP before it).
/// Groups is required by LayoutExporter.ToAgvs; each group needs at least one standby/buffer CP (a Cycle group none: it starts on the lane into its CycleStartCp).
/// Scale converts drawing units to meters (mm drawing: 0.001) whatever Units is; Units is the model's unit, and every length the model gets is
/// meters x <see cref="UnitsPerMeter"/>: drawing coordinates (x Scale), group Length/Width (m), Speed/SpeedEmpty (m/s), Accel/Decel (m/s2).</summary>
public sealed record ExportOptions(string Units = "Meters", Point2 Origin = default, double Scale = 1.0,
    IReadOnlyList<VehicleGroupSpec>? Groups = null, string Version = "12.6.1.19",
    string PathType = "DefaultGuidePath", string CpType = "DefaultControlPoint", bool ClaimBlockOnNextCp = true,
    IReadOnlyDictionary<string, VehicleTemplateOverride>? VehicleOverrides = null)
{
    /// <summary>Model units in one meter; ArgumentException for a Units value ASH cannot convert (the model would come out at the wrong size).</summary>
    public double UnitsPerMeter => Settings.UnitScale.ModelUnitsPerMeter(Units)
        ?? throw new System.ArgumentException($"Units '{Units}'은(는) 지원하지 않는 단위입니다. Meters, Millimeters, Centimeters, Feet, Inches 중 하나로 설정하세요.");

    /// <summary>Drawing point -> model coordinates: (p - Origin) x Scale (meters) x UnitsPerMeter.</summary>
    public Point2 ToModel(Point2 p) => new Point2((p.X - Origin.X) * Scale * UnitsPerMeter, (p.Y - Origin.Y) * Scale * UnitsPerMeter);
}

public sealed record ExportSummary(int PathsAdded, int PathsRemoved, int PathsMoved, int CpsAdded, int CpsRemoved, int CpsMoved);

public sealed record ExportResult(string AsyPath, string? BackupPath, string TrafficTsvPath, ExportSummary Summary, IReadOnlyList<string> Warnings, string BlocksTsvPath = "");
