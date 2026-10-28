using System.Text.Json;
using System.Text.Json.Serialization;
using ASH.Core.Film;

namespace ASH.Server;

/// <summary>What the export form asks for (POST /api/analyses/{id}/films, camelCase JSON). Output: "each+joined" | "joined" | "each".
/// Seed null = the first listed version's SeedBase for every version. VehicleDelta (plus each cut's own) changes the conclusion counts within each group's
/// range; SameCounts, per group, replaces them for every version.</summary>
public sealed record FilmRequest(string Id, string AnalysisId, IReadOnlyList<string> CutIds, IReadOnlyList<string> JobIds, int Width, int Height, int Fps,
    string Output, bool Caption, int? Seed, int VehicleDelta, IReadOnlyDictionary<string, int>? SameCounts);

/// <summary>One cut of one version. Name = the planned file name in the film folder (unique, safe); Ok once that file was made.</summary>
public sealed class FilmItem
{
    public string JobId { get; set; } = "";
    public string CutId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Ok { get; set; }
    public string? Error { get; set; }
}

/// <summary>One export: the request, the cuts as they were when it was asked for, and what came out. Saved as films\&lt;id&gt;\film.json.</summary>
public sealed class FilmInfo
{
    public FilmRequest Request { get; set; } = null!;
    public string AnalysisName { get; set; } = "";
    public List<CutSpec> Cuts { get; set; } = new();
    public string State { get; set; } = JobState.Queued;
    public DateTime CreatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public string? Phase { get; set; }
    public string? Error { get; set; }
    /// <summary>Shown on the film page (e.g. a version filmed with the estimated F0).</summary>
    public List<string> Warnings { get; set; } = new();
    /// <summary>Cuts made / failed so far.</summary>
    public int Done { get; set; }
    public int Failed { get; set; }
    /// <summary>Cut by cut, each cut for every version (the joined film's order).</summary>
    public List<FilmItem> Items { get; set; } = new();
    /// <summary>File name of the joined film; null when not asked for or not made.</summary>
    public string? Joined { get; set; }
    public bool JoinedOk { get; set; }
    /// <summary>Set by POST /films/{id}/cancel; the worker stops the film at its next step (a queued film is cancelled at once).</summary>
    public bool CancelRequested { get; set; }

    [JsonIgnore] public string Id => Request.Id;
    [JsonIgnore] public bool Finished => State is JobState.Done or JobState.Failed or JobState.Cancelled;
    [JsonIgnore] public bool KeepEach => Request.Output != "joined";
    [JsonIgnore] public bool Join => Request.Output != "each";
}

/// <summary>Films under &lt;Jobs:Dir&gt;\films\&lt;id&gt;\ (film.json, film.log, the MP4s). Kept in memory like JobStore; a film running when the server stopped is queued again at start.
/// Films are never removed by JobRetention.</summary>
public sealed class FilmStore
{
    static readonly JsonSerializerOptions Json = new(CutStore.Json) { WriteIndented = true };
    readonly Dictionary<string, FilmInfo> _films = new(StringComparer.Ordinal);
    readonly object _gate = new();
    readonly ILogger? _log;
    public string Dir { get; }

    public FilmStore(string jobsDir, ILogger? log = null)
    {
        Dir = Path.Combine(jobsDir, "films"); _log = log;
        Directory.CreateDirectory(Dir);
        foreach (var d in Directory.GetDirectories(Dir))
        {
            var p = Path.Combine(d, "film.json");
            if (!File.Exists(p)) continue;
            try
            {
                var f = JsonSerializer.Deserialize<FilmInfo>(File.ReadAllText(p), Json);
                if (f?.Request == null || f.Id != Path.GetFileName(d)) continue;
                // cut off by a stop the worker could not record (killed): made again from the start, as the worker does on a clean stop
                if (f.State == JobState.Running) { ResetToQueue(f); Write(f); }
                _films[f.Id] = f;
            }
            catch (Exception e) when (e is JsonException or IOException) { log?.LogWarning("영상 정보를 읽지 못했습니다: {Path}: {Error}", p, e.Message); }
        }
    }

    public string FilmDir(string id) => Path.Combine(Dir, id);

    void Write(FilmInfo f)
    {
        var p = Path.Combine(FilmDir(f.Id), "film.json");
        Directory.CreateDirectory(FilmDir(f.Id));
        File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(f, Json));
        File.Move(p + ".tmp", p, true);
    }

    static FilmInfo Clone(FilmInfo f) => JsonSerializer.Deserialize<FilmInfo>(JsonSerializer.Serialize(f, Json), Json)!;

    public void Add(FilmInfo f) { lock (_gate) { _films[f.Id] = f; Write(f); } }

    public bool Update(string id, Action<FilmInfo> change)
    {
        lock (_gate)
        {
            if (!_films.TryGetValue(id, out var f)) return false;
            change(f);
            try { Write(f); } catch (IOException e) { _log?.LogWarning("film.json 저장 실패 {Id}: {Error}", id, e.Message); }
            return true;
        }
    }

    public FilmInfo? Get(string id) { lock (_gate) return _films.TryGetValue(id, out var f) ? Clone(f) : null; }

    /// <summary>Newest first.</summary>
    public IReadOnlyList<FilmInfo> All() { lock (_gate) return _films.Values.OrderByDescending(f => f.CreatedUtc).ThenByDescending(f => f.Id, StringComparer.Ordinal).Select(Clone).ToList(); }

    /// <summary>Films of the analysis that are still queued or running.</summary>
    public IReadOnlyList<FilmInfo> Active(string analysisId) { lock (_gate) return _films.Values.Where(f => f.Request.AnalysisId == analysisId && !f.Finished).Select(Clone).ToList(); }

    /// <summary>Removes the analysis' films (record and folder with its MP4s). Null, and nothing removed, while one of them is queued or running (TakeNext shares the lock).
    /// An IOException (a file still being played) stops the removal; the films left are removed by the next try.</summary>
    public int? RemoveByAnalysis(string analysisId)
    {
        lock (_gate)
        {
            var mine = _films.Values.Where(f => f.Request.AnalysisId == analysisId).ToList();
            if (mine.Any(f => !f.Finished)) return null;
            foreach (var f in mine)
            {
                if (Directory.Exists(FilmDir(f.Id))) Directory.Delete(FilmDir(f.Id), true);
                _films.Remove(f.Id);
            }
            return mine.Count;
        }
    }

    /// <summary>A film cut off by a server stop (update, restart): queued again at its old place, to be made again from the start.</summary>
    public void RequeueCut(string id) => Update(id, ResetToQueue);

    static void ResetToQueue(FilmInfo f)
    {
        f.State = JobState.Queued; f.StartedUtc = f.FinishedUtc = null; f.Phase = null; f.Error = null; f.CancelRequested = false;
        f.Warnings = new(); f.Done = f.Failed = 0; f.JoinedOk = false;
        foreach (var i in f.Items) { i.Ok = false; i.Error = null; }
    }

    /// <summary>Oldest queued film, now running; null when none waits.</summary>
    public FilmInfo? TakeNext()
    {
        lock (_gate)
        {
            var f = _films.Values.Where(x => x.State == JobState.Queued).OrderBy(x => x.CreatedUtc).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (f == null) return null;
            f.State = JobState.Running; f.StartedUtc = DateTime.UtcNow;
            Write(f);
            return Clone(f);
        }
    }
}
