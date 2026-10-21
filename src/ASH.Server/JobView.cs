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

        // block numbers of the confirm runs
        var acc = new Dictionary<string, List<BlockResult>>(StringComparer.Ordinal);
        foreach (var id in confirm)
        {
            var tsv = Path.Combine(runs, id, "result.tsv");
            if (!File.Exists(tsv)) continue;
            try
            {
                foreach (var b in ResultParser.Parse(tsv).Blocks)
                {
                    if (!acc.TryGetValue(b.Name, out var l)) acc[b.Name] = l = new List<BlockResult>();
                    l.Add(b);
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException) { }
        }
        v.Blocks = acc.Select(kv => new BlockView(kv.Key, Mean(kv.Value.Select(b => b.Utilization)), Mean(kv.Value.Select(b => b.AvgWaitSec)), Mean(kv.Value.Select(b => b.Passes))))
            .OrderByDescending(b => b.AvgWaitSec).ToList();

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
}
