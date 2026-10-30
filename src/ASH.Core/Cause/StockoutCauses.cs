using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ASH.Core.Geometry;
using ASH.Core.Input;
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
/// Missing = the data that was not there ("matstat", "tsplit", "차량 시간", "경로 길이", "입력").
/// Cycle = a station of a Cycle group (no calls; the call fields stay NaN): FreeLapSec = load + unload + extra + load CP → [buffer] → this unload CP
/// [→ return legs] at the loaded speed + back to the load CP at the empty speed; LapSec = the mean WindowSec ÷ Jobs of the group's active vehicles;
/// SupplyRate = Active × Capacity ÷ FreeLapSec, UseRate = the group's stations' containers a second; WaitAvg = the station's containers held on vehicles.</summary>
public sealed record StockoutCheck(string Material, string Group, double StopSec, double CallThreshold, double ContainerSec, double CoverageSec,
    double MinLeadSec, double EmptyRunSec, double FreeLegSec, double PickAvg, double PickMax, double DelivAvg, double DelivMax,
    double DispatchWaitSec, double TrafficSec, double StopShare, double IdleShare, IReadOnlyList<KeyValuePair<string, double>> RouteBlocks, int Deadlocks,
    string Verdict, string Fix, int? SuggestCall, string Where, IReadOnlyList<string> Refs, IReadOnlyList<string> Missing, double? X = null, double? Y = null,
    bool Cycle = false, double FreeLapSec = double.NaN, double LapSec = double.NaN, double SupplyRate = double.NaN, double UseRate = double.NaN,
    int? Active = null, int MaxCount = 0, double WaitAvg = double.NaN);

/// <summary>Materials the line stood for (StopTimeSec &gt; 0): structural (the stock left at the call lasts less than the fastest possible delivery), dispatch
/// wait, traffic or deadlock on the route, or variability (the average delivery is in time, the slowest is not). First class that applies wins.
/// A Cycle group's station: structural (even MaxCount vehicles at the free lap supply less than its stations use), too few vehicles (Fleet),
/// traffic (the actual lap is inflated, or a deadlock), its turn (vehicles held at another station of its row), else other.</summary>
public static class StockoutCauses
{
    public const string Structural = "구조적", Dispatch = "배차 대기", Traffic = "교통·교착", Variability = "변동", Thin = "여유 부족", Other = "기타", NoData = "자료 부족";
    public const string Fleet = "대수 부족", Turn = "순환 차례 대기";

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
        if (k.Cycle)
        {
            // "자유 바퀴 1:20 · 실제 바퀴 1:40 · 공급 90·소비 120 · 대수 2/4 · 공급 대기 0.2" (containers an hour)
            Add("자유 바퀴", k.FreeLapSec, Ms); Add("실제 바퀴", k.LapSec, Ms);
            if (!double.IsNaN(k.SupplyRate) && !double.IsNaN(k.UseRate)) parts.Add($"공급 {PerHour(k.SupplyRate)}·소비 {PerHour(k.UseRate)}");
            parts.Add($"대수 {(k.Active?.ToString(CultureInfo.InvariantCulture) ?? "-")}/{k.MaxCount}");
            Add("공급 대기", k.WaitAvg, N);
        }
        else
        {
            Add("호출 시 남은", k.CoverageSec, Ms); Add("최소 리드", k.MinLeadSec, Ms);
            if (!double.IsNaN(k.DelivAvg)) parts.Add($"리드 평균 {Ms(k.DelivAvg)} / 최대 {Ms(k.DelivMax)}");
            Add("배차 대기", k.DispatchWaitSec, Ms); Add("이동 초과", k.TrafficSec, Ms);
        }
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
    static string PerHour(double perSec) => (perSec * 3600).ToString("0.#", CultureInfo.InvariantCulture);

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

        double container = ContainerSec(m);
        double call = m?.CallThreshold ?? double.NaN, coverage = call * container;

        // free-speed lead: standby -> load CP empty, load, loaded legs, extra, unload
        MaterialRoute? mv = null;
        try { mv = RouteView.Build(c.Layout, input, mr.Name); } catch (Exception e) when (!(e is OutOfMemoryException)) { }
        if (group?.IsCycle == true) return CycleOne(c, r, mr, m, route, group, type, mv, container, missing);
        double empty = double.NaN, free = double.NaN;
        if (mv != null && type != null && route != null)
        {
            // several load CPs take turns: each one's trip (and empty run) counts once, the mean is the typical one
            var trips = mv.Trips(false);
            double vl = type.SpeedLoaded, ve = type.SpeedEmpty > 0 ? type.SpeedEmpty : type.SpeedLoaded;
            if (vl > 0 && mv.Legs.Any(l => l.Kind == RouteLegKind.Supply) && trips.Count > 0 && trips.All(t => t.Distance != null))
                free = type.LoadTime + trips.Average(t => t.Distance!.Value) / vl + Math.Max(0, route.ExtraTime) + type.UnloadTime;
            // per load CP the nearest standby CP; no standby CP = the vehicle may be anywhere, the floor takes 0 (shortcut)
            var runs = route.FromCps.Select(f =>
            {
                if (group!.StandbyCPs.Count == 0 || group.StandbyCPs.Contains(f)) return 0;
                var out_ = mv.Legs.Where(l => l.Kind == RouteLegKind.Standby && l.To == f && l.Distance != null).ToList();
                return ve > 0 && out_.Count > 0 ? out_.Min(l => l.Distance!.Value) / ve : double.NaN;
            }).ToList();
            if (runs.All(x => !double.IsNaN(x))) empty = runs.Average();
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

        var (stopShare, idle, routeBlocks, blockWaits, deadlocks) = Shared(c, r, g, mv, missing);

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
            verdict = Traffic; fix = TrafficFix(blockWaits, deadlocks);
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

    /// <summary>One container's use in seconds (TaktTime × UnitsPerContainer / UnitsPerCycle); NaN without the material or a value.</summary>
    static double ContainerSec(MaterialInfo? m) =>
        m != null && m.UnitsPerCycle > 0 && m.TaktTime > 0 && m.UnitsPerContainer > 0 ? m.TaktTime * m.UnitsPerContainer / m.UnitsPerCycle : double.NaN;

    /// <summary>The group's standing and idle shares, the route's blocks with their wait per job and the deadlocks: the same for call and cycle stations.</summary>
    static (double StopShare, double Idle, List<string> RouteBlocks, List<KeyValuePair<string, double>> BlockWaits, int Deadlocks) Shared(
        CauseContext c, ResultSet r, string g, MaterialRoute? mv, List<string> missing)
    {
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
        return (stopShare, idle, routeBlocks, blockWaits, deadlocks);
    }

    static string TrafficFix(List<KeyValuePair<string, double>> blockWaits, int deadlocks)
    {
        var top = blockWaits.Take(2).Select(kv => $"{kv.Key} block 점검 · 대기 {kv.Value:0}초").ToList();
        if (deadlocks > 0) top.Insert(0, $"교착 {deadlocks}회 확인");
        return top.Count > 0 ? string.Join(" · ", top) : "경로 정체 점검";
    }

    /// <summary>A station of a Cycle group: no call, so the rates of the lap decide (see <see cref="StockoutCheck"/> for the definitions).</summary>
    static StockoutCheck CycleOne(CauseContext c, ResultSet r, MaterialResult mr, MaterialInfo? m, Route? route, AgvGroup group, AgvType? type,
        MaterialRoute? mv, double container, List<string> missing)
    {
        string g = group.Group, from = route?.SupplyFromCP ?? "", to = route?.SupplyToCP ?? "";

        // free lap: load + unload + extra, the loaded chain (supply then return legs, in order) and back to the load CP empty
        double free = double.NaN;
        if (mv != null && type != null && route != null && type.SpeedLoaded > 0)
        {
            double vl = type.SpeedLoaded, ve = type.SpeedEmpty > 0 ? type.SpeedEmpty : vl;
            var chain = mv.Legs.Where(l => l.Kind != RouteLegKind.Standby).ToList();
            string last = chain.Count > 0 ? chain[chain.Count - 1].To : to;
            // several load CPs take turns: each one's lap (its first leg, the shared chain, back to it) counts once, the mean is the typical lap
            var rounds = mv.Trips(true).Select(t => (t.Distance, Back: t.From == last ? 0 : Distance(c.Layout, last, t.From))).ToList();
            if (chain.Any(l => l.Kind == RouteLegKind.Supply) && rounds.Count > 0 && rounds.All(x => x.Distance != null && x.Back != null))
                free = type.LoadTime + type.UnloadTime + Math.Max(0, route.ExtraTime) + rounds.Average(x => x.Distance!.Value / vl + x.Back!.Value / ve);
        }
        if (double.IsNaN(free)) missing.Add("경로 길이");

        var stations = c.Input.Materials.Where(x => x.Group == g).ToList();
        double use = stations.Count == 0 ? double.NaN : stations.Sum(x => 1 / ContainerSec(x)); // NaN when one station lacks a value
        int cap = Math.Max(1, type?.Capacity ?? 1);
        double Rate(int n) => n * cap / free;
        int? active = r.Groups.FirstOrDefault(x => x.Group == g)?.ActiveCount;

        var (stopShare, idle, routeBlocks, blockWaits, deadlocks) = Shared(c, r, g, mv, missing);
        var laps = (r.Vehicles ?? Array.Empty<VehicleResult>()).Where(v => v.Group == g && v.Index <= (active ?? int.MaxValue) && v.Jobs > 0)
            .Select(v => v.WindowSec / v.Jobs).ToList();
        double lap = laps.Count > 0 ? laps.Average() : double.NaN;
        if (double.IsNaN(lap) && !missing.Contains("차량 시간")) missing.Add("차량 시간");

        // vehicles held for the unload threshold at another station of the same Materials row
        var mats = r.Materials.GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var held = m == null ? null : c.Input.Materials.Where(x => x.SourceName == m.SourceName && x.Material != m.Material)
            .Select(x => mats.TryGetValue(x.Material, out var o) ? o : null).Where(o => o != null && o.WaitAvg >= 1).OrderByDescending(o => o!.WaitAvg).FirstOrDefault();

        string verdict, fix;
        bool rates = !double.IsNaN(free) && !double.IsNaN(use);
        if (rates && Rate(group.MaxCount) < use) { verdict = Structural; fix = $"최대 {group.MaxCount}대로도 공급 부족 · 순환 경로 단축 또는 그룹 분리"; }
        else if (rates && active is int a && Rate(a) < use) { verdict = Fleet; fix = $"{g} 대수 {Math.Max(1, (int)Math.Ceiling(use * free / cap - 1e-9))}대 이상"; }
        else if (deadlocks > 0 || (rates && !double.IsNaN(lap) && lap > free * (1 + TrafficFactor) && lap - free >= MinWaitSec))
        { verdict = Traffic; fix = TrafficFix(blockWaits, deadlocks); }
        else if (rates && !double.IsNaN(lap) && held != null) { verdict = Turn; fix = $"{held.Name} UnloadThreshold 낮춤 · 공정 분리"; }
        else if (rates && !double.IsNaN(lap)) { verdict = Other; fix = "이벤트 기록 확인 · 초기 재고"; }
        else { verdict = NoData; fix = ""; }

        var refs = (mv?.Stops.Select(s => s.Name) ?? new[] { from, to }.Where(s => s.Length > 0)).Concat(routeBlocks).Distinct().ToList();
        var pos = c.Layout.Cps.FirstOrDefault(x => x.Name == to)?.Position;
        double nan = double.NaN;
        return new StockoutCheck(mr.Name, g, mr.StopTimeSec, nan, container, nan, nan, nan, nan, nan, nan, nan, nan, nan, nan, stopShare, idle, blockWaits,
            deadlocks, verdict, fix, null, to.Length > 0 ? to : mr.Name, refs, missing, pos?.X, pos?.Y,
            true, free, lap, active is int n ? Rate(n) : nan, use, active, group.MaxCount, mr.WaitAvg);
    }

    /// <summary>Directed shortest driving distance a → b over the paths (the route view's tracer); null when it cannot be driven.</summary>
    static double? Distance(Layout.Layout l, string a, string b)
    {
        try
        {
            var t = DetourCauses.Tracer(l)(a, b);
            return t == null || double.IsPositiveInfinity(t.Value.Dist) ? null : t.Value.Dist;
        }
        catch (Exception e) when (!(e is OutOfMemoryException)) { return null; }
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
