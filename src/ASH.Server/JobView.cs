using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using ASH.Core.Cause;
using ASH.Core.Input;
using ASH.Core.Model;
using ASH.Core.Runner;

namespace ASH.Server;

/// <summary>A confirm run with deadlocks: Number = its place in the job's confirm runs (1-based, the cause tab's "실행 N"), Count = deadlocks of the run (summary.tsv),
/// Scenario = "group=count" per group, HasResult = its result.tsv was read for the places.</summary>
public sealed record DeadlockRun(string RunId, int Number, int Count, string Scenario, bool HasResult);

/// <summary>Where the confirm runs deadlocked: Runs = the runs with deadlocks (most first), Spots = places by episodes from the runs read (at most <see cref="JobView.DeadlockRunCap"/>),
/// Capped = more runs than that, MissingPlaces = deadlocks counted in summary.tsv but no place recorded (run folder purged, or a model from before the dlock rows).</summary>
public sealed record DeadlockView(IReadOnlyList<DeadlockRun> Runs, IReadOnlyList<DeadlockSpot> Spots, bool Capped, bool MissingPlaces)
{
    public int Total => Runs.Sum(r => r.Count);
}

/// <summary>One material that ran out in the confirm runs: StopSum / StopAvg = stop seconds summed / averaged over the confirm runs read, Runs = runs it ran out in,
/// TopRun / TopNumber = the run with the most stop (its id, its "실행 N"), Top = that run's check (lead times, verdict, fix).</summary>
public sealed record StockoutRow(string Material, string Group, double StopSum, double StopAvg, int Runs, string TopRun, int TopNumber, StockoutCheck Top);

/// <summary>Why materials ran out in the confirm runs: a row per material (most stop first), RunsRead = confirm runs whose result.tsv was read.</summary>
public sealed record StockoutView(IReadOnlyList<StockoutRow> Rows, int RunsRead);

/// <summary>Per group at the recommended counts: mean over the confirm runs. AllOk = no real failure (stockout, supply, deadlock, no result);
/// a run over the utilization limit only is a warning (UtilOver), not a failure. Reason = the first real failure.</summary>
public sealed record GroupView(string Group, string Type, int Min, int Max, int? Decided, double Util, double MinStockRatio, bool AllOk, string? Reason, bool UtilOver = false);

/// <summary>Investment at the recommended counts; Unpriced = types without a unit price (left out of the sum).</summary>
public sealed record InvestmentView(decimal Total, string Currency, IReadOnlyList<string> Unpriced, bool AnyPriced = true);

/// <summary>Per count of one group (all runs where the group had that many active): mean, min and max utilization, and how many of them failed
/// (real failures; over the utilization limit only is a warning, visible against the limit line).</summary>
public sealed record CountPoint(int Active, double Mean, double Lo, double Hi, int Runs, int Failed);

/// <summary>Traffic block numbers at the recommended counts (mean over the confirm runs that have them).</summary>
public sealed record BlockView(string Name, double Utilization, double AvgWaitSec, double Passes);

/// <summary>One active vehicle, mean over the confirm runs it was active in. Down = breakdown seconds, NaN when the model does not write it.
/// Split = where its time went (mean per part), null when the model does not write it.</summary>
public sealed record VehicleView(string Name, string Group, double Util, double BusySec, double Distance, double Jobs, double Down, TimeSplit? Split = null)
{
    public double DistPerJob => Jobs > 0 ? Distance / Jobs : double.NaN;
    public double BusyPerJob => Jobs > 0 ? BusySec / Jobs : double.NaN;
}

/// <summary>Per group, from its active vehicles: mean utilisation, mean distance and jobs per vehicle, distance and busy seconds per job.
/// Split = the vehicles' splits summed (its busy share is the group's utilization), null when a vehicle has none.</summary>
public sealed record GroupVehicleView(string Group, int Count, double Util, double Distance, double Jobs, double DistPerJob, double BusyPerJob, TimeSplit? Split = null);

/// <summary>Per material: lead times (call -> load CP, call -> unload done) and stock statistics, plus supplies and stop time. Mean over the confirm runs,
/// except the Max/Min columns (extreme over the runs). Safety is NaN when the input is unreadable. Trips = supply jobs and Distance = metres driven
/// for them (pickup run to home, after warmup); NaN for models that do not write them.</summary>
public sealed record MaterialView(string Name, string Group, double Supplies, double Expected, double StopSec, double PickAvg, double PickMax,
    double DelivAvg, double DelivMax, double StockAvg, double StockStd, double StockMin, double StockMax, double Safety, double Trips = double.NaN, double Distance = double.NaN)
{
    public double DistPerTrip => Trips > 0 ? Distance / Trips : double.NaN;

    /// <summary>Warning only: the stock is likely to dip under the safety stock (mean - 2 sigma).</summary>
    public bool Warn => StockAvg - 2 * StockStd < Safety;
}

/// <summary>Conveyor lines (code_conv.m) at the recommended counts: how many, products passed and seconds stopped for want of stock, summed over
/// the conveyors, mean over the confirm runs.</summary>
public sealed record ConveyorSummary(int Count, double Passed, double StopSec);

/// <summary>
/// What the result pages show, read from a finished job's folder: the input (for group ranges and judgement settings), runs\summary.tsv, runs\impact.tsv,
/// the confirm runs' result.tsv (block occupancy and waits) and the model's layout (agv.asy + blocks.tsv) for the maps. Cached per job once it has finished.
/// </summary>
public sealed class JobView
{
    static readonly ConcurrentDictionary<string, (DateTime? Finished, JobView View)> Cache = new();

    public ProjectInput? Input { get; private set; }
    public string? InputError { get; private set; }
    public IReadOnlyList<SummaryRow> Rows { get; private set; } = Array.Empty<SummaryRow>();
    public IReadOnlyList<GroupView> Groups { get; private set; } = Array.Empty<GroupView>();
    public IReadOnlyDictionary<string, IReadOnlyList<CountPoint>> Curves { get; private set; } = new Dictionary<string, IReadOnlyList<CountPoint>>();
    public IReadOnlyList<BlockView> Blocks { get; private set; } = Array.Empty<BlockView>();
    /// <summary>Path name -> vehicles that entered it (the AutoMod report's Guide Path Total), mean over the confirm runs; empty without reports.</summary>
    public IReadOnlyDictionary<string, double> PathTraffic { get; private set; } = new Dictionary<string, double>();
    public ASH.Core.Layout.Layout? Layout { get; private set; }
    public IReadOnlyList<VehicleView> Vehicles { get; private set; } = Array.Empty<VehicleView>();
    public IReadOnlyList<GroupVehicleView> VehicleGroups { get; private set; } = Array.Empty<GroupVehicleView>();
    public IReadOnlyList<MaterialView> Materials { get; private set; } = Array.Empty<MaterialView>();
    public IReadOnlyList<ImpactRow> Impact { get; private set; } = Array.Empty<ImpactRow>();
    /// <summary>Null without conveyor rows. shortcut: not kept in stats.tsv, so it is gone once the run folders are purged; keep it there if that matters.</summary>
    public ConveyorSummary? Conveyors { get; private set; }
    public double UtilMax => Input?.Settings.UtilMax ?? 0.75;
    /// <summary>Deadlocks summed over the confirm runs (each run's count is model-wide, repeated per group row).</summary>
    public int Deadlocks { get; private set; }
    /// <summary>Runs and places of the confirm runs' deadlocks; null when none deadlocked.</summary>
    public DeadlockView? Deadlock { get; private set; }
    /// <summary>Materials that ran out in the confirm runs and why; null when none did (or no result.tsv is left).</summary>
    public StockoutView? Stockout { get; private set; }
    /// <summary>Confirm runs whose places are read at most (result.tsv of the ones with the most deadlocks).</summary>
    public const int DeadlockRunCap = 50;

    static readonly Regex DeadlockCount = new(@"교착 \d+건", RegexOptions.Compiled);

    /// <summary>The text of a verdict or warning with the biggest place after each "교착 N건": "교착 12건 · B007·B005 8건(3개 실행) 외". With <paramref name="runId"/> the place is
    /// the biggest of that run alone (no run count). Text without "교착 N건", or a job without places, comes back unchanged. The stored reason is not touched.</summary>
    public string Annotate(string? text, string? runId = null)
    {
        if (string.IsNullOrEmpty(text) || Deadlock is not { Spots.Count: > 0 } d) return text ?? "";
        var spots = runId == null ? d.Spots : d.Spots.Where(s => s.ByRun.ContainsKey(runId)).OrderByDescending(s => s.ByRun[runId]).ToList();
        if (spots.Count == 0) return text;
        var top = spots[0];
        var tail = runId == null ? $"{top.Name} {top.Episodes}건({top.Runs}개 실행)" : $"{top.Name} {top.ByRun[runId]}건";
        if (spots.Count > 1) tail += " 외";
        return DeadlockCount.Replace(text, m => m.Value + " · " + tail);
    }

    /// <summary>Link to the cause tab of the analysis with this run chosen; null for a job outside an analysis.</summary>
    internal static string? CauseUrl(JobInfo j, string runId) =>
        string.IsNullOrEmpty(j.AnalysisId) ? null : $"/analyses/{Uri.EscapeDataString(j.AnalysisId)}?tab=cause&job={Uri.EscapeDataString(j.Id)}&run={Uri.EscapeDataString(runId)}";

    /// <summary>A run that failed only on the utilization limit: shown as a warning. Judge checks utilization last, so its reason means every other rule
    /// passed; summary rows from before that order could hide a supply shortfall, a deadlock is still caught by the column.</summary>
    public static bool UtilOnly(SummaryRow r) => !r.Ok && r.Reason.StartsWith(Judge.UtilPrefix, StringComparison.Ordinal) && r.Deadlocks == 0;
    static bool Failed(SummaryRow r) => !r.Ok && !UtilOnly(r);

    /// <summary>Active count per group of every run, from its summary rows (a group without a row ran at MinCount, as in ModelRunner.RunId).</summary>
    public IReadOnlyDictionary<string, Dictionary<string, int>> RunCounts() =>
        Rows.GroupBy(r => r.RunId).ToDictionary(g => g.Key, g => g.GroupBy(r => r.Group).ToDictionary(x => x.Key, x => x.First().Active, StringComparer.Ordinal), StringComparer.Ordinal);

    /// <summary>What a run ran with (group counts, seed, debug), rebuilt from summary.tsv; null for an unknown run or one whose id does not come out the same.</summary>
    public RunScenario? Scenario(string runId)
    {
        if (Input == null) return null;
        var rows = Rows.Where(r => r.RunId == runId && Input.Groups.Any(g => g.Group == r.Group)).ToList();
        if (rows.Count == 0) return null;
        var s = new RunScenario(rows[0].Seed, rows.GroupBy(r => r.Group).ToDictionary(g => g.Key, g => g.First().Active, StringComparer.Ordinal), runId.Contains("_dbg"), runId.EndsWith("_ev", StringComparison.Ordinal));
        return ModelRunner.RunId(Input, s) == runId ? s : null;
    }

    // The catalog only accepts KRW rows, so summing unit prices across types is safe.
    /// <summary>Sum of decided count x unit price of the group's type; types without a price are listed, not counted. Hidden types still count.</summary>
    public static InvestmentView Investment(ProjectInput input, IReadOnlyDictionary<string, int> decided, IReadOnlyDictionary<string, PriceRow> prices)
    {
        decimal total = 0; var unpriced = new List<string>(); var any = false;
        foreach (var g in input.Groups)
        {
            if (!prices.TryGetValue(g.Type, out var p)) { if (!unpriced.Contains(g.Type)) unpriced.Add(g.Type); continue; }
            any = true;
            total += (decided.TryGetValue(g.Group, out var n) ? n : 0) * p.UnitPrice;
        }
        return new InvestmentView(total, prices.Values.FirstOrDefault()?.Currency ?? "KRW", unpriced, any);
    }

    public static JobView For(JobStore store, JobInfo job)
    {
        if (job.Finished && Cache.TryGetValue(job.Id, out var c) && c.Finished == job.FinishedUtc) return c.View;
        var v = Load(store, job);
        if (job.Finished) Cache[job.Id] = (job.FinishedUtc, v);
        return v;
    }

    static double Mean(IEnumerable<double> xs) { var l = xs.Where(x => !double.IsNaN(x)).ToList(); return l.Count == 0 ? double.NaN : l.Average(); }

    static JobView Load(JobStore store, JobInfo job)
    {
        var v = new JobView();
        var project = store.ProjectDir(job.Id);
        var runs = Path.Combine(project, "runs");
        try { v.Input = SearchJob.ReadInput(project, TextWriter.Null, new List<string>(), out var err); v.InputError = err; }
        catch (Exception e) { v.InputError = e.Message; }
        try { v.Rows = SummaryTsv.ReadAll(Path.Combine(runs, "summary.tsv")); }
        catch (Exception e) when (e is IOException or InvalidDataException) { v.InputError ??= e.Message; }

        var confirm = new HashSet<string>(job.ConfirmRunIds, StringComparer.Ordinal);
        v.Deadlocks = v.Rows.Where(r => confirm.Contains(r.RunId)).GroupBy(r => r.RunId).Sum(g => g.First().Deadlocks);
        var badRun = new Dictionary<string, string>(StringComparer.Ordinal); // group -> the run its Reason comes from
        if (v.Input != null)
        {
            v.Groups = v.Input.Groups.Select(g =>
            {
                var at = v.Rows.Where(r => r.Group == g.Group && confirm.Contains(r.RunId)).ToList();
                int? decided = job.Decided != null && job.Decided.TryGetValue(g.Group, out var d) ? d : null;
                var bad = at.FirstOrDefault(Failed);
                if (bad != null) badRun[g.Group] = bad.RunId;
                return new GroupView(g.Group, g.Type, g.MinCount, g.MaxCount, decided, Mean(at.Select(r => r.Utilization)),
                    at.Count == 0 ? double.NaN : at.Min(r => r.MinStockRatio), at.Count > 0 && !at.Any(Failed), bad?.Reason, at.Any(UtilOnly));
            }).ToList();
            v.Curves = v.Input.Groups.ToDictionary(g => g.Group, g => (IReadOnlyList<CountPoint>)v.Rows.Where(r => r.Group == g.Group).GroupBy(r => r.Active).OrderBy(x => x.Key)
                .Select(x =>
                {
                    var u = x.Select(r => r.Utilization).Where(d => !double.IsNaN(d)).ToList();
                    return new CountPoint(x.Key, u.Count == 0 ? double.NaN : u.Average(), u.Count == 0 ? double.NaN : u.Min(), u.Count == 0 ? double.NaN : u.Max(), x.Count(), x.Count(Failed));
                }).ToList());
        }

        // result.tsv of the confirm runs: block, vehicle and material numbers
        var sets = new List<ResultSet>(); var byRun = new Dictionary<string, ResultSet>(StringComparer.Ordinal);
        foreach (var id in confirm)
        {
            var tsv = Path.Combine(runs, id, "result.tsv");
            if (!File.Exists(tsv)) continue;
            try { var rs = ResultParser.Parse(tsv); sets.Add(rs); byRun[id] = rs; }
            catch (Exception e) when (e is IOException or InvalidDataException) { }
        }
        var conv = sets.Where(rs => rs.Conveyors is { Count: > 0 }).ToList();
        if (conv.Count > 0) v.Conveyors = new ConveyorSummary(conv.Max(rs => rs.Conveyors!.Count), conv.Average(rs => rs.Conveyors!.Sum(c => (double)c.Passed)), conv.Average(rs => rs.Conveyors!.Sum(c => c.StopSec)));
        var acc = new Dictionary<string, List<BlockResult>>(StringComparer.Ordinal);
        foreach (var b in sets.SelectMany(rs => rs.Blocks))
        {
            if (!acc.TryGetValue(b.Name, out var l)) acc[b.Name] = l = new List<BlockResult>();
            l.Add(b);
        }
        // path traffic from the confirm runs' reports (Guide Path Statistics), kept in paths.tsv once the run folders are purged
        var traffic = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var id in confirm)
        {
            var rep = Path.Combine(runs, id, job.Model + ".report");
            if (!File.Exists(rep)) continue;
            try { foreach (var kv in ResultParser.ParseReport(rep).PathTotals ?? new Dictionary<string, int>()) { if (!traffic.TryGetValue(kv.Key, out var tl)) traffic[kv.Key] = tl = new List<double>(); tl.Add(kv.Value); } }
            catch (Exception e) when (e is IOException or InvalidDataException or FormatException) { }
        }
        var savedPaths = Path.Combine(store.JobDir(job.Id), JobRetention.PathsFile);
        if (traffic.Count > 0) v.PathTraffic = traffic.ToDictionary(kv => kv.Key, kv => kv.Value.Average(), StringComparer.Ordinal);
        else if (File.Exists(savedPaths))
            v.PathTraffic = File.ReadAllLines(savedPaths).Skip(1).Select(x => x.Split('\t')).Where(t => t.Length >= 2 && double.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                .ToDictionary(t => t[0], t => double.Parse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture), StringComparer.Ordinal);

        var saved = Path.Combine(store.JobDir(job.Id), JobRetention.BlocksFile);
        if (acc.Count == 0 && File.Exists(saved))
        {
            // the run folders were purged (Jobs:KeepRunsDays): the numbers were kept here first
            static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
            v.Blocks = File.ReadAllLines(saved).Skip(1).Select(l => l.Split('\t')).Where(t => t.Length >= 4)
                .Select(t => new BlockView(t[0], D(t[1]), D(t[2]), D(t[3]))).OrderByDescending(b => b.AvgWaitSec).ToList();
        }
        else v.Blocks = acc.Select(kv => new BlockView(kv.Key, Mean(kv.Value.Select(b => b.Utilization)), Mean(kv.Value.Select(b => b.AvgWaitSec)), Mean(kv.Value.Select(b => b.Passes))))
            .OrderByDescending(b => b.AvgWaitSec).ToList();

        var savedStats = Path.Combine(store.JobDir(job.Id), JobRetention.StatsFile);
        if (sets.Count == 0 && File.Exists(savedStats)) ReadStats(v, savedStats);
        else
        {
            // AutoMod distances are in model units; the pages show metres
            var perM = ASH.Core.Settings.UnitScale.ModelUnitsPerMeter(v.Input?.Settings.Units) ?? 1;
            v.Vehicles = VehicleViews(sets, perM); v.Materials = MaterialViews(sets, v.Input, perM);
        }
        v.VehicleGroups = v.Vehicles.GroupBy(x => x.Group).Select(g =>
        {
            var jobs = g.Sum(x => x.Jobs);
            return new GroupVehicleView(g.Key, g.Count(), Mean(g.Select(x => x.Util)), Mean(g.Select(x => x.Distance)), Mean(g.Select(x => x.Jobs)),
                jobs > 0 ? g.Sum(x => x.Distance) / jobs : double.NaN, jobs > 0 ? g.Sum(x => x.BusySec) / jobs : double.NaN,
                g.All(x => x.Split != null) ? Combine(g.Select(x => x.Split!), 1) : null);
        }).ToList();

        try { v.Layout = ArcLayout.Load(Path.Combine(project, job.Model + ".arc")).Layout; } catch (Exception e) when (e is IOException or FormatException or InvalidDataException) { }
        try { v.Deadlock = DeadlockOf(job, v, byRun); } catch (Exception e) when (e is not OutOfMemoryException) { } // odd data must never take a result page down
        try { v.Stockout = StockoutOf(job, v, byRun); } catch (Exception e) when (e is not OutOfMemoryException) { } // the box is extra: odd data must not take the page down
        // the stored reason stays "교착 N건"; what the pages show carries the biggest place of the run it came from
        if (v.Deadlock != null) v.Groups = v.Groups.Select(g => g with { Reason = g.Reason == null ? null : v.Annotate(g.Reason, badRun.GetValueOrDefault(g.Group)) }).ToList();

        var impact = Path.Combine(runs, "impact.tsv");
        if (File.Exists(impact))
        {
            var list = new List<ImpactRow>();
            foreach (var line in File.ReadAllLines(impact).Skip(1))
            {
                var t = line.TrimEnd('\r').Split('\t');
                if (t.Length < 5) continue;
                if (int.TryParse(t[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var plus)
                    && double.TryParse(t[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var du)
                    && double.TryParse(t[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var ds))
                    list.Add(new ImpactRow(t[0], plus, t[2], du, ds));
            }
            v.Impact = list;
        }
        return v;
    }

    /// <summary>The confirm runs that deadlocked (summary.tsv count above 0, most first) and, from the first <see cref="DeadlockRunCap"/> of them whose result.tsv was read, the places.</summary>
    static DeadlockView? DeadlockOf(JobInfo job, JobView v, IReadOnlyDictionary<string, ResultSet> read)
    {
        var runs = new List<DeadlockRun>();
        var ids = job.ConfirmRunIds.Distinct(StringComparer.Ordinal).ToList();
        for (int i = 0; i < ids.Count; i++)
        {
            var rows = v.Rows.Where(r => r.RunId == ids[i]).ToList();
            int n = rows.Count == 0 ? 0 : rows.Max(r => r.Deadlocks);
            if (n > 0) runs.Add(new DeadlockRun(ids[i], i + 1, n, string.Join(" · ", rows.Select(r => $"{r.Group}={r.Active}")), read.ContainsKey(ids[i])));
        }
        if (runs.Count == 0) return null;
        runs = runs.OrderByDescending(r => r.Count).ThenBy(r => r.Number).ToList();
        var used = runs.Take(DeadlockRunCap).Where(r => read.ContainsKey(r.RunId)).ToList();
        var blocks = v.Layout?.Blocks.Select(b => b.Name).ToHashSet(StringComparer.Ordinal);
        var spots = DeadlockSummary.Build(used.Select(r => new KeyValuePair<string, IReadOnlyList<DeadlockEvent>?>(r.RunId, read[r.RunId].DeadlockEvents)),
            blocks is { Count: > 0 } ? blocks.Contains : null);
        return new DeadlockView(runs, spots, runs.Count > DeadlockRunCap, spots.Count == 0);
    }

    /// <summary>StockoutCauses per confirm run that was read, grouped per material. Without the layout the free-speed lead is unknown (자료 부족), the rest still works.</summary>
    static StockoutView? StockoutOf(JobInfo job, JobView v, IReadOnlyDictionary<string, ResultSet> read)
    {
        if (v.Input == null) return null;
        var ids = job.ConfirmRunIds.Distinct(StringComparer.Ordinal).Where(read.ContainsKey).ToList();
        var number = job.ConfirmRunIds.Distinct(StringComparer.Ordinal).Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1, StringComparer.Ordinal);
        var layout = v.Layout ?? new ASH.Core.Layout.Layout();
        var checks = ids.SelectMany(id => StockoutCauses.Check(new CauseContext(layout, v.Input, read[id])).Select(c => (Run: id, Check: c))).ToList();
        if (checks.Count == 0) return null;
        var rows = checks.GroupBy(x => x.Check.Material, StringComparer.Ordinal).Select(g =>
        {
            var top = g.OrderByDescending(x => x.Check.StopSec).First();
            double sum = g.Sum(x => x.Check.StopSec);
            return new StockoutRow(g.Key, top.Check.Group, sum, sum / ids.Count, g.Count(), top.Run, number[top.Run], top.Check);
        }).OrderByDescending(x => x.StopSum).ToList();
        return new StockoutView(rows, ids.Count);
    }

    /// <summary>Part by part: the sum of the splits times <paramref name="scale"/> (1 = sum, 1/n = mean).</summary>
    internal static TimeSplit Combine(IEnumerable<TimeSplit> xs, double scale)
    {
        var l = xs.ToList();
        double S(Func<TimeSplit, double> f) => l.Sum(f) * scale;
        return new TimeSplit(S(x => x.Window), S(x => x.Busy), S(x => x.Loaded), S(x => x.Handling), S(x => x.BufferBusy), S(x => x.BufferIdle), S(x => x.Repair));
    }

    static double Max(IEnumerable<double> xs) { var l = xs.Where(x => !double.IsNaN(x)).ToList(); return l.Count == 0 ? double.NaN : l.Max(); }
    static double Min(IEnumerable<double> xs) { var l = xs.Where(x => !double.IsNaN(x)).ToList(); return l.Count == 0 ? double.NaN : l.Min(); }

    /// <summary>The model writes a row for every vehicle up to MaxCount: only those with Index &lt;= the run's active count of the group count.
    /// Downtime is value6 of the row, read defensively (older models do not write it).</summary>
    static List<VehicleView> VehicleViews(List<ResultSet> sets, double perM)
    {
        var per = new List<(string Name, string Group, double Busy, double Window, double Dist, double Jobs, double Down, TimeSplit? Split)>();
        foreach (var rs in sets)
        {
            if (rs.Vehicles == null) continue;
            var active = rs.Groups.ToDictionary(g => g.Group, g => g.ActiveCount, StringComparer.Ordinal);
            var raw = rs.Rows.Where(r => r.Kind == "vehicle").GroupBy(r => (r.Name, r.Group)).ToDictionary(g => g.Key, g => g.First());
            foreach (var x in rs.Vehicles)
            {
                if (!active.TryGetValue(x.Group, out var n) || x.Index > n) continue;
                var down = raw.TryGetValue((x.Name, x.Group), out var row) && row.Values.Length > 5 ? row.Values[5] : double.NaN;
                per.Add((x.Name, x.Group, x.BusySec, x.WindowSec, x.Distance / perM, x.Jobs, down, x.Split));
            }
        }
        return per.GroupBy(x => (x.Name, x.Group)).Select(g => new VehicleView(g.Key.Name, g.Key.Group,
            Mean(g.Select(x => x.Window > 0 ? x.Busy / x.Window : double.NaN)), Mean(g.Select(x => x.Busy)), Mean(g.Select(x => x.Dist)), Mean(g.Select(x => x.Jobs)),
            Mean(g.Select(x => x.Down)), Split(g.Select(x => x.Split)))).ToList();
    }

    /// <summary>Mean over the runs that have a split; null when none has.</summary>
    static TimeSplit? Split(IEnumerable<TimeSplit?> xs)
    {
        var l = xs.OfType<TimeSplit>().ToList();
        return l.Count == 0 ? null : Combine(l, 1.0 / l.Count);
    }

    /// <summary>Value7/value8 of a material row are the supply jobs' distance and count; older models write 0 there, read as no data
    /// (a material that was supplied has always driven some way).</summary>
    static List<MaterialView> MaterialViews(List<ResultSet> sets, ProjectInput? input, double perM)
    {
        var stats = sets.SelectMany(rs => rs.MaterialStats ?? Array.Empty<MaterialStat>()).ToList();
        return stats.Select(m => m.Name).Distinct(StringComparer.Ordinal).Select(name =>
        {
            var st = stats.Where(m => m.Name == name).ToList();
            var ma = sets.SelectMany(rs => rs.Materials).Where(m => m.Name == name).ToList();
            var jobs = sets.SelectMany(rs => rs.Rows).Where(r => r.Kind == "material" && r.Name == name && r.Values.Length > 7 && r.Values[7] > 0).ToList();
            var safety = input == null ? double.NaN : input.Materials.FirstOrDefault(m => m.Material == name)?.SafetyStock ?? input.Settings.SafetyStock;
            return new MaterialView(name, st[0].Group, Mean(ma.Select(m => (double)m.Supplies)), Mean(ma.Select(m => m.Expected)), Mean(ma.Select(m => m.StopTimeSec)),
                Mean(st.Select(m => m.PickAvg)), Max(st.Select(m => m.PickMax)), Mean(st.Select(m => m.DelivAvg)), Max(st.Select(m => m.DelivMax)),
                Mean(st.Select(m => m.StockAvg)), Mean(st.Select(m => m.StockStd)), Min(st.Select(m => m.StockMin)), Max(st.Select(m => m.StockMax)), safety,
                Mean(jobs.Select(r => r.Values[7])), Mean(jobs.Select(r => r.Values[6] / perM)));
        }).ToList();
    }

    /// <summary>The lines of stats.tsv as JobRetention writes them: "vehicle" and "material" rows after a header line.</summary>
    internal static string[] StatsLines(JobView v)
    {
        static string R(double d) => double.IsNaN(d) ? "" : d.ToString("R", CultureInfo.InvariantCulture);
        return new[] { "kind\tname\tgroup\tvalues" }
            .Concat(v.Vehicles.Select(x => string.Join("\t", new[] { "vehicle", x.Name, x.Group, R(x.Util), R(x.BusySec), R(x.Distance), R(x.Jobs), R(x.Down) }
                .Concat(x.Split is { } p ? new[] { p.Window, p.Busy, p.Loaded, p.Handling, p.BufferBusy, p.BufferIdle, p.Repair }.Select(R) : Array.Empty<string>()))))
            .Concat(v.Materials.Select(m => string.Join("\t", "material", m.Name, m.Group, R(m.Supplies), R(m.Expected), R(m.StopSec), R(m.PickAvg), R(m.PickMax),
                R(m.DelivAvg), R(m.DelivMax), R(m.StockAvg), R(m.StockStd), R(m.StockMin), R(m.StockMax), R(m.Safety), R(m.Trips), R(m.Distance)))).ToArray();
    }

    static void ReadStats(JobView v, string path)
    {
        static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        var veh = new List<VehicleView>(); var mat = new List<MaterialView>();
        foreach (var t in File.ReadAllLines(path).Skip(1).Select(l => l.TrimEnd('\r').Split('\t')))
        {
            if (t[0] == "vehicle" && t.Length >= 8) veh.Add(new VehicleView(t[1], t[2], D(t[3]), D(t[4]), D(t[5]), D(t[6]), D(t[7]),
                t.Length >= 15 ? new TimeSplit(D(t[8]), D(t[9]), D(t[10]), D(t[11]), D(t[12]), D(t[13]), D(t[14])) : null)); // stats.tsv from before the split: none
            else if (t[0] == "material" && t.Length >= 15)
                mat.Add(new MaterialView(t[1], t[2], D(t[3]), D(t[4]), D(t[5]), D(t[6]), D(t[7]), D(t[8]), D(t[9]), D(t[10]), D(t[11]), D(t[12]), D(t[13]), D(t[14]),
                    t.Length > 16 ? D(t[15]) : double.NaN, t.Length > 16 ? D(t[16]) : double.NaN)); // stats.tsv from before the supply columns: no data
        }
        v.Vehicles = veh; v.Materials = mat;
    }
}
