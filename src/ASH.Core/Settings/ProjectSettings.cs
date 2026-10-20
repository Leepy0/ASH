using ASH.Core.Geometry;

namespace ASH.Core.Settings;

/// <summary>Per-drawing settings read from ash.settings.tsv. Groups = "Group:TypeName:MaxCount:Speed:Length:Width;..." (see SettingsFile.ParseGroups).</summary>
public sealed record ProjectSettings(double SafetyGap = 0.5, double CpSpacing = 10, double? ClaimDist = null, double? BlockReach = null,
    double VehicleLength = 2.0, double VehicleWidth = 1.0, string Units = "Meters", double Scale = 1.0, Point2 Origin = default,
    string ModelName = "model", string Groups = "G1:DefVehicle:5:1.0:2.0:1.0", string AmHome = @"C:\AutoMod
_6");
