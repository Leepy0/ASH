using System.Collections.Generic;

namespace ASH.Core.Runner;

/// <summary>Supplies = deliveries after warmup; Consumed = containers consumed after warmup (value6); Expected = theoretical consumption.</summary>
public sealed record MaterialResult(string Name, string Group, double MinStock, double AvgStock, int Supplies, double Expected, double StopTimeSec,
    double Consumed);
public sealed record GroupResult(string Group, int ActiveCount);
public sealed record BlockResult(string Name, double Passes, double AvgWaitSec, double MaxWaitSec, double Utilization);

/// <summary>vehicle row: busy seconds (pickup run + job until the Finish CP / standby arrival, repairs during a job included), distance, jobs,
/// the measured window, index within the group, repair seconds (MTBF/MTTR breakdowns). All after warmup, as the logic keeps them (AutoMod's own
/// vehicle states mean nothing once the load rides along the whole job).</summary>
public sealed record VehicleResult(string Name, string Group, double BusySec, double Distance, int Jobs, double WindowSec, int Index, double DownSec = 0);

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

public sealed record ReportStats(IReadOnlyDictionary<string, TypeStats> Types, IReadOnlyList<BlockStat> Blocks, IReadOnlyList<string> RawLines);
