using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ASH.Core.Lite;

/// <summary>Temporary measuring (AGVLITESTAT): how dense a drawing is per 1 m cell and which blocks are heavy, to set AGVLITE's Dense thresholds from real drawings.</summary>
public static class LiteStat
{
    public const int CellBins = 5;
    /// <summary>Upper edges (mm of line per cell) of the first four bins; the fifth is open.</summary>
    public static readonly double[] BinEdges = { 1_000, 5_000, 20_000, 100_000 };

    /// <summary>Cut: the walk stopped at its budget, so the counts cover part of the drawing. Lines: chords walked (LiteBuild.Histogram).</summary>
    public sealed record Hist(int Cells, double P50, double P90, double P99, double Max, int[] Bins, double TotalMm, bool Cut = false, long Lines = 0);

    /// <summary>Per-definition input for <see cref="BlockTable"/>; the name is for the human table only.</summary>
    public sealed record BlockRow(string Name, int Instances, int Lines, double DiagMm, int Solids3d);

    /// <summary>Calls <paramref name="cell"/>(cx, cy, length) for every cell the segment crosses, the length split by the part inside each cell.
    /// Shared with Blobs.Occupy (Task 5).
    /// shortcut: a segment spanning over 4 million cells is skipped (a corrupt coordinate, not a real line), upgrade if a drawing legitimately needs longer ones.</summary>
    internal static void WalkCells(double x1, double y1, double x2, double y2, double cellSize, Action<long, long, double> cell)
    {
        double dx = x2 - x1, dy = y2 - y1, len = Math.Sqrt(dx * dx + dy * dy);
        if (!(len > 0) || double.IsInfinity(len)) return;
        if ((Math.Abs(dx) + Math.Abs(dy)) / cellSize > 4e6) return; // decided before casting: huge values would overflow the long cell indices
        long ax = (long)Math.Floor(Math.Min(x1, x2) / cellSize), bx = (long)Math.Floor(Math.Max(x1, x2) / cellSize);
        long ay = (long)Math.Floor(Math.Min(y1, y2) / cellSize), by = (long)Math.Floor(Math.Max(y1, y2) / cellSize);
        var ts = new List<double> { 0, 1 };
        if (dx != 0) for (long k = ax + 1; k <= bx; k++) { double t = (k * cellSize - x1) / dx; if (t > 0 && t < 1) ts.Add(t); }
        if (dy != 0) for (long k = ay + 1; k <= by; k++) { double t = (k * cellSize - y1) / dy; if (t > 0 && t < 1) ts.Add(t); }
        ts.Sort();
        for (int i = 0; i + 1 < ts.Count; i++)
        {
            double t0 = ts[i], t1 = ts[i + 1];
            if (t1 <= t0) continue;
            double tm = (t0 + t1) / 2;
            cell((long)Math.Floor((x1 + dx * tm) / cellSize), (long)Math.Floor((y1 + dy * tm) / cellSize), (t1 - t0) * len);
        }
    }

    /// <summary>Chords of the entities (arcs, circles and bulges sampled with <paramref name="tol"/>, same units as the entities).</summary>
    public static IEnumerable<(double, double, double, double)> Segments(IEnumerable<LiteEntity> entities, double tol)
    {
        foreach (var e in entities)
            switch (e)
            {
                case LiteLine l: yield return (l.X1, l.Y1, l.X2, l.Y2); break;
                case LiteCircle c:
                    foreach (var s in Chain(CurveApprox.Arc(c.Cx, c.Cy, c.R, 0, 2 * Math.PI, tol), false)) yield return s;
                    break;
                case LiteArc a:
                {
                    double sw = a.EndDeg - a.StartDeg;
                    if (sw <= 0) sw += 360;
                    foreach (var s in Chain(CurveApprox.Arc(a.Cx, a.Cy, a.R, a.StartDeg * Math.PI / 180, sw * Math.PI / 180, tol), false)) yield return s;
                    break;
                }
                case LitePolyline p:
                {
                    var v = CurveApprox.Flatten(p.Vertices, p.Closed, tol);
                    foreach (var s in Chain(v.Select(q => (q.X, q.Y)).ToList(), p.Closed)) yield return s;
                    break;
                }
            }
    }

    static IEnumerable<(double, double, double, double)> Chain(List<(double X, double Y)> pts, bool closed)
    {
        int n = pts.Count;
        for (int i = 0; i + 1 < n; i++) yield return (pts[i].X, pts[i].Y, pts[i + 1].X, pts[i + 1].Y);
        if (closed && n > 2) yield return (pts[n - 1].X, pts[n - 1].Y, pts[0].X, pts[0].Y);
    }

    /// <summary>Line length per occupied cell (mm), its percentiles (nearest rank) and a count of cells per density bin. Segments and cellMm in mm.</summary>
    public static Hist Histogram(IEnumerable<(double X1, double Y1, double X2, double Y2)> segments, double cellMm)
    {
        var sum = new Dictionary<(long, long), double>();
        foreach (var (x1, y1, x2, y2) in segments)
            WalkCells(x1, y1, x2, y2, cellMm, (cx, cy, l) => sum[(cx, cy)] = (sum.TryGetValue((cx, cy), out var v) ? v : 0) + l);
        var vals = sum.Values.OrderBy(v => v).ToArray();
        var bins = new int[CellBins];
        foreach (var v in vals) { int b = 0; while (b < BinEdges.Length && v >= BinEdges[b]) b++; bins[b]++; }
        double P(double q) => vals.Length == 0 ? 0 : vals[Math.Max(0, (int)Math.Ceiling(q * vals.Length - 1e-9) - 1)];
        return new Hist(vals.Length, P(0.5), P(0.9), P(0.99), vals.Length == 0 ? 0 : vals[vals.Length - 1], bins, vals.Sum());
    }

    /// <summary>Top 30 definitions by instances x expanded lines (name breaks ties, so the table is stable).</summary>
    public static List<BlockRow> BlockTable(IEnumerable<BlockRow> defs) =>
        defs.OrderByDescending(d => (double)d.Instances * d.Lines).ThenBy(d => d.Name, StringComparer.Ordinal).Take(30).ToList();

    static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>The stat.tsv report: sections are a [name] line, then tab-separated rows.</summary>
    public static void Write(TextWriter w, string drawing, double readSec, double histSec, long lines, bool truncated, double cellMm, Hist h, IEnumerable<BlockRow> blocks, IReadOnlyDictionary<string, int> kinds)
    {
        w.NewLine = "\r\n";
        w.WriteLine("[도면]");
        w.WriteLine("파일\t" + drawing);
        w.WriteLine("선분 수\t" + lines);
        w.WriteLine("잘림	" + (truncated ? "예" : "아니오"));
        w.WriteLine("셀 크기 mm\t" + F(cellMm));
        w.WriteLine();
        w.WriteLine("[시간]");
        w.WriteLine("읽기 초\t" + F(readSec));
        w.WriteLine("히스토그램 초\t" + F(histSec));
        w.WriteLine();
        WriteHist(w, h);
        w.WriteLine();
        w.WriteLine("[블록 상위 30]");
        w.WriteLine("이름\t인스턴스\t펼친 선 수\t범위 대각선 mm\t3D 수\t인스턴스×선");
        foreach (var b in BlockTable(blocks)) w.WriteLine($"{b.Name}\t{b.Instances}\t{b.Lines}\t{F(b.DiagMm)}\t{b.Solids3d}\t{(long)b.Instances * b.Lines}");
        w.WriteLine();
        w.WriteLine("[종류별 개수]");
        foreach (var kv in kinds.OrderBy(k => k.Key, StringComparer.Ordinal)) w.WriteLine(kv.Key + "\t" + kv.Value);
    }

    /// <summary>The histogram section, shared with LiteReport.</summary>
    public static void WriteHist(TextWriter w, Hist h)
    {
        w.WriteLine("[밀도 히스토그램]");
        w.WriteLine("점유 셀\t" + h.Cells);
        w.WriteLine("50%\t" + F(h.P50));
        w.WriteLine("90%\t" + F(h.P90));
        w.WriteLine("99%\t" + F(h.P99));
        w.WriteLine("최대\t" + F(h.Max));
        string[] names = { "0~1k", "1k~5k", "5k~20k", "20k~100k", "100k~" };
        for (int i = 0; i < CellBins; i++) w.WriteLine("셀 수 " + names[i] + "\t" + h.Bins[i]);
    }
}
