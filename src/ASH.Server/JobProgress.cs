namespace ASH.Server;

/// <summary>Progress and finish estimate of a job from RunsDone, ExpectedRuns and the finished runs' time.</summary>
public static class JobProgress
{
    public static double? Ratio(JobInfo j) =>
        j.State == JobState.Done ? 1.0
        : j.ExpectedRuns is null or <= 0 ? null
        : j.State == JobState.Queued ? 0.0
        : Math.Min(j.RunsDone / (double)j.ExpectedRuns, 0.99);

    public static TimeSpan? AvgRun(JobInfo j) => j.RunsDone > 0 ? TimeSpan.FromSeconds(j.RunSeconds / j.RunsDone) : null;

    public static DateTime? Eta(JobInfo j, TimeSpan? typical, int parallel, DateTime nowUtc)
    {
        if (j.State != JobState.Running) return null;
        if (AvgRun(j) is { } avg && j.ExpectedRuns is { } e)
            return nowUtc + TimeSpan.FromTicks((long)(Math.Max(0, e - j.RunsDone) * avg.Ticks / Math.Max(1, parallel)));
        return j.RunsDone == 0 && j.StartedUtc is { } s && typical is { } t ? s + t : null;
    }

    public static int Parallel(IConfiguration cfg) => Math.Max(1, cfg.GetValue("Jobs:Parallel", 2));
}
