using System;
using System.Collections.Generic;
using System.Linq;
using ASH.Core.Cel;

namespace ASH.Core.Lite;

/// <summary>What only the reader of the drawing saw (R22): for the report and AGVLITESTAT. Before: model space's own entities by type. Removed: always-dropped kinds and unresolved xrefs,
/// counted once per definition, not per instance. Names and Solids (3D objects) have one entry per LiteBlocks.Defs entry. ExplodeFailed: objects or pieces that became their extents box.
/// Points3D: points taken from the drawing-wide 3D budget; Over3D: objects boxed unread because it ran out. MinsertOver: MINSERT cells past the cap, not placed.</summary>
public sealed class LiteReadLog
{
    public SortedDictionary<string, int> Before { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> Removed { get; } = new(StringComparer.Ordinal);
    public List<string> Names { get; } = new();
    public List<int> Solids { get; } = new();
    public int Failures, ExplodeFailed, Over3D;
    public long Points3D, MinsertOver;

    public static void Count(IDictionary<string, int> d, string key) => d[key] = (d.TryGetValue(key, out var v) ? v : 0) + 1;
}

/// <summary>The reader's arithmetic, apart from ZWCAD so it can be tested.</summary>
public static class LiteRead
{
    /// <summary>Points one 3D object may give. Over this the object becomes its box: it is a mesh of a machine nobody can see at plan scale, and one object must not fill the file.</summary>
    public const int MaxPointsPerObject = CurveApprox.MaxPoints * 64;
    /// <summary>Points all 3D objects of a drawing may give: thousands of loose solids under MaxPointsPerObject would still fill the memory.</summary>
    public const long MaxPoints3D = 20_000_000;
    /// <summary>Cells one MINSERT may place.</summary>
    public const int MaxMinsertCells = 65536;

    /// <summary>Curve sampling tolerance in drawing units: level 0's, once, while reading (R23).
    /// shortcut: the largest instance scale is unknown here, so a curve in a heavily scaled block comes out slightly coarser; upgrade by reading after the scales are known.</summary>
    public static double Tolerance(int insUnits)
    {
        var t = LiteLevels.Get(0, insUnits);
        return t.ToleranceMm * t.UnitsPerMm;
    }

    /// <summary>One cell of a MINSERT: the offset runs along the insert's rotated axes in its own plane, unscaled, so it comes after BlockTransform. dx, dy = column and row distance.</summary>
    public static Affine3 Minsert(Affine3 blockTransform, Vec3 normal, double rotation, double dx, double dy)
    {
        var (x, y) = Flatten.OcsAxes(normal);
        double co = Math.Cos(rotation), si = Math.Sin(rotation), u = dx * co - dy * si, v = dx * si + dy * co;
        return blockTransform with { M03 = blockTransform.M03 + u * x.X + v * y.X, M13 = blockTransform.M13 + u * x.Y + v * y.Y, M23 = blockTransform.M23 + u * x.Z + v * y.Z };
    }

    /// <summary>The cells of a MINSERT, row by row: only the first MaxMinsertCells, the rest go to log.MinsertOver.</summary>
    public static List<Affine3> Minsert(Affine3 blockTransform, Vec3 normal, double rotation, int rows, int columns, double rowSpacing, double columnSpacing, LiteReadLog log)
    {
        long total = (long)Math.Max(rows, 0) * Math.Max(columns, 0);
        log.MinsertOver += Math.Max(0, total - MaxMinsertCells);
        var cells = new List<Affine3>((int)Math.Min(total, MaxMinsertCells));
        for (int r = 0; r < rows && cells.Count < MaxMinsertCells; r++)
            for (int c = 0; c < columns && cells.Count < MaxMinsertCells; c++)
                cells.Add(Minsert(blockTransform, normal, rotation, c * columnSpacing, r * rowSpacing));
        return cells;
    }

    static long Points(IEnumerable<Geo3> geo) => geo.Sum(g => (long)(g is Points3 p ? p.Points.Count : g is Poly3 q ? q.Vertices.Count : 2));

    /// <summary>The points these geometries hold, against MaxPointsPerObject.</summary>
    public static bool TooBig(IEnumerable<Geo3> geo) => Points(geo) > MaxPointsPerObject;

    /// <summary>What one 3D object adds to its definition: read's geometry, or box() with a count (Over3D, past MaxPoints3D, unread; ExplodeFailed, read threw or gave over MaxPointsPerObject).
    /// Only top objects (not exploded pieces) meet the budget: a piece is charged with its object, half an object would look like a real shape. The object crossing the budget stays whole.
    /// Whatever box throws goes to the caller.</summary>
    public static IReadOnlyList<Geo3> Whole(LiteReadLog log, bool top, Func<List<Geo3>> read, Func<Geo3> box)
    {
        bool over = top && log.Points3D >= MaxPoints3D;
        if (!over)
            try
            {
                var geo = read();
                long points = Points(geo);
                if (points <= MaxPointsPerObject)
                {
                    if (top) log.Points3D += points;
                    return geo;
                }
            }
            catch (Exception) { } // the box below: Explode throws on what it cannot take apart
        var b = box();
        if (over) log.Over3D++;
        else log.ExplodeFailed++;
        return new[] { b };
    }

    /// <summary>Unique edges of SubDMesh.FaceArray: per face a vertex count, then that many zero-based indices. A shared edge comes once, in first-seen order.</summary>
    public static List<(int A, int B)> FaceEdges(IReadOnlyList<int> faces)
    {
        var e = new Edges();
        for (int i = 0, n; i < faces.Count; i += n + 1)
        {
            n = Math.Abs(faces[i]);
            for (int k = 0; k < n; k++) e.Add(faces[i + 1 + k], faces[i + 1 + (k + 1) % n]);
        }
        return e.List;
    }

    /// <summary>Unique edges of PolyFaceMesh face records, four one-based indices each: 0 = unused corner (a triangle), negative = the edge leaving that corner is hidden. Result zero-based.</summary>
    public static List<(int A, int B)> RecordEdges(IEnumerable<int[]> faces)
    {
        var e = new Edges();
        foreach (var f in faces)
        {
            int n = f.TakeWhile(v => v != 0).Count();
            for (int k = 0; k < n; k++)
                if (f[k] > 0) e.Add(f[k] - 1, Math.Abs(f[(k + 1) % n]) - 1);
        }
        return e.List;
    }

    /// <summary>Edges of a PolygonMesh of m rows of n vertices, row by row.</summary>
    public static List<(int A, int B)> GridEdges(int m, int n, bool mClosed, bool nClosed)
    {
        var e = new Edges();
        for (int i = 0; i < m; i++)
            for (int j = 0; j < n; j++)
            {
                if (j + 1 < n || nClosed) e.Add(i * n + j, i * n + (j + 1) % n);
                if (i + 1 < m || mClosed) e.Add(i * n + j, (i + 1) % m * n + j);
            }
        return e.List;
    }

    sealed class Edges
    {
        public readonly List<(int A, int B)> List = new();
        readonly HashSet<(int, int)> _seen = new();

        public void Add(int a, int b)
        {
            if (a == b || !_seen.Add(a < b ? (a, b) : (b, a))) return;
            List.Add((a, b));
            if (List.Count * 2 > MaxPointsPerObject) throw new InvalidOperationException("mesh over the per-object limit");
        }
    }
}
