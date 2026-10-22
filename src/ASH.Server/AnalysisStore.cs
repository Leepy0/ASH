using System.Text.Json;
using ASH.Core.Analysis;

namespace ASH.Server;

/// <summary>
/// Analyses and their plans, saved as analyses.json next to the job folders. Jobs point at them by AnalysisId/PlanId; a job's Version is
/// 1 + the highest in its plan, handed out under one lock so parallel sends never share a number.
/// </summary>
public sealed class AnalysisStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    readonly object _gate = new();
    readonly string _path;
    readonly List<AnalysisInfo> _items;

    public AnalysisStore(string dir)
    {
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "analyses.json");
        _items = File.Exists(_path) ? JsonSerializer.Deserialize<List<AnalysisInfo>>(File.ReadAllText(_path), Json) ?? new() : new();
    }

    public static AnalysisStore Create(IConfiguration cfg)
    {
        var dir = cfg["Jobs:Dir"];
        if (string.IsNullOrWhiteSpace(dir)) dir = "%ProgramData%/ASH.Server/jobs";
        return new AnalysisStore(Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir)));
    }

    static AnalysisInfo Clone(AnalysisInfo a) => JsonSerializer.Deserialize<AnalysisInfo>(JsonSerializer.Serialize(a, Json), Json)!;
    static string NewId(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    void Save()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items, Json));
        File.Move(tmp, _path, true);
    }

    public IReadOnlyList<AnalysisInfo> All() { lock (_gate) return _items.Select(Clone).ToList(); }

    public AnalysisInfo? Get(string id) { lock (_gate) return _items.FirstOrDefault(a => a.Id == id) is { } a ? Clone(a) : null; }

    public AnalysisInfo CreateAnalysis(string name, string note)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("분석 이름을 입력하세요.", nameof(name));
        var a = new AnalysisInfo { Id = NewId("a-"), Name = name.Trim(), Note = note ?? "", CreatedUtc = DateTime.UtcNow };
        lock (_gate) { _items.Add(a); Save(); return Clone(a); }
    }

    public PlanInfo CreatePlan(string analysisId, string name, string note)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("안 이름을 입력하세요.", nameof(name));
        name = name.Trim();
        lock (_gate)
        {
            var a = Find(analysisId);
            CheckPlanName(a, name, null);
            var p = new PlanInfo { Id = NewId("p-"), Name = name, Note = note ?? "" };
            a.Plans.Add(p); Save();
            return new PlanInfo { Id = p.Id, Name = p.Name, Note = p.Note };
        }
    }

    /// <summary>Renames the analysis, or one of its plans when planId is given.</summary>
    public void Rename(string analysisId, string? planId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("이름을 입력하세요.", nameof(name));
        name = name.Trim();
        lock (_gate)
        {
            var a = Find(analysisId);
            if (planId == null) a.Name = name;
            else
            {
                var p = a.Plans.FirstOrDefault(x => x.Id == planId) ?? throw new ArgumentException("안이 없습니다.", nameof(planId));
                CheckPlanName(a, name, p);
                p.Name = name;
            }
            Save();
        }
    }

    /// <summary>Deletes the analysis only when no job points at it.</summary>
    public bool DeleteIfEmpty(string analysisId, JobStore jobs)
    {
        lock (_gate)
        {
            var a = _items.FirstOrDefault(x => x.Id == analysisId);
            if (a == null || jobs.All().Any(j => j.AnalysisId == analysisId)) return false;
            _items.Remove(a); Save();
            return true;
        }
    }

    /// <summary>Files a job under a plan as that plan's next version and stores the version note.</summary>
    public (string AnalysisId, string PlanId, int Version) Assign(JobStore jobs, string jobId, string analysisId, string planId, string note)
        => Place(jobs, jobId, analysisId, planId, j => j.VersionNote = note ?? "");

    /// <summary>Moves a job to another plan; it becomes that plan's next version.</summary>
    public void Move(JobStore jobs, string jobId, string analysisId, string planId) => Place(jobs, jobId, analysisId, planId, null);

    public void SetSelected(JobStore jobs, string jobId, bool on)
    {
        if (!jobs.Update(jobId, j => j.Selected = on)) throw new ArgumentException("작업이 없습니다.", nameof(jobId));
    }

    (string AnalysisId, string PlanId, int Version) Place(JobStore jobs, string jobId, string analysisId, string planId, Action<JobInfo>? extra)
    {
        lock (_gate)
        {
            var a = Find(analysisId);
            if (a.Plans.All(p => p.Id != planId)) throw new ArgumentException("안이 없습니다.", nameof(planId));
            var version = jobs.All().Where(j => j.AnalysisId == analysisId && j.PlanId == planId).Select(j => j.Version).DefaultIfEmpty(0).Max() + 1;
            if (!jobs.Update(jobId, j => { j.AnalysisId = analysisId; j.PlanId = planId; j.Version = version; extra?.Invoke(j); }))
                throw new ArgumentException("작업이 없습니다.", nameof(jobId));
            return (analysisId, planId, version);
        }
    }

    AnalysisInfo Find(string id) => _items.FirstOrDefault(a => a.Id == id) ?? throw new ArgumentException("분석이 없습니다.", nameof(id));

    static void CheckPlanName(AnalysisInfo a, string name, PlanInfo? self)
    {
        if (a.Plans.Any(p => p != self && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"같은 분석에 '{name}' 안이 이미 있습니다.", nameof(name));
    }
}
