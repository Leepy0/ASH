using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Settings;

namespace ASH.Core.Lite;

/// <summary>Cleanup limits in mm, whatever the drawing's unit (INSUNITS). DenseMmPerM2 = 0 turns the blob outlines (G) off; MaxBlobMm is the largest blob replaced (and the shortest entity
/// left out of the occupancy, R6); CellMm is the occupancy cell for connectivity and the outline, density is always measured per 1 m cell (R5).</summary>
public sealed record LiteThresholds(double ToleranceMm, double MinSizeMm, int InsUnits, double DenseMmPerM2 = 0, double MaxBlobMm = 0, double CellMm = 200)
{
    public const double GridMm = 0.1, DegenerateMm = 0.01;

    /// <summary>Drawing units per mm.</summary>
    public double UnitsPerMm => 0.001 / UnitScale.FromInsUnits(InsUnits);
}

/// <summary>What one step removed: entities, and bytes as LiteDxfWriter.Bytes writes them unshifted.</summary>
public sealed class LiteStep
{
    public int Count;
    public long Bytes;
}

/// <summary>One blob that became an outline: where (lower left, size) in the coordinates of Node (null outside LiteBuild), how many lines it replaced and its density (mm of line per m²).</summary>
public sealed record LiteBlobInfo(LiteNode? Node, double X, double Y, double W, double H, int Lines, double Density);

/// <summary>Removed per step, added up over LitePipeline.Run and Blobs.Replace calls. Counts and bytes are per definition, not multiplied by instances.</summary>
public sealed class LiteStats
{
    public const int TopCount = 30;
    /// <summary>퇴화, 중복, 겹침, 작은 요소, 덩어리 외곽선 (lines a blob replaced, placed child lines included).</summary>
    public readonly LiteStep Degenerate = new(), Duplicate = new(), Overlap = new(), Small = new(), Blob = new();
    /// <summary>Blobs replaced by an outline; child instances dropped whole inside one (R18); instances not placed because their box was under MinSize (R2a).</summary>
    public int Blobs, DroppedInstances, SmallInstances;
    /// <summary>The TopCount blobs with most lines, most first (X then Y break ties).</summary>
    public readonly List<LiteBlobInfo> TopBlobs = new();

    public void AddTop(LiteBlobInfo b)
    {
        int i = TopBlobs.FindIndex(x => x.Lines < b.Lines || x.Lines == b.Lines && (x.X > b.X || x.X == b.X && x.Y > b.Y));
        TopBlobs.Insert(i < 0 ? TopBlobs.Count : i, b);
        if (TopBlobs.Count > TopCount) TopBlobs.RemoveAt(TopCount);
    }
}

/// <summary>The cleanup of AGVLITE over one definition's geometry.</summary>
public static class LitePipeline
{
    /// <summary>Degenerate, Dedup, MergeCollinear, Join, Simplify, SmallFilter, in this order: the small filter has to see joined closed shapes, and only joined pieces have vertices to thin.
    /// flat is already 2D, in local units. scaleMax is the largest instance scale of the definition: every limit is divided by it, so nothing removed here is large in any instance.</summary>
    public static List<LiteEntity> Run(IEnumerable<LiteEntity> flat, LiteThresholds t, double scaleMax, LiteStats stats)
    {
        if (!(scaleMax > 0)) throw new ArgumentOutOfRangeException(nameof(scaleMax)); // a zero would turn every limit infinite and remove everything
        double unit = t.UnitsPerMm / scaleMax, grid = LiteThresholds.GridMm * unit;

        var all = new List<LiteEntity>(flat);
        all = Tally(stats.Degenerate, all, Clean.Degenerate(all, LiteThresholds.DegenerateMm * unit, out int n), n);
        all = Tally(stats.Duplicate, all, Clean.Dedup(all, grid, out n), n);
        all = Tally(stats.Overlap, all, Clean.MergeCollinear(all, grid, out n), n);
        all = Chain.Join(all, grid).ConvertAll(e => e is LitePolyline p ? Chain.Simplify(p, t.ToleranceMm * unit) : e);
        return Tally(stats.Small, all, SmallFilter.Apply(all, t.MinSizeMm * unit, Math.Max(t.ToleranceMm * unit, grid), out n), n); // gaps a chain cannot cross still make one outline
    }

    static List<LiteEntity> Tally(LiteStep step, List<LiteEntity> before, List<LiteEntity> after, int removed)
    {
        if (removed > 0)
        {
            step.Count += removed;
            step.Bytes += Bytes(before) - Bytes(after);
        }
        return after;
    }

    static long Bytes(List<LiteEntity> l) => l.Sum(e => LiteDxfWriter.Bytes(e, e.Layer, 0, 0));
}
