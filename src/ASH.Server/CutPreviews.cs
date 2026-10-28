using System.Text.Json;
using ASH.Core.Film;

namespace ASH.Server;

/// <summary>One cut's preview: one AutoView frame of <see cref="Cut"/> (as the editor had it when asked) at <see cref="AtSec"/> on version <see cref="JobId"/>.</summary>
public sealed class PreviewInfo
{
    public string AnalysisId { get; set; } = "";
    public string JobId { get; set; } = "";
    public CutSpec Cut { get; set; } = null!;
    public double AtSec { get; set; }
    public string State { get; set; } = JobState.Queued;
    public string? Phase { get; set; }
    public string? Error { get; set; }
    public DateTime RequestedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
}

/// <summary>Cut previews under &lt;Jobs:Dir&gt;\analyses\&lt;id&gt;\previews\ (&lt;cutId&gt;.json, &lt;cutId&gt;.jpg, and work\, the cached film model of the last preview).
/// Kept in memory like FilmStore; a preview running when the server stopped is failed at start. Removed with the analysis' cut folder.</summary>
public sealed class PreviewStore
{
    readonly string _dir;
    readonly Dictionary<(string, string), PreviewInfo> _items = new();
    readonly object _gate = new();

    public PreviewStore(string jobsDir, ILogger? log = null)
    {
        _dir = Path.Combine(jobsDir, "analyses");
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.GetDirectories(_dir).Select(d => Path.Combine(d, "previews")).Where(Directory.Exists).SelectMany(d => Directory.GetFiles(d, "*.json")))
            try
            {
                var p = JsonSerializer.Deserialize<PreviewInfo>(File.ReadAllText(f), CutStore.Json);
                if (p?.Cut == null || !Ok(p.AnalysisId) || !Ok(p.Cut.Id)) continue;
                if (p.State is JobState.Queued or JobState.Running) { p.State = JobState.Failed; p.Error = "서버가 다시 시작되어 중단되었습니다. 다시 누르세요."; Write(p); }
                _items[(p.AnalysisId, p.Cut.Id)] = p;
            }
            catch (Exception e) when (e is JsonException or IOException) { log?.LogWarning("미리보기 정보를 읽지 못했습니다: {Path}: {Error}", f, e.Message); }
    }

    /// <summary>Ids that are safe as folder and file names (CutStore's rules).</summary>
    public static bool Ok(string? id) => id is { Length: > 0 and <= 64 } && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public string Dir(string analysisId) => Ok(analysisId) ? Path.Combine(_dir, analysisId, "previews") : throw new ArgumentException("분석 id가 올바르지 않습니다.");
    public string ImagePath(string analysisId, string cutId) => Ok(cutId) ? Path.Combine(Dir(analysisId), cutId + ".jpg") : throw new ArgumentException("컷 id가 올바르지 않습니다.");

    void Write(PreviewInfo p)
    {
        var d = Dir(p.AnalysisId);
        Directory.CreateDirectory(d);
        var path = Path.Combine(d, p.Cut.Id + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(p, CutStore.Json));
        File.Move(path + ".tmp", path, true);
    }

    static PreviewInfo Clone(PreviewInfo p) => JsonSerializer.Deserialize<PreviewInfo>(JsonSerializer.Serialize(p, CutStore.Json), CutStore.Json)!;

    public PreviewInfo? Get(string analysisId, string cutId) { lock (_gate) return _items.TryGetValue((analysisId, cutId), out var p) ? Clone(p) : null; }

    /// <summary>Queues (or re-queues) the cut's preview; false while that cut's preview is being made.</summary>
    public bool Request(PreviewInfo p)
    {
        lock (_gate)
        {
            if (_items.TryGetValue((p.AnalysisId, p.Cut.Id), out var old) && old.State == JobState.Running) return false;
            p.State = JobState.Queued; p.RequestedUtc = DateTime.UtcNow;
            _items[(p.AnalysisId, p.Cut.Id)] = p;
            Write(p);
            return true;
        }
    }

    public void Update(string analysisId, string cutId, Action<PreviewInfo> change)
    {
        lock (_gate)
        {
            if (!_items.TryGetValue((analysisId, cutId), out var p)) return;
            change(p);
            try { Write(p); } catch (IOException) { } // the state in memory is what the page reads
        }
    }

    /// <summary>Oldest queued preview, now running; null when none waits.</summary>
    public PreviewInfo? TakeNext()
    {
        lock (_gate)
        {
            var p = _items.Values.Where(x => x.State == JobState.Queued).OrderBy(x => x.RequestedUtc).FirstOrDefault();
            if (p == null) return null;
            p.State = JobState.Running;
            Write(p);
            return Clone(p);
        }
    }

    /// <summary>Queued and running previews, the running one first, then in the order they are taken.</summary>
    public IReadOnlyList<PreviewInfo> Active()
    {
        lock (_gate) return _items.Values.Where(x => x.State is JobState.Queued or JobState.Running).OrderBy(x => x.State == JobState.Running ? 0 : 1).ThenBy(x => x.RequestedUtc).Select(Clone).ToList();
    }

    /// <summary>Previews queued before this one (any analysis).</summary>
    public int Ahead(PreviewInfo p) { lock (_gate) return _items.Values.Count(x => x.State == JobState.Queued && x.RequestedUtc < p.RequestedUtc); }
}
