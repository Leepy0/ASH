using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Layout;
using ASH.Core.Traffic;

namespace ASH.Core.Cause;

/// <summary>Blocks claimed too early or released too late: stop CPs near block edges (layout), hold per pass against the pure crossing time (run).</summary>
public static class BlockHoldCauses
{
    // run rule: a pass is flagged when its excess over the pure crossing time is both long and large against it
    const double MinExcessSec = 10, ExcessRatio = 2, HighWaitSec = 10;
    const int MinPasses = 5;

    sealed record Hit(string Block, CauseItem Item);

    public static IEnumerable<CauseItem> Find(CauseContext c)
    {
        var hits = LayoutHits(c);
        foreach (var h in hits) yield return h.Item;
        if (c.Result != null)
            foreach (var x in RunItems(c, hits)) yield return x;
    }

    static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
    static string F0(double v) => v.ToString("0", CultureInfo.InvariantCulture);
    static double Up(double v) => Math.Ceiling(v * 10 - 1e-9) / 10; // the shown move must really clear it

    /// <summary>Stop CPs (every type but traffic) that sit in a block, between the traffic CP and the entry (a stopped vehicle has already claimed it),
    /// or less than one vehicle length past the exit (its tail is still inside, AutoMod releases on the trailing edge). Entry geometry is
    /// TrafficCpGenerator's: the traffic CP sits ClaimDist before the entry. One finding per block and CP.</summary>
    static List<Hit> LayoutHits(CauseContext c)
    {
        var l = c.Layout; var hits = new List<Hit>();
        double tol = l.Tolerance, claim = Math.Max(0, c.ClaimDist), len = c.VehicleLength, maxOff = new CheckOptions().CpMaxOffset;
        var stops = l.Cps.Where(x => x.Type != "traffic").ToList();
        var onPaths = new Dictionary<LayoutCp, List<(LayoutPath Path, double At)>>();
        List<(LayoutPath Path, double At)> OnPaths(LayoutCp cp)
        {
            if (onPaths.TryGetValue(cp, out var r)) return r;
            r = new List<(LayoutPath, double)>();
            foreach (var p in l.Paths)
            {
                double off = 0;
                foreach (var s in p.Segments)
                {
                    double d = s.DistanceAlong(cp.Position);
                    if (cp.Position.DistanceTo(s.PointAt(d)) <= maxOff) { r.Add((p, off + d)); break; }
                    off += s.Length;
                }
            }
            return onPaths[cp] = r;
        }
        CauseItem Item(LayoutBlock b, LayoutCp cp, string what, string detail, string fix) =>
            new CauseItem(CauseKinds.BlockHold, 1, $"{b.Name} {what}", b.Name, detail, fix, null, cp.Position.X, cp.Position.Y, new[] { b.Name, cp.Name });

        foreach (var b in l.Blocks)
        {
            var near = b.Rect.Expand(Math.Max(claim, len) + tol); // path distance is never shorter than the straight one
            var spans = new Dictionary<LayoutPath, List<(double From, double To)>>();
            foreach (var cp in stops.Where(x => near.Contains(x.Position)))
            {
                CauseItem? item = null;
                bool inRect = b.Rect.Contains(cp.Position);
                foreach (var (path, at) in OnPaths(cp))
                {
                    if (!spans.TryGetValue(path, out var sp)) spans[path] = sp = TrafficCpGenerator.Spans(path, b.Rect, tol);
                    foreach (var (from, to) in sp)
                    {
                        if (inRect)
                        {
                            if (at < from - tol || at > to + tol) continue; // in the rect, but this path only passes elsewhere
                            double outDist = to - at + len, inDist = at - from + claim; // leave through the exit (tail clear) or back before the traffic CP
                            item = Item(b, cp, "안 정차", $"{cp.Name} {cp.Type} · 입구 뒤 {F1(at - from)} m",
                                outDist <= inDist ? $"{cp.Name} 출구 쪽 {F1(Up(outDist))} m 이동" : $"{cp.Name} 입구 쪽 {F1(Up(inDist))} m 이동");
                            break;
                        }
                        for (int dir = 0; dir < (path.IsTwoWay ? 2 : 1) && item == null; dir++) // a two-way path is entered from both ends
                        {
                            double enter = dir == 0 ? from : to, exit = dir == 0 ? to : from, sign = dir == 0 ? 1 : -1;
                            double before = (enter - at) * sign, after = (at - exit) * sign;
                            if (claim > 0 && before > tol && before < claim - tol)
                                item = Item(b, cp, "이른 점유", $"{cp.Name} 입구 앞 {F1(before)} m · 점유 거리 {F1(claim)} m", $"{cp.Name} 입구 쪽 {F1(Up(claim - before))} m 이동");
                            else if (after > tol && after < len - tol)
                                item = Item(b, cp, "늦은 해제", $"{cp.Name} 출구 뒤 {F1(after)} m · 차 길이 {F1(len)} m", $"{cp.Name} 출구 쪽 {F1(Up(len - after))} m 이동");
                        }
                        if (item != null) break;
                    }
                    if (item != null) break;
                }
                // in the rect but on no path: still a stop in the block, nothing to measure
                if (item == null && inRect) item = Item(b, cp, "안 정차", $"{cp.Name} {cp.Type}", "");
                if (item != null) hits.Add(new Hit(b.Name, item));
            }
        }
        return hits;
    }

    /// <summary>Per block: the time one pass holds it against the pure crossing time. Hold = the report's Av_Time (seconds per occupation, AutoMod's own,
    /// independent of any window) when the report is there, else Utilization x Cap x run length / Passes (result.tsv; AutoMod's block statistics run
    /// from time 0, the model resets nothing at warmup, so the window is the whole run, not SimLength - Warmup; Utilization is the mean contents / Cap).
    /// Pure = (longest path stretch inside the rect + one vehicle length) / the slowest loaded speed among the groups' types
    /// (the slow end keeps fast-vehicle blocks from being flagged for nothing).</summary>
    static IEnumerable<CauseItem> RunItems(CauseContext c, List<Hit> hits)
    {
        var res = c.Result!;
        var speeds = c.Input.Groups.Select(g => c.Input.AgvTypes.FirstOrDefault(t => t.Type == g.Type)).Where(t => t != null).Select(t => t!.SpeedLoaded)
            .Where(v => v > 0).ToList();
        if (speeds.Count == 0) speeds = c.Input.AgvTypes.Select(t => t.SpeedLoaded).Where(v => v > 0).ToList();
        if (speeds.Count == 0) yield break;
        double speed = speeds.Min(), runSec = res.SimSec > 0 ? res.SimSec : c.Input.Settings.SimLengthSec;
        foreach (var br in res.Blocks)
        {
            var b = c.Layout.Blocks.FirstOrDefault(x => x.Name == br.Name);
            if (b == null || br.Passes < MinPasses) continue;
            var st = c.Report?.Blocks.FirstOrDefault(x => x.Name == br.Name);
            double hold = st != null && st.AvTime > 0 ? st.AvTime : br.Utilization * Math.Max(1, b.Cap) * runSec / br.Passes;
            if (double.IsNaN(hold)) continue;
            double inside = c.Layout.Paths.SelectMany(p => TrafficCpGenerator.Spans(p, b.Rect, c.Layout.Tolerance)).Select(s => s.To - s.From).DefaultIfEmpty(0).Max();
            double pure = (inside + c.VehicleLength) / speed, excess = hold - pure;
            if (excess <= MinExcessSec || excess <= ExcessRatio * pure) continue;
            var cause = hits.FirstOrDefault(h => h.Block == b.Name && h.Item.Fix.Length > 0)?.Item;
            yield return new CauseItem(CauseKinds.BlockHold, br.AvgWaitSec >= HighWaitSec ? 2 : 1, $"{b.Name} 초과 점유 {F0(excess)}초", b.Name,
                $"1회 점유 {F0(hold)}초 · 통과 {F0(pure)}초" + (double.IsNaN(br.AvgWaitSec) ? "" : $" · 대기 평균 {F0(br.AvgWaitSec)}초"), cause?.Fix ?? "", null,
                cause?.X ?? (b.Rect.Min.X + b.Rect.Max.X) / 2, cause?.Y ?? (b.Rect.Min.Y + b.Rect.Max.Y) / 2, cause?.Refs ?? new[] { b.Name });
        }
    }
}
