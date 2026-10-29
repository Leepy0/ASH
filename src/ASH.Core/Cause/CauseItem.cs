using System.Collections.Generic;
using ASH.Core.Input;
using ASH.Core.Runner;

namespace ASH.Core.Cause;

/// <summary>Kinds of cause findings, in the order the 원인 tab groups them.</summary>
public static class CauseKinds
{
    public const string Deadlock = "deadlock", DeadlockRisk = "deadlock-risk", BlockHold = "block-hold", Detour = "detour",
        LoadShare = "load-share", TimeLoss = "time-loss", Clearance = "clearance", Stockout = "stockout";
    public static readonly string[] Order = { Deadlock, Stockout, DeadlockRisk, Clearance, BlockHold, Detour, LoadShare, TimeLoss };
}

/// <summary>One finding of the cause analysis. UI text rules: words and short noun phrases, no sentences; "자재 품절", never "결품".
/// Title e.g. "교착 B005·B007"; Where e.g. "B005" or "L003→U001"; Detail e.g. "V3 대기 B007 · 점유 B005"; Fix e.g. "U003 출구 쪽 1.5 m 이동" ("" = none).
/// Severity 2 = 심각, 1 = 주의, 0 = 참고. AtSec = simulation time of the event (null for layout findings).
/// X, Y = model metres (like ArcLayout) of the place to show on the map and to aim a film cut at; null when there is no single place.
/// Refs = names of the CPs, paths and blocks the finding concerns (the 원인 tab highlights them on a map); null or names the layout lacks are ignored.</summary>
public sealed record CauseItem(string Kind, int Severity, string Title, string Where, string Detail, string Fix,
    double? AtSec = null, double? X = null, double? Y = null, IReadOnlyList<string>? Refs = null);

/// <summary>Everything an analyser may read. Layout is metres (ArcLayout.Load on the server; ExportOptions-converted in the plugin).
/// Result / Report are null for layout-only analysis (plugin AGVCHECK, before any run). VehicleLength / VehicleWidth / ClaimDist / BlockReach are metres
/// (ash.settings.tsv values when known, else the first AgvType size and the ProjectSettings defaults).</summary>
public sealed record CauseContext(Layout.Layout Layout, ProjectInput Input, ResultSet? Result = null, ReportStats? Report = null,
    double VehicleLength = 1.5, double VehicleWidth = 1.0, double ClaimDist = 0, double BlockReach = 0, double SafetyGap = 0.5);

/// <summary>One deadlock as the model logged it (dlock rows of result.tsv): when it was first seen and who waited for whom.</summary>
public sealed record DeadlockEvent(double AtSec, IReadOnlyList<DeadlockLink> Links);

/// <summary>A vehicle in the wait chain: the block it waits for, the blocks it holds, where it stands (model metres, NaN when unknown).</summary>
public sealed record DeadlockLink(string Vehicle, string Group, string WaitsFor, IReadOnlyList<string> Holds, double X, double Y);
