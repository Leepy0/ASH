using System.Globalization;

namespace ASH.Server;

/// <summary>
/// Frees disk: Jobs:KeepRunsDays (default 30, 0 = keep forever) after a job finished, its raw run folders (runs\&lt;RunId&gt;\, AutoMod output), the build
/// output (&lt;Model&gt;.dir, &lt;Model&gt;.exe), the work slots and runs.zip are deleted. What the result pages need stays: job.json, job.log, upload.zip, the
/// model folder, runs\summary.tsv, impact.tsv, search.tsv, and the block numbers and vehicle/material statistics, which are saved to blocks.tsv and stats.tsv first. Checked hourly.
/// </summary>
public sealed class JobRetention(JobStore store, IConfiguration cfg, ILogger<JobRetention> log) : BackgroundService
{
    public const string BlocksFile = "blocks.tsv";
    public const string StatsFile = "stats.tsv";

    int KeepDays => Math.Max(0, cfg.GetValue("Jobs:KeepRunsDays", 30));

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            try { Sweep(DateTime.UtcNow); }
            catch (Exception e) { log.LogWarning(e, "작업 정리 실패"); }
            try { await Task.Delay(TimeSpan.FromHours(1), stop); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Purges every finished job older than the limit; returns how many were purged.</summary>
    public int Sweep(DateTime nowUtc)
    {
        if (KeepDays == 0) return 0;
        int n = 0;
        foreach (var j in store.All())
        {
            if (!j.Finished || j.RunsPurgedUtc != null || j.FinishedUtc is not { } f || nowUtc - f < TimeSpan.FromDays(KeepDays)) continue;
            Purge(j);
            n++;
        }
        return n;
    }

    void Purge(JobInfo j)
    {
        var project = store.ProjectDir(j.Id);
        var runs = Path.Combine(project, "runs");
        // the block maps read the confirm runs' result.tsv: keep their numbers before the folders go
        var view = JobView.For(store, j);
        if (view.Blocks.Count > 0)
        {
            var inv = CultureInfo.InvariantCulture;
            File.WriteAllLines(Path.Combine(store.JobDir(j.Id), BlocksFile),
                new[] { "name\tutilization\tavgWaitSec\tpasses" }.Concat(view.Blocks.Select(b =>
                    string.Join("\t", b.Name, b.Utilization.ToString("R", inv), b.AvgWaitSec.ToString("R", inv), b.Passes.ToString("R", inv)))));
        }
        if (view.Vehicles.Count > 0 || view.Materials.Count > 0) File.WriteAllLines(Path.Combine(store.JobDir(j.Id), StatsFile), JobView.StatsLines(view));
        long freed = 0;
        void Del(string path)
        {
            try
            {
                if (Directory.Exists(path)) { freed += new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); Directory.Delete(path, true); }
                else if (File.Exists(path)) { freed += new FileInfo(path).Length; File.Delete(path); }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log.LogWarning("지우지 못함 {Path}: {Error}", path, e.Message); }
        }
        if (Directory.Exists(runs)) foreach (var d in Directory.GetDirectories(runs)) Del(d);
        Del(Path.Combine(project, "work"));
        Del(Path.Combine(project, j.Model + ".dir"));
        Del(Path.Combine(project, j.Model + ".exe"));
        Del(Path.Combine(store.JobDir(j.Id), "runs.zip"));
        store.Update(j.Id, x => x.RunsPurgedUtc = DateTime.UtcNow);
        log.LogInformation("작업 {Id}: 실행 원본 정리, {MB:0.#} MB", j.Id, freed / 1048576.0);
    }
}
