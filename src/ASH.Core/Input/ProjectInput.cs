using System.Collections.Generic;

namespace ASH.Core.Input;

public sealed record Settings
{
    public string Units { get; init; } = "Meters";
    public double SimLengthSec { get; init; } = 28800;
    public double WarmupSec { get; init; } = 3600;
    public int Replications { get; init; } = 3;
    public int SeedBase { get; init; } = 1;
    public double UtilMax { get; init; } = 0.75;
    public double SafetyStock { get; init; } = 2;
    public string InitStockMode { get; init; } = "Uniform";
    public double InitStockSpread { get; init; } = 10;
    public string TrafficMode { get; init; } = "Block";
    public string WorkDefinition { get; init; } = "LoadToUnload";
    /// <summary>A single travel leg longer than this counts as a deadlock.</summary>
    public double DeadlockSec { get; init; } = 600;
}

public sealed record AgvType(string Type, double SpeedLoaded, double SpeedEmpty, double Accel, double Decel,
    int Capacity, double LoadTime, double UnloadTime, double MTBF, double MTTR, double Length, double Width, string Color, int Row = 0, string Kind = "",
    double? SpeedLoadedCurve = null, double? SpeedEmptyCurve = null, string Category = "", string Asset = "");

/// <summary>The group is the AutoMod vehicle type (its name is the AGVS vehicle type), so one AgvType can serve several groups.</summary>
/// <param name="FinishCP">Optional: a vehicle counts as idle from its arrival here on the way home (else from the free-run return time).</param>
public sealed record AgvGroup(string Group, string Type, int MinCount, int MaxCount, List<string> StandbyCPs, string Dispatch, int Row = 0, string? FinishCP = null);

public sealed record MaterialInfo(string Material, string Station, string Group, double TaktTime, double UnitsPerCycle,
    double UnitsPerContainer, double CallThreshold, double UnloadThreshold, double? InitStock, double? SafetyStock, bool EmptyReturn, int Row = 0, string Form = "",
    double? LoadLength = null, double? LoadWidth = null, double? LoadHeight = null, string Flow = "", bool ConveyorFed = false)
{
    // ConveyorFed: a Conveyors row consumes this material (products passing the use point) instead of the takt clock; TaktTime and
    // UnitsPerCycle then hold that row's ProductTakt and UnitsPerProduct, so every takt-based KPI and check keeps working unchanged.
    /// <summary>Flow is Output (an unknown value is reported by the validator and treated as Supply).</summary>
    public bool IsOutput => MaterialFlows.Normalize(Flow) == MaterialFlows.Output;
}

public sealed record Route(string Material, string SupplyFromCP, string SupplyToCP, string? BufferCP,
    string? ReturnFromCP, string? ReturnToCP, double ExtraTime, int Row = 0, double BufferWaitSec = 0);

public sealed record StationInfo(string Station, string CPName, double BufferCapacity, int Row = 0);

/// <summary>One Conveyors sheet row: the CAD conveyor (AGV_CONV polyline named by AGVCONV) that consumes Material.
/// SpeedMps metres/second; Capacity null = from length; ConsumeAtM metres from the polyline start, null = the end.</summary>
public sealed record ConveyorInfo(string Conveyor, string Material, double ProductTakt, double UnitsPerProduct, double SpeedMps,
    int? Capacity, double? ConsumeAtM, int Row = 0);

public sealed record ExtHook(string Name, bool Enabled, string Description, int Row = 0);

public sealed class ProjectInput
{
    public Settings Settings { get; set; } = new Settings();
    public List<AgvType> AgvTypes { get; } = new List<AgvType>();
    public List<AgvGroup> Groups { get; } = new List<AgvGroup>();
    public List<MaterialInfo> Materials { get; } = new List<MaterialInfo>();
    public List<Route> Routes { get; } = new List<Route>();
    public List<StationInfo> Stations { get; } = new List<StationInfo>();
    public List<ExtHook> Ext { get; } = new List<ExtHook>();
    public List<ConveyorInfo> Conveyors { get; } = new List<ConveyorInfo>();
    /// <summary>Materials whose Use column is N: left out of Materials and Routes by the reader, listed here for messages.</summary>
    public List<string> DisabledMaterials { get; } = new List<string>();
}
