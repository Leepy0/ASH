using System.Collections.Concurrent;
using System.Globalization;
using ASH.Core.Input;
using ASH.Core.Runner;

namespace ASH.Server;

/// <summary>Per group at the recommended counts: mean over the confirm runs.</summary>
public sealed record GroupView(string Group, string Type, int Min, int Max, int? Decided, double Util, double MinStockRatio, bool AllOk, string? Reason);

/// <summary>Per count of one group (all runs where the group had that many active): mean, min and max utilization, and whether any of them failed.</summary>
public sealed record CountPoint(int Active, double Mean, double Lo, double Hi, int Runs, int Failed);

/// <summary>Traffic block numbers at the recommended counts (mean over the confirm runs that have them).</summary>
public sealed record BlockView(string Name, double Utilization, double AvgWaitSec, double Passes);

/// <summary>One active vehicle, mean over the confirm runs it was active in. Down = breakdown seconds, NaN when the model does not write it.</summary>
public sealed record VehicleView(string Name, string Group, double Util, double BusySec, double Distance, double Jobs, double Down)
{
    public double DistPerJob => Jobs > 0 ? Distance / Jobs : double.NaN;
    public double BusyPerJob => Jobs > 0 ? BusySec / Jobs : double.NaN;
}

/// <summary>Per group, from its active vehicles: mean utilisation, mean distance and jobs per vehicle, distance and busy seconds per job.</summary>
public sealed record GroupVehicleView(string Group, int Count, double Util, double Distance, double Jobs, double DistPerJob, double BusyPerJob);

/// <summary>Per material: lead times (call -> load CP, call -> unload done) and stock statistics, plus supplies and stop time. Mean over the confirm runs,
/// except the Max/Min columns (extreme over the runs). Safety is NaN when the input is unreadable.</summary>
public sealed record MaterialView(string Name, string Group, double Supplies, double Expected, double StopSec, double PickAvg, double PickMax,
    double DelivAvg, double DelivMax, double StockAvg, double StockStd, double StockMin, double StockMax, double Safety)
{
    /// <summary>Warning only: the stock is likely to dip under the safety stock (mean - 2 sigma).</summary>
    public bool Warn => StockAvg - 2 * StockStd < Safety;
}

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
    public ASH.Core.Layout.Layout? Layout { get; private set; }
    public IReadOnlyList<VehicleView> Vehicles { get; private set; } = Array.Empty<VehicleView>();
    public IReadOnlyList<GroupVehicleView> VehicleGroups { get; private set; } = Array.Empty<GroupVehicleView>();
    public IReadOnlyList<MaterialView> Materials { get; private set; } = Array.Empty<MaterialView>();
    public IReadOnlyList<ImpactRow> Impact { get; private set; } = Array.Empty<ImpactRow>();
    public double UtilMax => Input?.Settings.UtilMax ?? 0.75;

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
        if (v.Input != null)
        {
            v.Groups = v.Input.Groups.Select(g =>
            {
                var at = v.Rows.Where(r => r.Group == g.Group && confirm.Contains(r.RunId)).ToList();
                int? decided = job.Decided != null && job.Decided.TryGetValue(g.Group, out var d) ? d : null;
                var bad = at.FirstOrDefault(r => !r.Ok);
                return new GroupView(g.Group, g.Type, g.MinCount, g.MaxCount, decided, Mean(at.Select(r => r.Utilization)),
                    at.Count == 0 ? double.NaN : at.Min(r => r.MinStockRatio), at.Count > 0 && at.All(r => r.Ok), bad?.Reason);
            }).ToList();
            v.Curves = v.Input.Groups.ToDictionary(g => g.Group, g => (IReadOnlyList<CountPoint>)v.Rows.Where(r => r.Group == g.Group).GroupBy(r => r.Active).OrderBy(x => x.Key)
                .Select(x =>
                {
                    var u = x.Select(r => r.Utilization).Where(d => !double.IsNaN(d)).ToList();
                    return new CountPoint(x.Key, u.Count == 0 ? double.NaN : u.Average(), u.Count == 0 ? double.NaN : u.Min(), u.Count == 0 ? double.NaN : u.Max(), x.Count(), x.Count(r => !r.Ok));
                }).ToList());
        }

        // result.tsv of the confirm runs: block, vehicle and material numbers
        var sets = new List<ResultSet>();
        foreach (var id in confirm)
        {
            var tsv = Path.Combine(runs, id, "result.tsv");
            if (!File.Exists(tsv)) continue;
            try { sets.Add(ResultParser.Parse(tsv)); }
            catch (Exception e) when (e is IOException or InvalidDataException) { }
        }
        var acc = new Dictionary<string, List<BlockResult>>(StringComparer.Ordinal);
        foreach (var b in sets.SelectMany(rs => rs.Blocks))
        {
            if (!acc.TryGetValue(b.Name, out var l)) acc[b.Name] = l = new List<BlockResult>();
            l.Add(b);
        }
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
        else { v.Vehicles = VehicleViews(sets); v.Materials = MaterialViews(sets, v.Input); }
        v.VehicleGroups = v.Vehicles.GroupBy(x => x.Group).Select(g =>
        {
            var jobs = g.Sum(x => x.Jobs);
            return new GroupVehicleView(g.Key, g.Count(), Mean(g.Select(x => x.Util)), Mean(g.Select(x => x.Distance)), Mean(g.Select(x => x.Jobs)),
                jobs > 0 ? g.Sum(x => x.Distance) / jobs : double.NaN, jobs > 0 ? g.Sum(x => x.BusySec) / jobs : double.NaN);
        }).ToList();

        try { v.Layout = ArcLayout.Load(Path.Combine(project, job.Model + ".arc")).Layout; } catch (Exception e) when (e is IOException or FormatException or InvalidDataException) { }

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

    static double Max(IEnumerable<double> xs) { var l = xs.Where(x => !double.IsNaN(x)).ToList(); return l.Count == 0 ? double.NaN : l.Max(); }
    static double Min(IEnumerable<double> xs) { var l = xs.Where(x => !double.IsNaN(x)).ToList(); return l.Count == 0 ? double.NaN : l.Min(); }

    /// <summary>The model writes a row for every vehicle up to MaxCount: only those with Index &lt;= the run's active count of the group count.
    /// Downtime is value6 of the row, read defensively (older models do not write it).</summary>
    static List<VehicleView> VehicleViews(List<ResultSet> sets)
    {
        var per = new List<(string Name, string Group, double Busy, double Window, double Dist, double Jobs, double Down)>();
        foreach (var rs in sets)
        {
            if (rs.Vehicles == null) continue;
            var active = rs.Groups.ToDictionary(g => g.Group, g => g.ActiveCount, StringComparer.Ordinal);
            var raw = rs.Rows.Where(r => r.Kind == "vehicle").GroupBy(r => (r.Name, r.Group)).ToDictionary(g => g.Key, g => g.First());
            foreach (var x in rs.Vehicles)
            {
                if (!active.TryGetValue(x.Group, out var n) || x.Index > n) continue;
                var down = raw.TryGetValue((x.Name, x.Group), out var row) && row.Values.Length > 5 ? row.Values[5] : double.NaN;
                per.Add((x.Name, x.Group, x.BusySec, x.WindowSec, x.Distance, x.Jobs, down));
            }
        }
        return per.GroupBy(x => (x.Name, x.Group)).Select(g => new VehicleView(g.Key.Name, g.Key.Group,
            Mean(g.Select(x => x.Window > 0 ? x.Busy / x.Window : double.NaN)), Mean(g.Select(x => x.Busy)), Mean(g.Select(x => x.Dist)), Mean(g.Select(x => x.Jobs)),
            Mean(g.Select(x => x.Down)))).ToList();
    }

    static List<MaterialView> MaterialViews(List<ResultSet> sets, ProjectInput? input)
    {
        var stats = sets.SelectMany(rs => rs.MaterialStats ?? Array.Empty<MaterialStat>()).ToList();
        return stats.Select(m => m.Name).Distinct(StringComparer.Ordinal).Select(name =>
        {
            var st = stats.Where(m => m.Name == name).ToList();
            var ma = sets.SelectMany(rs => rs.Materials).Where(m => m.Name == name).ToList();
            var safety = input == null ? double.NaN : input.Materials.FirstOrDefault(m => m.Material == name)?.SafetyStock ?? input.Settings.SafetyStock;
            return new MaterialView(name, st[0].Group, Mean(ma.Select(m => (double)m.Supplies)), Mean(ma.Select(m => m.Expected)), Mean(ma.Select(m => m.StopTimeSec)),
                Mean(st.Select(m => m.PickAvg)), Max(st.Select(m => m.PickMax)), Mean(st.Select(m => m.DelivAvg)), Max(st.Select(m => m.DelivMax)),
                Mean(st.Select(m => m.StockAvg)), Mean(st.Select(m => m.StockStd)), Min(st.Select(m => m.StockMin)), Max(st.Select(m => m.StockMax)), safety);
        }).ToList();
    }

    /// <summary>The lines of stats.tsv as JobRetention writes them: "vehicle" and "material" rows after a header line.</summary>
    internal static string[] StatsLines(JobView v)
    {
        static string R(double d) => double.IsNaN(d) ? "" : d.ToString("R", CultureInfo.InvariantCulture);
        return new[] { "kind\tname\tgroup\tvalues" }
            .Concat(v.Vehicles.Select(x => string.Join("\t", "vehicle", x.Name, x.Group, R(x.Util), R(x.BusySec), R(x.Distance), R(x.Jobs), R(x.Down))))
            .Concat(v.Materials.Select(m => string.Join("\t", "material", m.Name, m.Group, R(m.Supplies), R(m.Expected), R(m.StopSec), R(m.PickAvg), R(m.PickMax),
                R(m.DelivAvg), R(m.DelivMax), R(m.StockAvg), R(m.StockStd), R(m.StockMin), R(m.StockMax), R(m.Safety)))).ToArray();
    }

    static void ReadStats(JobView v, string path)
    {
        static double D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
        var veh = new List<VehicleView>(); var mat = new List<MaterialView>();
        foreach (var t in File.ReadAllLines(path).Skip(1).Select(l => l.TrimEnd('\r').Split('\t')))
        {
            if (t[0] == "vehicle" && t.Length >= 8) veh.Add(new VehicleView(t[1], t[2], D(t[3]), D(t[4]), D(t[5]), D(t[6]), D(t[7])));
            else if (t[0] == "material" && t.Length >= 15)
                mat.Add(new MaterialView(t[1], t[2], D(t[3]), D(t[4]), D(t[5]), D(t[6]), D(t[7]), D(t[8]), D(t[9]), D(t[10]), D(t[11]), D(t[12]), D(t[13]), D(t[14])));
        }
        v.Vehicles = veh; v.Materials = mat;
    }
}
