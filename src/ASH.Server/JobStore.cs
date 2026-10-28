using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ASH.Core.Runner;

namespace ASH.Server;

public static class JobState
{
    public const string Queued = "queued", Running = "running", Done = "done", Failed = "failed", Cancelled = "cancelled";
    public static string Korean(string s) => s switch
    {
        Queued => "대기", Running => "실행 중", Done => "완료", Failed => "실패", Cancelled => "취소됨", _ => s,
    };
}

public static class JobSource
{
    /// <summary>Server = built and searched here; Local = searched by ash-run on the user's PC and uploaded with its results.</summary>
    public const string Server = "server", Local = "local";
}

/// <summary>One job: who sent which model, where it is in the queue, and what came out. Saved as job.json in the job's folder.</summary>
public sealed class JobInfo
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = JobSource.Server;
    /// <summary>When the raw run folders were deleted (Jobs:KeepRunsDays); summaries, the upload and the log stay.</summary>
    public DateTime? RunsPurgedUtc { get; set; }
    public string Project { get; set; } = "";
    public string Plan { get; set; } = "";
    public string Model { get; set; } = "";
    public string Submitter { get; set; } = "";
    public string Ip { get; set; } = "";
    public string State { get; set; } = JobState.Queued;
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public bool Impact { get; set; } = true;
    /// <summary>Confirmation only (대수 바꿔 다시 돌리기): no search, run exactly these counts of every group. Null = the normal search.</summary>
    public Dictionary<string, int>? FixedCounts { get; set; }
    public int InitialReps { get; set; } = 1;
    public bool CancelRequested { get; set; }
    /// <summary>Queued again by 이어서 하기: the search keeps runs\ and takes the runs that finished before instead of running them again.</summary>
    public bool Resume { get; set; }
    /// <summary>How many times this job was queued again after it failed or was cancelled.</summary>
    public int Retries { get; set; }
    public int RunsDone { get; set; }
    public int RunsFailed { get; set; }
    /// <summary>SearchEstimate.Expected from the search start (raised once the runs pass it); null for old jobs and local uploads.</summary>
    public int? ExpectedRuns { get; set; }
    /// <summary>Counts of the clusters decided so far, while the search runs.</summary>
    public Dictionary<string, int>? Partial { get; set; }
    /// <summary>Sum of the finished runs' durations, in seconds.</summary>
    public double RunSeconds { get; set; }
    public string? Phase { get; set; }
    public string? LastLine { get; set; }
    public string? Error { get; set; }
    public int ExitCode { get; set; }
    public Dictionary<string, int>? Decided { get; set; }
    public List<List<string>>? Clusters { get; set; }
    public List<string> ConfirmRunIds { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> FailureMessages { get; set; } = new();
    public List<PackageFile> Files { get; set; } = new();
    public long UploadBytes { get; set; }
    /// <summary>Which analysis and plan (안) this job is filed under; null until filed. Version 0 = unclassified, else 1.. within the plan.</summary>
    public string? AnalysisId { get; set; }
    public string? PlanId { get; set; }
    public int Version { get; set; }
    public string VersionNote { get; set; } = "";
    public bool Selected { get; set; }
    /// <summary>Put away by an admin: left out of the 작업 대기열 list (its data, analysis and archive entries stay).</summary>
    public bool Archived { get; set; }

    [JsonIgnore] public bool Finished => State is JobState.Done or JobState.Failed or JobState.Cancelled;
    [JsonIgnore] public TimeSpan? Duration => StartedUtc is { } s ? (FinishedUtc ?? DateTime.UtcNow) - s : null;
}

/// <summary>
/// The job folders under Jobs:Dir: &lt;id&gt;\job.json, &lt;id&gt;\upload.zip (as sent), &lt;id&gt;\project\ (unpacked; the model is built and run here),
/// &lt;id&gt;\job.log. All jobs are kept in memory; every change is written to job.json at once, so a restart loses nothing but the run in flight.
/// </summary>
public sealed class JobStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    readonly Dictionary<string, JobInfo> _jobs = new(StringComparer.Ordinal);
    readonly object _gate = new();
    readonly ILogger _log;
    readonly SemaphoreSlim _signal = new(0);
    public string Dir { get; }

    public JobStore(string dir, ILogger log)
    {
        Dir = dir; _log = log;
        Directory.CreateDirectory(dir);
        foreach (var d in Directory.GetDirectories(dir))
        {
            var p = Path.Combine(d, "job.json");
            if (!File.Exists(p)) continue;
            try
            {
                var j = JsonSerializer.Deserialize<JobInfo>(File.ReadAllText(p), Json);
                if (j == null || j.Id != Path.GetFileName(d)) continue;
                // cut off by a stop the worker could not record (killed): queued again to continue from its finished runs, as the worker does on a clean stop
                // shortcut: no retry limit, so a job that crashes the whole server would loop; add one if that ever happens
                if (j.State == JobState.Running)
                {
                    if (j.Source == JobSource.Server) ResetForResume(j);
                    else { j.State = JobState.Failed; j.Error = "서버가 다시 시작되어 중단되었습니다. 다시 보내 주세요."; j.FinishedUtc = DateTime.UtcNow; }
                    Write(j);
                }
                _jobs[j.Id] = j;
            }
            catch (Exception e) when (e is JsonException or IOException) { log.LogWarning("작업 정보를 읽지 못했습니다: {Path}: {Error}", p, e.Message); }
        }
    }

    public static JobStore Create(IConfiguration cfg, ILogger log) => new(DirFrom(cfg), log);

    /// <summary>Jobs:Dir with environment variables expanded (the film helper reads the same folder).</summary>
    public static string DirFrom(IConfiguration cfg)
    {
        var dir = cfg["Jobs:Dir"];
        if (string.IsNullOrWhiteSpace(dir)) dir = "%ProgramData%/ASH.Server/jobs";
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir));
    }

    public string JobDir(string id) => Path.Combine(Dir, id);
    public string ProjectDir(string id) => Path.Combine(Dir, id, "project");
    public string LogPath(string id) => Path.Combine(Dir, id, "job.log");
    public string UploadPath(string id) => Path.Combine(Dir, id, "upload.zip");

    /// <summary>yyyyMMdd-HHmmss-xxxx in Seoul time: sorts by time and reads as a date.</summary>
    public static string NewId() => ASH.Core.SeoulTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..4];

    public static bool IsValidId(string id) => id.Length is > 10 and < 40 && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    void Write(JobInfo j)
    {
        var p = Path.Combine(JobDir(j.Id), "job.json");
        Directory.CreateDirectory(JobDir(j.Id));
        var tmp = p + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(j, Json));
        File.Move(tmp, p, true);
    }

    public void Add(JobInfo j)
    {
        lock (_gate) { _jobs[j.Id] = j; Write(j); }
        _signal.Release();
    }

    /// <summary>Changes a job under the lock and saves it. Returns false when there is no such job.</summary>
    public bool Update(string id, Action<JobInfo> change)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var j)) return false;
            change(j);
            try { Write(j); } catch (IOException e) { _log.LogWarning("job.json 저장 실패 {Id}: {Error}", id, e.Message); }
            return true;
        }
    }

    /// <summary>Removes a finished job and its folder; false when it is missing or still queued or running (cancel it first).</summary>
    public bool Delete(string id)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var j) || !j.Finished) return false;
            _jobs.Remove(id);
        }
        try { Directory.Delete(JobDir(id), true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogWarning("작업 폴더를 다 지우지 못했습니다 {Id}: {Error}", id, e.Message); }
        return true;
    }

    /// <summary>A copy, so a page can render it while the worker changes the original.</summary>
    public JobInfo? Get(string id)
    {
        lock (_gate) return _jobs.TryGetValue(id, out var j) ? Clone(j) : null;
    }

    static JobInfo Clone(JobInfo j) => JsonSerializer.Deserialize<JobInfo>(JsonSerializer.Serialize(j, Json), Json)!;

    /// <summary>Newest first.</summary>
    public IReadOnlyList<JobInfo> All()
    {
        lock (_gate) return _jobs.Values.OrderByDescending(j => j.CreatedUtc).ThenByDescending(j => j.Id, StringComparer.Ordinal).Select(Clone).ToList();
    }

    /// <summary>Oldest queued job, now marked running; null when none waits.</summary>
    public JobInfo? TakeNext()
    {
        lock (_gate)
        {
            var j = _jobs.Values.Where(x => x.State == JobState.Queued).OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (j == null) return null;
            j.State = JobState.Running; j.StartedUtc = DateTime.UtcNow;
            Write(j);
            return Clone(j);
        }
    }

    /// <summary>Wakes the worker for other queued work (a film).</summary>
    public void Wake() => _signal.Release();

    /// <summary>A failed or cancelled server job back in the queue as the same job (same version): <paramref name="resume"/> = continue from the runs
    /// it finished, else start over (runs\ is rotated away as for a new job). False when it is missing, not failed/cancelled, or a PC result.</summary>
    public bool Requeue(string id, bool resume)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var j) || j.State is not (JobState.Failed or JobState.Cancelled) || j.Source != JobSource.Server) return false;
            Reset(j, resume); j.Retries++;
            j.CreatedUtc = DateTime.UtcNow; // its place in the queue: behind what waits now
            try { Write(j); } catch (IOException e) { _log.LogWarning("job.json 저장 실패 {Id}: {Error}", id, e.Message); }
        }
        _signal.Release();
        return true;
    }

    /// <summary>A server job cut off by a server stop (update, restart): queued again at its old place (first) to continue from the runs it finished.</summary>
    public void RequeueCut(string id) => Update(id, ResetForResume);

    static void ResetForResume(JobInfo j) => Reset(j, true);

    static void Reset(JobInfo j, bool resume)
    {
        j.State = JobState.Queued; j.Resume = resume;
        j.StartedUtc = j.FinishedUtc = null; j.CancelRequested = false;
        j.RunsDone = j.RunsFailed = 0; j.RunSeconds = 0; j.ExpectedRuns = null; j.Partial = null;
        j.Phase = null; j.LastLine = null; j.Error = null; j.ExitCode = 0; j.Decided = null; j.Clusters = null;
        j.ConfirmRunIds = new(); j.Warnings = new(); j.FailureMessages = new();
    }

    /// <summary>Wakes the worker (a job was added) or times out.</summary>
    public Task WaitForWorkAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);

    /// <summary>Queue position (1 = next) and a start estimate from the average length of the last finished jobs; null estimate without history.</summary>
    public (int Ahead, DateTime? EstimatedStartUtc, TimeSpan? Typical) QueueInfo(string? id = null)
    {
        lock (_gate)
        {
            var queued = _jobs.Values.Where(x => x.State == JobState.Queued).OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            var running = _jobs.Values.FirstOrDefault(x => x.State == JobState.Running);
            int ahead = id == null ? queued.Count : queued.FindIndex(x => x.Id == id);
            if (ahead < 0) ahead = 0;
            var done = _jobs.Values.Where(x => x.State == JobState.Done && x.StartedUtc != null && x.FinishedUtc != null)
                .OrderByDescending(x => x.FinishedUtc).Take(10).Select(x => (x.FinishedUtc!.Value - x.StartedUtc!.Value).TotalMinutes).ToList();
            if (done.Count == 0) return (ahead + (running != null ? 1 : 0), null, null);
            var typical = TimeSpan.FromMinutes(done.Average());
            var now = DateTime.UtcNow;
            var left = running?.StartedUtc is { } st ? typical - (now - st) : TimeSpan.Zero;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            return (ahead + (running != null ? 1 : 0), now + left + TimeSpan.FromTicks(typical.Ticks * ahead), typical);
        }
    }
}
