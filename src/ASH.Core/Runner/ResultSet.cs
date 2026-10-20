using System.Collections.Generic;

namespace ASH.Core.Runner;

public sealed record MaterialResult(string Name, string Group, double MinStock, double AvgStock, int Supplies, double Expected, double StopTimeSec);
public sealed record GroupResult(string Group, int ActiveCount);
public sealed record BlockResult(string Name, double Passes, double AvgWaitSec, double MaxWaitSec, double Utilization);

/// <summary>Typed rows of result.tsv. Deadlocks = value1 of the deadlock row, SimSec = value2 of the clock row.</summary>
public sealed record ResultSet(IReadOnlyList<MaterialResult> Materials, IReadOnlyList<GroupResult> Groups,
    IReadOnlyList<BlockResult> Blocks, int Deadlocks, double SimSec);

/// <summary>One data row of "Vehicle Statistics" in the .report: percent of total time and trips per state.</summary>
public sealed record VehicleRow(double DeliveringPct, int DeliveringTrips, double RetrievingPct, int RetrievingTrips,
    double GoingToParkPct, double ParkingPct);

/// <summary>Per vehicle type: one row per vehicle plus the "All &lt;type&gt;:" average row.</summary>
public sealed record TypeStats(IReadOnlyList<VehicleRow> Vehicles, VehicleRow? All);

/// <summary>Row of "Block Statistics": Util is the fraction of time the block was occupied.</summary>
public sealed record BlockStat(string Name, int Total, double Util, double AvTime, double AvWait);

public sealed record VehicleStats(IReadOnlyDictionary<string, TypeStats> Types, IReadOnlyList<BlockStat> Blocks, IReadOnlyList<string> RawLines);
