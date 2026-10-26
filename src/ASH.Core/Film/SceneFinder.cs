using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Runner;

namespace ASH.Core.Film;

public enum SceneKind { Stockout, DelaySpike, Bottleneck, Whole }

/// <summary>A film-worthy stretch of a run. Start/End are absolute simulation seconds (warmup included);
/// CenterX/Y are model metres in the frame of the layout given to <see cref="SceneFinder.Find"/>. Severity is in seconds, comparable within a kind only.</summary>
public sealed record SceneCandidate(SceneKind Kind, string Title, double StartSec, double EndSec, double CenterX, double CenterY, double Severity, string Detail);

public static class SceneFinder
{
    const double StockMargin = 60, SpikeMargin = 30, BottleneckWindow = 600, BottleneckStep = 60;
    const int MaxPerKind = 5;

    /// <summary>Candidates grouped by kind (enum order), each kind by Severity descending, at most 5 per kind (Whole: 1).
    /// <paramref name="layout"/> must be in model metres relative to the model origin (what ArcLayout.Load(...).Layout returns), so CP positions are the film's coordinates.
    /// Times in Title/Detail are hh:mm of absolute simulation time (the warmup is included, like StartSec/EndSec).
    /// A stockin without a stockout began before warmup (window starts at stockin.T - value); a stockout without a stockin runs to simSec.
    /// A delay spike is a delivery of at least 3x the mean of the same material's other deliveries (leave-one-out; materials with fewer than 2 deliveries are skipped, zero delays never count)
    /// that also lies in the slowest 1% (at least the single slowest) of that material's own deliveries - per material, so one slow material cannot hide the others.
    /// Events with a non-finite or negative time/value are ignored.
    /// A material or CP missing from the layout is placed at the layout centre.</summary>
    public static IReadOnlyList<SceneCandidate> Find(IReadOnlyList<SimEvent> events, Layout.Layout layout,
        IReadOnlyDictionary<string, string> unloadCpByMaterial, double simSec, double warmupSec)
    {
        var cps = new Dictionary<string, Point2>();
        foreach (var c in layout.Cps) cps[c.Name] = c.Position;
        // shortcut: centre = middle of the CP bounding box, blocks and paths are ignored; fine for a camera target
        var centre = cps.Count == 0 ? new Point2(0, 0)
            : new Point2((cps.Values.Min(p => p.X) + cps.Values.Max(p => p.X)) / 2, (cps.Values.Min(p => p.Y) + cps.Values.Max(p => p.Y)) / 2);
        Point2 Pos(string? cp) => cp != null && cps.TryGetValue(cp, out var p) ? p : centre;
        string? UnloadCp(string material) => unloadCpByMaterial.TryGetValue(material, out var cp) ? cp : null;
        double Clamp(double t) => Math.Max(0, Math.Min(simSec, t));
        string Sec(double v) => v.ToString("0", CultureInfo.InvariantCulture);
        var sorted = events.Where(e => !double.IsNaN(e.T + e.Value) && !double.IsInfinity(e.T + e.Value) && e.T >= 0 && e.Value >= 0).OrderBy(e => e.T).ToList();

        var stock = new List<SceneCandidate>();
        foreach (var g in sorted.Where(e => e.Kind == "stockout" || e.Kind == "stockin").GroupBy(e => e.Material))
        {
            var pos = Pos(UnloadCp(g.Key));
            double? open = null;
            void Add(double s, double e) => stock.Add(new SceneCandidate(SceneKind.Stockout, "자재 품절: " + g.Key,
                Clamp(s - StockMargin), Clamp(e + StockMargin), pos.X, pos.Y, e - s, $"{Hm(s)}~{Hm(e)} 재고 0 ({Sec(e - s)}초)"));
            foreach (var ev in g)
            {
                if (ev.Kind == "stockout") open ??= ev.T;
                else { Add(open ?? Math.Max(warmupSec, ev.T - ev.Value), ev.T); open = null; }
            }
            if (open != null) Add(open.Value, simSec);
        }

        var spikes = new List<SceneCandidate>();
        foreach (var g in sorted.Where(e => e.Kind == "deliver").GroupBy(e => e.Material))
        {
            var list = g.ToList();
            if (list.Count < 2) continue;
            double total = list.Sum(e => e.Value);
            var cutoff = list.Select(e => e.Value).OrderByDescending(v => v).ElementAt((int)Math.Ceiling(list.Count * 0.01) - 1);
            foreach (var d in list)
            {
                double others = (total - d.Value) / (list.Count - 1);
                if (d.Value <= 0 || others <= 0 || d.Value < 3 * others || d.Value < cutoff) continue;
                var pos = Pos(UnloadCp(d.Material) ?? d.Cp);
                spikes.Add(new SceneCandidate(SceneKind.DelaySpike, "지연: " + d.Material, Clamp(d.T - d.Value - SpikeMargin), Clamp(d.T + SpikeMargin),
                    pos.X, pos.Y, d.Value, $"평균 {Sec(others)}초 → {Sec(d.Value)}초"));
            }
        }

        var bottleneck = new List<SceneCandidate>();
        var waits = sorted.Where(e => e.Kind == "wait" && e.Cp != "").ToList();
        double bestSum = 0, bestStart = 0, bestCpSum = 0; string? bestCp = null;
        for (double s = warmupSec; waits.Count > 0 && (s == warmupSec || s + BottleneckWindow <= simSec); s += BottleneckStep)
        {
            var w = waits.Where(e => e.T >= s && e.T < s + BottleneckWindow).ToList();
            double sum = w.Sum(e => e.Value);
            if (sum <= bestSum) continue;
            var top = w.GroupBy(e => e.Cp).Select(g => (Cp: g.Key, Sum: g.Sum(e => e.Value))).OrderByDescending(x => x.Sum).First();
            (bestSum, bestStart, bestCp, bestCpSum) = (sum, s, top.Cp, top.Sum);
        }
        if (bestCp != null)
        {
            var pos = Pos(bestCp);
            double end = Math.Min(simSec, bestStart + BottleneckWindow);
            bottleneck.Add(new SceneCandidate(SceneKind.Bottleneck, "병목: " + bestCp, bestStart, end, pos.X, pos.Y, bestSum,
                $"{Hm(bestStart)}~{Hm(end)} 대기 합 {Sec(bestSum)}초 ({bestCp} {Sec(bestCpSum)}초)"));
        }

        var whole = new SceneCandidate(SceneKind.Whole, "전체", warmupSec, simSec, centre.X, centre.Y, 0, $"{Hm(warmupSec)}~{Hm(simSec)} 전체 구간");
        return stock.OrderByDescending(c => c.Severity).Take(MaxPerKind)
            .Concat(spikes.OrderByDescending(c => c.Severity).Take(MaxPerKind))
            .Concat(bottleneck).Append(whole).ToList();
    }

    static string Hm(double sec) { int m = (int)(Math.Max(0, sec) / 60); return $"{m / 60:00}:{m % 60:00}"; }
}
