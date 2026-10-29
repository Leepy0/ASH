using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Runner;

namespace ASH.Core.Cause;

/// <summary>Why one material ran out ("자재 품절") in one run. Seconds; NaN = not known (the input, the layout or the run lacks it).
/// ContainerSec = one container's use (TaktTime × UnitsPerContainer / UnitsPerCycle); CoverageSec = CallThreshold × ContainerSec, what the line can run on
/// when the call goes out. MinLeadSec = EmptyRunSec + FreeLegSec, call to delivered with no waiting: standby → load CP at the empty speed, then load + loaded
/// legs at the loaded speed + extra + unload. Pick/Deliv = the run's call → load CP arrival and call → unloaded (matstat). DispatchWaitSec = PickAvg − EmptyRunSec
/// (queue for a free vehicle); TrafficSec = (DelivAvg − PickAvg) − FreeLegSec (standing and slow driving on the loaded legs). StopShare = the group's standing
/// share of its job time (tsplit), IdleShare = idle share of its active vehicles' window (vehicle split). RouteBlocks = blocks the route passes, wait per job
/// of the group (claims ÷ jobs × the block's average wait; the average wait alone without tsplit), most first. Deadlocks = episodes with a vehicle of the group
/// or a block of the route. Verdict = one of <see cref="StockoutCauses"/>' classes; SuggestCall = the CallThreshold the fix names (null = none).
/// Missing = the data that was not there ("matstat", "tsplit", "차량 시간", "경로 길이", "입력").</summary>
public sealed record StockoutCheck(string Material, string Group, double StopSec, double CallThreshold, double ContainerSec, double CoverageSec,
    double MinLeadSec, double EmptyRunSec, double FreeLegSec, double PickAvg, double PickMax, double DelivAvg, double DelivMax,
    double DispatchWaitSec, double TrafficSec, double StopShare, double IdleShare, IReadOnlyList<KeyValuePair<string, double>> RouteBlocks, int Deadlocks,
    string Verdict, string Fix, int? SuggestCall, string Where, IReadOnlyList<string> Refs, IReadOnlyList<string> Missing, double? X = null, double? Y = null);

/// <summary>Materials the line stood for (StopTimeSec &gt; 0): structural (the stock left at the call lasts less than the fastest possible delivery), dispatch
/// wait, traffic or deadlock on the route, or variability (the average delivery is in time, the slowest is not). First class that applies wins.</summary>
public static class StockoutCauses
{
    public const string Structural = "구조적", Dispatch = "배차 대기", Traffic = "교통·교착", Variability = "변동", Thin = "여유 부족", Other = "기타", NoData = "자료 부족";

    /// <summary>A wait counts as large from this many seconds and this multiple of its free time (pick ≥ 2× the free empty run; loaded leg ≥ 1.5× free).</summary>
    const double MinWaitSec = 30, DispatchFactor = 1.0, TrafficFactor = 0.5, IdleShareForPlacement = 0.3, StopShareForTraffic = 0.3;

    public static IEnumerable<CauseItem> Find(CauseContext c) =>
        Check(c).Select(k => new CauseItem(CauseKinds.Stockout, k.StopSec >= 600 ? 2 : k.StopSec >= 60 ? 1 : 0, $"{k.Material} 자재 품절 · {k.Verdict}", k.Where,
            Detail(k), k.Fix, null, k.X, k.Y, k.Refs));

    /// <summary>"호출 시 남은 1:20 · 최소 리드 2:05 · 리드 평균 1:50 / 최대 4:10 · 배차 대기 0:40 · 이동 초과 0:15 · 정지 22% · 유휴 60% · 품절 12:00" (unknown parts left out).</summary>
    public static string Detail(StockoutCheck k)
    {
        var parts = new List<string>();
        void Add(string label, double v, Func<double, string> f) { if (!double.IsNaN(v)) parts.Add(label + " " + f(v)); }
        Add("호출 시 남은", k.CoverageSec, Ms); Add("최소 리드", k.MinLeadSec, Ms);
        if (!double.IsNaN(k.DelivAvg)) parts.Add($"리드 평균 {Ms(k.DelivAvg)} / 최대 {Ms(k.DelivMax)}");
        Add("배차 대기", k.DispatchWaitSec, Ms); Add("이동 초과", k.TrafficSec, Ms);
        Add("정지", k.StopShare, Pct); Add("유휴", k.IdleShare, Pct);
        if (k.Deadlocks > 0) parts.Add($"교착 {k.Deadlocks}회");
        parts.Add("품절 " + Ms(k.StopSec));
        if (k.Missing.Count > 0) parts.Add("자료 부족: " + string.Join("·", k.Missing));
        return string.Join(" · ", parts);
    }

    /// <summary>Seconds as m:ss ("-" for NaN); an hour or more as h:mm:ss.</summary>
    public static string Ms(double sec)
    {
        if (double.IsNaN(sec) || double.IsInfinity(sec)) return "-";
        long s = (long)Math.Round(Math.Max(0, Math.Min(sec, 1e9)), MidpointRounding.AwayFromZero);
        return s >= 3600 ? $"{s / 3600}:{s % 3600 / 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    static string Pct(double f) => Math.Round(f * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    static string N(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>One check per material of the run with stop time, most stop first. Never throws on missing rows or an empty layout: what cannot be worked out is NaN and named in Missing.</summary>
    public static IReadOnlyList<StockoutCheck> Check(CauseContext c)
    {
        var res = new List<StockoutCheck>();
        if (c.Result is not { } r) return res;
        foreach (var mr in r.Materials.Where(x => x.StopTimeSec > 0).OrderByDescending(x => x.StopTimeSec))
            res.Add(One(c, r, mr));
        return res;
    }

    static StockoutCheck One(CauseContext c, ResultSet r, MaterialResult mr)
    {
        var missing = new List<string>();
        var input = c.Input;
        var m = input.Materials.FirstOrDefault(x => x.Material == mr.Name);
        var route = input.Routes.FirstOrDefault(x => x.Material == mr.Name);
        var group = input.Groups.FirstOrDefault(x => x.Group == (m?.Group ?? mr.Group));
        var type = group == null ? null : input.AgvTypes.FirstOrDefault(x => x.Type == group.Type);
        string g = m?.Group ?? mr.Group;
        if (m == null || route == null || type == null) missing.Add("입력");

        double container = m != null && m.UnitsPerCycle > 0 && m.TaktTime > 0 && m.UnitsPerContainer > 0 ? m.TaktTime * m.UnitsPerContainer / m.UnitsPerCycle : double.NaN;
        double call = m?.CallThreshold ?? double.NaN, coverage = call * container;

        // free-speed lead: standby -> load CP empty, load, loaded legs, extra, unload
        MaterialRoute? mv = null;
        try { mv = RouteView.Build(c.Layout, input, mr.Name); } catch (Exception e) when (!(e is OutOfMemoryException)) { }
        double empty = double.NaN, free = double.NaN;
        if (mv != null && type != null && route != null)
        {
            var supply = mv.Legs.Where(l => l.Kind == RouteLegKind.Supply).ToList();
            double vl = type.SpeedLoaded, ve = type.SpeedEmpty > 0 ? type.SpeedEmpty : type.SpeedLoaded;
            if (vl > 0 && supply.Count > 0 && supply.All(l => l.Distance != null))
                free = type.LoadTime + supply.Sum(l => l.Distance!.Value) / vl + Math.Max(0, route.ExtraTime) + type.UnloadTime;
            var out_ = mv.Legs.Where(l => l.Kind == RouteLegKind.Standby && l.To == route.SupplyFromCP).ToList();
            if (group!.StandbyCPs.Count == 0 || group.StandbyCPs.Contains(route.SupplyFromCP)) empty = 0; // shortcut: no standby CP = the vehicle may be anywhere, the floor takes 0
            else if (ve > 0 && out_.Any(l => l.Distance != null)) empty = out_.Where(l => l.Distance != null).Min(l => l.Distance!.Value) / ve;
        }
        if (double.IsNaN(free) || double.IsNaN(empty)) missing.Add("경로 길이");
        double minLead = empty + free;

        var st = r.MaterialStats?.FirstOrDefault(x => x.Name == mr.Name);
        if (st == null) missing.Add("matstat");
        double pickAvg = st?.PickAvg ?? double.NaN, pickMax = st?.PickMax ?? double.NaN, delivAvg = st?.DelivAvg ?? double.NaN, delivMax = st?.DelivMax ?? double.NaN;
        double dispatch = Math.Max(0, pickAvg - (double.IsNaN(empty) ? 0 : empty)), traffic = Math.Max(0, delivAvg - pickAvg - (double.IsNaN(free) ? 0 : free));
        if (st == null) dispatch = traffic = double.NaN;
        else
        {
            if (double.IsNaN(empty)) dispatch = double.NaN;
            if (double.IsNaN(free)) traffic = double.NaN;
        }

        var gt = r.GroupTimes?.FirstOrDefault(x => x.Group == g);
        if (gt == null) missing.Add("tsplit");
        double stopShare = gt != null && gt.JobSec > 0 ? gt.StopSec / gt.JobSec : double.NaN;
        var active = r.Groups.FirstOrDefault(x => x.Group == g)?.ActiveCount ?? int.MaxValue;
        var splits = (r.Vehicles ?? Array.Empty<VehicleResult>()).Where(v => v.Group == g && v.Index <= active && v.Split != null).Select(v => v.Split!).ToList();
        double win = splits.Sum(s => s.Window);
        double idle = win > 0 ? splits.Sum(s => Math.Max(0, s.Idle)) / win : double.NaN;
        if (double.IsNaN(idle)) missing.Add("차량 시간");

        // blocks the route passes (supply legs and the run from standby), and the deadlocks that touched the group or them
        var routeBlocks = RouteBlocks(c, mv);
        var wait = r.Blocks.GroupBy(b => b.Name).ToDictionary(x => x.Key, x => x.First().AvgWaitSec, StringComparer.Ordinal);
        var blockWaits = routeBlocks.Select(b =>
        {
            double w = wait.TryGetValue(b, out var a) && a > 0 ? a : 0;
            double per = gt == null ? w : gt.Jobs > 0 && gt.BlockClaims.TryGetValue(b, out var n) ? n * w / gt.Jobs : 0;
            return new KeyValuePair<string, double>(b, per);
        }).Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).ToList();
        var rb = new HashSet<string>(routeBlocks, StringComparer.Ordinal);
        int deadlocks = (r.DeadlockEvents ?? Array.Empty<DeadlockEvent>()).Count(e => e?.Links != null &&
            e.Links.Any(k => k != null && (k.Group == g || rb.Contains(k.WaitsFor ?? "") || (k.Holds ?? Array.Empty<string>()).Any(rb.Contains))));

        // the verdict: first class that applies
        string verdict, fix; int? suggest = null;
        string from = route?.SupplyFromCP ?? "", to = route?.SupplyToCP ?? "";
        int Need(double lead) => (int)Math.Ceiling(lead / container - 1e-9) + 1;
        bool hasStat = st != null;
        if (!double.IsNaN(minLead) && !double.IsNaN(coverage) && minLead >= coverage)
        {
            verdict = Structural; suggest = Need(minLead);
            fix = $"CallThreshold {N(call)}→{suggest} · 대수 무관";
        }
        else if (hasStat && !double.IsNaN(dispatch) && dispatch >= Math.Max(MinWaitSec, DispatchFactor * empty) && (double.IsNaN(traffic) || dispatch >= traffic))
        {
            verdict = Dispatch;
            fix = !double.IsNaN(idle) && idle >= IdleShareForPlacement
                ? $"대기 위치 {from} 쪽 · 배차 규칙 {group?.Dispatch ?? "-"} 점검 · 호출 몰림"
                : $"{g} 대수 +1 · 호출 몰림";
        }
        else if (deadlocks > 0 || (hasStat && !double.IsNaN(traffic) && traffic >= Math.Max(MinWaitSec, TrafficFactor * free))
                 || (!hasStat && !double.IsNaN(stopShare) && stopShare >= StopShareForTraffic))
        {
            verdict = Traffic;
            var top = blockWaits.Take(2).Select(kv => $"{kv.Key} block 점검 · 대기 {kv.Value:0}초").ToList();
            if (deadlocks > 0) top.Insert(0, $"교착 {deadlocks}회 확인");
            fix = top.Count > 0 ? string.Join(" · ", top) : "경로 정체 점검";
        }
        else if (hasStat && !double.IsNaN(coverage) && delivAvg < coverage && delivMax >= coverage)
        {
            verdict = Variability;
            int more = Math.Max(1, (int)Math.Ceiling((delivMax - coverage) / container - 1e-9));
            suggest = (int)Math.Ceiling(call - 1e-9) + more;
            fix = $"CallThreshold {N(call)}→{suggest} 또는 안전재고 +{more}";
        }
        else if (hasStat && !double.IsNaN(coverage) && delivAvg >= coverage)
        {
            verdict = Thin; suggest = Need(delivMax);
            fix = $"CallThreshold {N(call)}→{suggest}";
        }
        else if (hasStat && !double.IsNaN(coverage)) { verdict = Other; fix = "이벤트 기록 확인 · 초기 재고"; }
        else { verdict = NoData; fix = ""; }

        var refs = (mv?.Stops.Select(s => s.Name) ?? new[] { from, to }.Where(s => s.Length > 0)).Concat(routeBlocks).Distinct().ToList();
        var pos = c.Layout.Cps.FirstOrDefault(x => x.Name == to)?.Position;
        return new StockoutCheck(mr.Name, g, mr.StopTimeSec, call, container, coverage, minLead, empty, free, pickAvg, pickMax, delivAvg, delivMax,
            dispatch, traffic, stopShare, idle, blockWaits, deadlocks, verdict, fix, suggest, from.Length > 0 ? $"{from}→{to}" : mr.Name, refs, missing,
            pos?.X, pos?.Y);
    }

    /// <summary>Blocks whose rectangle a supply or standby-out leg of the route crosses, in route order.</summary>
    static List<string> RouteBlocks(CauseContext c, MaterialRoute? mv)
    {
        var res = new List<string>();
        if (mv == null || c.Layout.Blocks.Count == 0) return res;
        foreach (var leg in mv.Legs.Where(l => l.Kind == RouteLegKind.Supply || (l.Kind == RouteLegKind.Standby && mv.Stops.Any(s => s.Name == l.To && s.Role != "대기"))))
            for (int i = 0; i < leg.Points.Count; i++)
            {
                var a = leg.Points[i]; var b = i + 1 < leg.Points.Count ? leg.Points[i + 1] : a;
                // shortcut: sampled every 0.25 m, a block thinner than that across the path can be missed; clip the segment if that ever matters
                int n = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / 0.25));
                for (int k = 0; k <= n; k++)
                {
                    var p = new Point2(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n);
                    foreach (var blk in c.Layout.Blocks)
                        if (blk.Rect.Contains(p) && !res.Contains(blk.Name)) res.Add(blk.Name);
                }
            }
        return res;
    }
}
