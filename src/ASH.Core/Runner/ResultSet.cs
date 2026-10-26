using System.Collections.Generic;

namespace ASH.Core.Runner;

/// <summary>Supplies = deliveries after warmup; Consumed = containers consumed after warmup (value6); Expected = theoretical consumption.
/// The raw row also carries value7 = distance and value8 = count of the material's supply jobs (0 in models built before them).</summary>
public sealed record MaterialResult(string Name, string Group, double MinStock, double AvgStock, int Supplies, double Expected, double StopTimeSec,
    double Consumed);
public sealed record GroupResult(string Group, int ActiveCount);
public sealed record BlockResult(string Name, double Passes, double AvgWaitSec, double MaxWaitSec, double Utilization);

/// <summary>vehicle row: busy seconds (pickup run + job until the Finish CP / standby arrival, repairs during a job included, the wait at a buffer
/// for the unload threshold left out), distance, jobs,
/// the measured window, index within the group, repair seconds (MTBF/MTTR breakdowns). All after warmup, as the logic keeps them (AutoMod's own
/// vehicle states mean nothing once the load rides along the whole job).</summary>
public sealed record VehicleResult(string Name, string Group, double BusySec, double Distance, int Jobs, double WindowSec, int Index, double DownSec = 0,
    TimeSplit? Split = null);

/// <summary>Where a vehicle's measured window went, in seconds after warmup (value7..11 of the vehicle row; null for models built before them).
/// Busy = Loaded + Empty + Handling + BufferBusy + Repair, the busy time the utilization is made of; the rest of the window is BufferIdle + Idle.
/// Handling = load, unload and Route.ExtraTime waits; BufferBusy / BufferIdle = the hold at a buffer for the unload threshold within / beyond
/// Route.BufferWaitSec; Repair = breakdowns that held a job (one while standing idle is Idle). Empty = the rest of the busy time: empty runs, home.</summary>
public sealed record TimeSplit(double Window, double Busy, double Loaded, double Handling, double BufferBusy, double BufferIdle, double Repair)
{
    public double Empty => Busy - Loaded - Handling - BufferBusy - Repair;
    public double Idle => Window - Busy - BufferIdle;
}

/// <summary>matstat row (AutoMod tables, after warmup): lead time call -> pickup and call -> delivered (average, maximum), stock samples one per takt.</summary>
public sealed record MaterialStat(string Name, string Group, double PickAvg, double PickMax, double DelivAvg, double DelivMax,
    double StockAvg, double StockStd, double StockMin, double StockMax);

/// <summary>Raw data row of result.tsv: Values = columns 4.. (value1..), unparsable cells are NaN.</summary>
public sealed record ResultRow(string Kind, string Name, string Group, double[] Values);

/// <summary>Typed rows of result.tsv. Deadlocks = value1 of the deadlock row, SimSec = value2 of the clock row.
/// Rows keeps every data row in file order, including kinds not typed here.</summary>
public sealed record ResultSet(IReadOnlyList<MaterialResult> Materials, IReadOnlyList<GroupResult> Groups,
    IReadOnlyList<BlockResult> Blocks, int Deadlocks, double SimSec,
    IReadOnlyList<ResultRow> Rows,
    IReadOnlyList<VehicleResult>? Vehicles = null, IReadOnlyList<MaterialStat>? MaterialStats = null);

/// <summary>One data row of "Vehicle Statistics" in the .report: percent of total time and trips per state.</summary>
public sealed record VehicleRow(double DeliveringPct, int DeliveringTrips, double RetrievingPct, int RetrievingTrips,
    double GoingToParkPct, double ParkingPct);

/// <summary>Per vehicle type: one row per vehicle plus the "All &lt;type&gt;:" average row.</summary>
public sealed record TypeStats(IReadOnlyList<VehicleRow> Vehicles, VehicleRow? All);

/// <summary>Row of "Block Statistics": Util is the fraction of time the block was occupied.</summary>
public sealed record BlockStat(string Name, int Total, double Util, double AvTime, double AvWait);

/// <summary>PathTotals = "Guide Path Statistics": path name -> vehicles that entered it during the run (empty when the report has no such table).</summary>
public sealed record ReportStats(IReadOnlyDictionary<string, TypeStats> Types, IReadOnlyList<BlockStat> Blocks, IReadOnlyList<string> RawLines,
    IReadOnlyDictionary<string, int>? PathTotals = null);
