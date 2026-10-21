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

/// <summary>One job: who sent which model, where it is in the queue, and what came out. Saved as job.json in the job's folder.</summary>
public sealed class JobInfo
{
    public string Id { get; set; } = "";
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
    public int InitialReps { get; set; } = 1;
    public bool CancelRequested { get; set; }
    public int RunsDone { get; set; }
    public int RunsFailed { get; set; }
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
                // the run in flight when the server stopped cannot be picked up again: its model processes are gone
                if (j.State == JobState.Running) { j.State = JobState.Failed; j.Error = "서버가 다시 시작되어 중단되었습니다. 다시 보내 주세요."; j.FinishedUtc = DateTime.UtcNow; Write(j); }
                _jobs[j.Id] = j;
            }
            catch (Exception e) when (e is JsonException or IOException) { log.LogWarning("작업 정보를 읽지 못했습니다: {Path}: {Error}", p, e.Message); }
        }
    }

    public static JobStore Create(IConfiguration cfg, ILogger log)
    {
        var dir = cfg["Jobs:Dir"];
        if (string.IsNullOrWhiteSpace(dir)) dir = "%ProgramData%/ASH.Server/jobs";
        return new JobStore(Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir)), log);
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
