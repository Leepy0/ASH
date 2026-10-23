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

    public AnalysisStore(string dir, ILogger? log = null)
    {
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "analyses.json");
        _items = new();
        if (!File.Exists(_path)) return;
        var text = File.ReadAllText(_path); // an IOException here (file locked, disk error) is not corruption: let it stop the start and name the file
        try { _items = JsonSerializer.Deserialize<List<AnalysisInfo>>(text, Json) ?? throw new JsonException("내용이 비어 있습니다."); }
        catch (JsonException e)
        {
            // keep the server up: set the unreadable file aside (never overwriting an earlier one) and start with an empty catalog
            var stamp = ASH.Core.SeoulTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            var bad = _path + ".bad-" + stamp;
            for (var i = 2; File.Exists(bad); i++) bad = _path + ".bad-" + stamp + "-" + i;
            File.Move(_path, bad);
            _items = new();
            log?.LogError("분석 목록을 읽지 못해 {Bad} 로 옮기고 빈 목록으로 시작합니다 ({Path}): {Error}", bad, _path, e.Message);
        }
    }

    public static AnalysisStore Create(IConfiguration cfg, ILogger? log = null)
    {
        var dir = cfg["Jobs:Dir"];
        if (string.IsNullOrWhiteSpace(dir)) dir = "%ProgramData%/ASH.Server/jobs";
        return new AnalysisStore(Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir)), log);
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

    /// <summary>Admin delete: every job of the analysis is kept and becomes unclassified (AnalysisId/PlanId null, Version 0, not selected; the note stays),
    /// then the analysis and its plans go. Jobs first: a stop in between leaves an empty analysis, never a job pointing at a missing one. Null when there is no such analysis.</summary>
    public int? DeleteAnalysis(string analysisId, JobStore jobs)
    {
        lock (_gate)
        {
            var a = _items.FirstOrDefault(x => x.Id == analysisId);
            if (a == null) return null;
            var mine = jobs.All().Where(j => j.AnalysisId == analysisId).ToList();
            foreach (var j in mine) jobs.Update(j.Id, x => { x.AnalysisId = null; x.PlanId = null; x.Version = 0; x.Selected = false; });
            _items.Remove(a); Save();
            return mine.Count;
        }
    }

    /// <summary>Files a job under a plan as that plan's next version and stores the version note.</summary>
    public (string AnalysisId, string PlanId, int Version) Assign(JobStore jobs, string jobId, string analysisId, string planId, string note)
        => Place(jobs, jobId, analysisId, planId, j => j.VersionNote = note ?? "");

    /// <summary>Moves a job to another plan; it becomes that plan's next version.</summary>
    /// <summary>Files the job as the next version of the plan; moved to another analysis it leaves that analysis' comparison (Selected off).</summary>
    public void Move(JobStore jobs, string jobId, string analysisId, string planId) => Place(jobs, jobId, analysisId, planId, j => { if (j.AnalysisId != analysisId) j.Selected = false; });

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
            if (!jobs.Update(jobId, j => { extra?.Invoke(j); j.AnalysisId = analysisId; j.PlanId = planId; j.Version = version; }))
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
