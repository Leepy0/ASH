using System.Text.Json;
using System.Text.Json.Serialization;
using ASH.Core.Film;

namespace ASH.Server;

/// <summary>An analysis' film cuts, saved as &lt;Jobs:Dir&gt;\analyses\&lt;analysisId&gt;\cuts.json. Every save is checked first; a rejected list leaves the saved one alone.</summary>
public sealed class CutStore
{
    /// <summary>The file and the web API share one shape: camelCase names, the cut kind as its name ("Hold", "Move", "Whole").</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public const int MaxCuts = 200;

    readonly string _dir;
    readonly object _gate = new();

    readonly ILogger? _log;

    public CutStore(string jobsDir, ILogger? log = null) { _dir = Path.Combine(jobsDir, "analyses"); _log = log; }

    string FilePath(string analysisId)
    {
        if (analysisId.Length is 0 or > 64 || !analysisId.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) throw new ArgumentException("분석 id가 올바르지 않습니다.");
        return Path.Combine(_dir, analysisId, "cuts.json");
    }

    public IReadOnlyList<CutSpec> Get(string analysisId)
    {
        var p = FilePath(analysisId);
        lock (_gate)
        {
            if (!File.Exists(p)) return new List<CutSpec>();
            var text = File.ReadAllText(p); // an IOException (locked, disk) is not corruption: let it surface
            try { return JsonSerializer.Deserialize<List<CutSpec>>(text, Json) ?? throw new JsonException("내용이 비어 있습니다."); }
            catch (JsonException e)
            {
                // keep the editor usable: set the unreadable file aside (never overwriting an earlier one) and start empty, like AnalysisStore
                var stamp = ASH.Core.SeoulTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
                var bad = p + ".bad-" + stamp;
                for (var i = 2; File.Exists(bad); i++) bad = p + ".bad-" + stamp + "-" + i;
                File.Move(p, bad);
                _log?.LogError("컷 목록을 읽지 못해 {Bad} 로 옮기고 빈 목록으로 시작합니다: {Error}", bad, e.Message);
                return new List<CutSpec>();
            }
        }
    }

    /// <exception cref="ArgumentException">with the Korean reason when a cut is out of range</exception>
    public void Save(string analysisId, IReadOnlyList<CutSpec> cuts)
    {
        var p = FilePath(analysisId);
        if (Validate(cuts) is { } why) throw new ArgumentException(why);
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(cuts, Json));
            File.Move(tmp, p, true);
        }
    }

    /// <summary>Null when every cut is usable, else the first problem in Korean ("2번 컷 '이름': 기울기는 0~70°입니다.").</summary>
    public static string? Validate(IReadOnlyList<CutSpec?> cuts)
    {
        if (cuts.Count > MaxCuts) return $"컷은 {MaxCuts}개까지입니다.";
        static bool Time(double s) => s >= 0 && double.IsFinite(s);
        static bool Area(Rect? r) => r != null && double.IsFinite(r.X) && double.IsFinite(r.Y) && r.W > 0 && r.H > 0 && double.IsFinite(r.W) && double.IsFinite(r.H);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < cuts.Count; i++)
        {
            var c = cuts[i];
            var at = $"{i + 1}번 컷";
            if (c == null) return at + ": 내용이 없습니다.";
            if (string.IsNullOrEmpty(c.Id) || c.Id.Length > 40 || !c.Id.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')) return at + ": id가 올바르지 않습니다(영문·숫자·-·_ 40자까지).";
            if (!ids.Add(c.Id)) return at + ": id가 겹칩니다.";
            var name = c.Name?.Trim() ?? "";
            if (name.Length is < 1 or > 60) return at + ": 이름은 1~60자입니다.";
            at += $" '{name}'";
            if (!Enum.IsDefined(c.Kind)) return at + ": 종류가 올바르지 않습니다.";
            if (!(c.Speed >= 1 && c.Speed <= 600)) return at + ": 배속은 1~600배입니다.";
            if (!(c.TiltDeg >= 0 && c.TiltDeg <= 70)) return at + ": 기울기는 0~70°입니다.";
            if (!(c.DirDeg >= 0 && c.DirDeg < 360)) return at + ": 방향은 0~359°입니다.";
            if (!Time(c.StartSec) || !Time(c.HoldSec) || !Time(c.HoldASec) || !Time(c.MoveSec) || !Time(c.HoldBSec)) return at + ": 시각과 시간은 0초 이상이어야 합니다.";
            if (!(c.SimSeconds > 0)) return at + ": 촬영 시간은 0초보다 길어야 합니다.";
            if (!Area(c.A)) return at + ": 구역 A가 없거나 크기가 0입니다.";
            if (c.Kind == CutKind.Move && c.B == null) return at + ": A → B 이동에는 구역 B가 필요합니다.";
            if (c.B != null && !Area(c.B)) return at + ": 구역 B의 크기가 0입니다.";
            if (c.VehicleDelta is < -20 or > 20) return at + ": 대수 변경은 -20~20대입니다.";
        }
        return null;
    }
}
