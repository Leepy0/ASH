using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using ASH.Core.AutoMod;

namespace ASH.Server;

public enum SettingKind { Bool, Int, Text }

/// <summary>
/// One appsettings.json value the settings page edits. Live = the code reads IConfiguration again on every use and appsettings.json reloads on change,
/// so a save applies to the next job/check; otherwise the value is read once at start and needs a service restart.
/// Pick = "dir" or "file": the page offers a server-side folder/file browser (/admin/fs) next to the input.
/// </summary>
public sealed record SettingDef(string Key, string Label, string Help, SettingKind Kind, string Default, bool Live, int Min = 0, int Max = 0, Func<string, string?>? Check = null, string? Pick = null);

/// <summary>Every key the server code reads, except Admin:Password and Update:PublicKeyPem (security: never editable from the web) and Logging (framework).</summary>
public static class ServerSettings
{
    static string? PathCheck(string v) => v.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ? "폴더 경로에 쓸 수 없는 문자가 있습니다." : null;

    static string? Try(Action a)
    {
        try { a(); return null; }
        catch (ArgumentException e) { return e.Message; }
    }

    static string? UrlsCheck(string v)
    {
        if (v.Length == 0) return "비울 수 없습니다(예: http://0.0.0.0:8082).";
        foreach (var p in v.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!Uri.TryCreate(p.Replace("://*", "://0.0.0.0").Replace("://+", "://0.0.0.0"), UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https"))
                return $"http://주소:포트 형식이어야 합니다(여러 개는 ;로 구분): {p}";
        return null;
    }

    static string? ProxyCheck(string v) =>
        v.Length == 0 || v.Equals("none", StringComparison.OrdinalIgnoreCase) || (Uri.TryCreate(v, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")
            ? null : "비우거나 none, 또는 http://프록시:포트 를 적습니다.";

    public static readonly IReadOnlyList<SettingDef> All =
    [
        new("Jobs:Enabled", "작업 받기", "끄면 ZWCAD의 \"서버로 보내기\"를 거절합니다.", SettingKind.Bool, "true", true),
        new("Jobs:WorkerEnabled", "작업 실행", "끄면 받기만 하고 실행하지 않습니다.", SettingKind.Bool, "true", false),
        new("Jobs:AmHome", "AutoMod 폴더", "bin 아래에 빌드 exe(am264.exe)가 있는 AutoMod 설치 폴더.", SettingKind.Text, @"C:\AutoMod\12_6", true, Check: PathCheck, Pick: "dir"),
        new("Jobs:Parallel", "동시 실행 수", "한 번에 돌리는 모델 실행 수. 코어 수와 AutoMod 라이선스에 맞춥니다.", SettingKind.Int, "2", true, 1, 32),
        new("Jobs:DialogButton", "대화상자 버튼", "체험판처럼 실행 때 뜨는 대화상자에서 누를 버튼(예: Student Version). 정식판은 비웁니다.", SettingKind.Text, "", true),
        new("Jobs:RunTimeoutMin", "실행 시간 제한(분)", "모델 실행 하나의 제한.", SettingKind.Int, "30", true, 1, 1440),
        new("Jobs:BuildTimeoutMin", "빌드 시간 제한(분)", "AutoMod 빌드의 제한.", SettingKind.Int, "10", true, 1, 600),
        new("Jobs:JobTimeoutHours", "작업 시간 제한(시간)", "넘으면 끊고 다음 작업으로 넘어갑니다. 다음 작업부터 적용.", SettingKind.Int, "24", true, 1, 168),
        new("Jobs:KeepRunsDays", "실행 원본 압축(일)", "끝난 작업의 실행 폴더를 이 일수 뒤 작업 폴더의 runs.zip 하나로 압축해 보관하고 원본 폴더와 빌드 출력은 지웁니다(runs.zip 받기와 결과 화면은 그대로). 0 = 그대로 둠.", SettingKind.Int, "30", true, 0, 3650),
        new("Jobs:Dir", "작업 폴더", "올린 작업과 결과를 두는 폴더.", SettingKind.Text, "%ProgramData%/ASH.Server/jobs", false, Check: PathCheck, Pick: "dir"),
        new("Film:FfmpegPath", "ffmpeg", "영상 후처리(자막·이어 붙이기)에 쓰는 ffmpeg. 기본 ffmpeg = PATH에서 찾음. AutoView도 PATH의 ffmpeg로 MP4를 만듭니다.", SettingKind.Text, "ffmpeg", true, Check: PathCheck, Pick: "file"),
        new("Film:AviewExe", "AutoView 실행 파일", "비우면 AutoMod 폴더의 bin 안에서 찾습니다: aview64.exe(12.6 이상), 없으면 aview.exe(12.4).", SettingKind.Text, "", true, Check: PathCheck, Pick: "file"),
        new("Film:CutTimeoutMin", "컷 시간 제한(분)", "컷 하나의 AutoView 실행 제한. 넘으면 그 컷만 실패하고 다음 컷으로 넘어갑니다.", SettingKind.Int, "20", true, 1, 600),
        new("Film:CalibrationTimeoutMin", "보정 시간 제한(분)", "영상마다 처음 하는 화면 배율 보정(AutoView 1프레임 렌더) 한 번의 제한. 넘으면 추정값으로 계속하고 영상 페이지에 경고와 진단을 남깁니다.", SettingKind.Int, "3", true, 1, 60),
        new("Film:DialogTitles", "AutoView 대화상자 제목", "촬영 중 자동으로 OK(확인)를 누를 AutoView 창 제목(제목의 일부, 쉼표로 구분). 취소는 누르지 않습니다. 시간 초과 진단의 \"창\" 항목에 나온 제목을 더합니다. 제목에 Error가 든 창은 누르지 않습니다. 너무 짧은 글자는 AutoView 본 창까지 닫을 수 있습니다.", SettingKind.Text, string.Join(",", ASH.Core.Film.AutoViewRenderer.DefaultDialogTitles), true,
            Check: v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(t => t.Length < 2) ? "제목은 2글자 이상씩 쉼표로 구분해 적습니다." : null),
        new("GitHub:Enabled", "GitHub 가져오기", "새 릴리스를 github.com에서 가져옵니다.", SettingKind.Bool, "true", false),
        new("GitHub:BaseUrl", "저장소 주소", "https 저장소 주소.", SettingKind.Text, GitHubSync.DefaultBaseUrl, false, Check: v => Try(() => GitHubSync.Url(v))),
        new("GitHub:CheckIntervalMin", "확인 간격(분)", "0 = 시작할 때만.", SettingKind.Int, "60", false, 0, 10080),
        new("GitHub:UpdateServer", "서버 자동 업데이트", "서버 프로그램도 새 버전으로 스스로 교체합니다.", SettingKind.Bool, "true", false),
        new("GitHub:Proxy", "프록시", "비우면 PC 설정, none = 직접 연결, 또는 http://프록시:포트.", SettingKind.Text, "", false, Check: ProxyCheck),
        new("GitHub:Dns", "DNS 서버", "비우면 PC의 DNS. 예: 8.8.8.8, 1.1.1.1", SettingKind.Text, "", false, Check: v => Try(() => DnsLookup.ParseServers(v))),
        new("Urls", "서버 주소·포트", "예: http://0.0.0.0:8082. 포트를 바꾸면 방화벽 규칙도 바꿉니다.", SettingKind.Text, "http://0.0.0.0:8082", false, Check: UrlsCheck),
        new("Releases:Dir", "배포 파일 폴더", "릴리스 보관 폴더. 바꾸면 폴더 권한을 다시 잠급니다.", SettingKind.Text, "%ProgramData%/ASH.Server/releases", false, Check: PathCheck, Pick: "dir"),
        new("Update:AllowUnsigned", "서명 없는 릴리스 받기", "키 없는 빌드(기본)는 켜 둡니다. 끄면 서명 없는 릴리스를 모두 거절합니다.", SettingKind.Bool, "true", false),
    ];

    /// <summary>Section title of a key for the page.</summary>
    public static string Section(string key) => key.StartsWith("Jobs:") ? "작업 실행" : key.StartsWith("Film:") ? "영상 촬영" : key.StartsWith("GitHub:") ? "GitHub 가져오기·자동 업데이트" : "서버";

    /// <summary>Error text, or null with the value normalized (trimmed, "true"/"false", plain integer).</summary>
    public static string? Validate(SettingDef d, string? raw, out string value)
    {
        value = (raw ?? "").Trim();
        switch (d.Kind)
        {
            case SettingKind.Bool:
                if (!bool.TryParse(value, out var b)) return "켬 또는 끔이어야 합니다.";
                value = b ? "true" : "false";
                return null;
            case SettingKind.Int:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return "정수여야 합니다.";
                if (n < d.Min || n > d.Max) return $"{d.Min}~{d.Max} 사이여야 합니다.";
                value = n.ToString(CultureInfo.InvariantCulture);
                return null;
            default:
                if (value.Length > 500) return "너무 깁니다(500자까지).";
                return d.Check?.Invoke(value);
        }
    }

    /// <summary>Warning that does not block a save: the AutoMod build exe is missing under Jobs:AmHome.</summary>
    public static string? Warning(SettingDef d, string value) =>
        d.Key == "Jobs:AmHome" && AmBuilder.FindBuildExe(value.Length == 0 ? d.Default : value) == null
            ? "이 폴더에 AutoMod 빌드 exe가 없습니다: " + AmBuilder.BuildExeCandidates(value.Length == 0 ? d.Default : value) : null;
}

/// <summary>Result of a save: per-key errors (nothing written), or the keys written and where the previous file went.</summary>
public sealed record SettingsSave(IReadOnlyDictionary<string, string> Errors, IReadOnlyList<SettingDef> Changed, string? Backup);

/// <summary>
/// appsettings.json next to the exe. A save re-reads the file, changes only the submitted keys that differ, keeps every other key,
/// writes a temp file and swaps it in with File.Replace, keeping the old file as appsettings.json.yyyyMMdd-HHmmss.bak.
/// shortcut: comments in appsettings.json are dropped on save (the .bak keeps them); write a text-preserving editor if people start commenting the file.
/// </summary>
public sealed class AppSettingsFile(string path)
{
    static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true }; // configuration keys are case-insensitive
    static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    readonly object _gate = new();

    public string Path => path;

    public JsonObject Load()
    {
        if (!File.Exists(path)) return new JsonObject(NodeOptions);
        return JsonNode.Parse(File.ReadAllText(path), NodeOptions, ReadOptions) as JsonObject ?? throw new InvalidDataException("appsettings.json의 최상위가 객체가 아닙니다.");
    }

    /// <summary>The value at a "A:B" key as text; null when missing.</summary>
    public static string? Get(JsonObject root, string key)
    {
        JsonNode? n = root;
        foreach (var part in key.Split(':')) n = n is JsonObject o && o.TryGetPropertyValue(part, out var c) ? c : null;
        return n switch
        {
            null => null,
            JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
            _ => n.ToJsonString(),
        };
    }

    static void Set(JsonObject root, SettingDef d, string value)
    {
        var parts = d.Key.Split(':');
        var o = root;
        foreach (var p in parts[..^1])
        {
            if (o[p] is not JsonObject next) o[p] = next = new JsonObject(NodeOptions);
            o = next;
        }
        o[parts[^1]] = d.Kind switch
        {
            SettingKind.Bool => JsonValue.Create(value == "true"),
            SettingKind.Int => JsonValue.Create(int.Parse(value, CultureInfo.InvariantCulture)),
            _ => JsonValue.Create(value),
        };
    }

    public SettingsSave Save(Func<string, string?> form)
    {
        var errors = new Dictionary<string, string>();
        var values = new Dictionary<string, string>();
        foreach (var d in ServerSettings.All)
        {
            if (form(d.Key) is not { } raw) continue; // not submitted: left as it is
            if (ServerSettings.Validate(d, raw, out var v) is { } err) errors[d.Key] = err;
            else values[d.Key] = v;
        }
        if (errors.Count > 0) return new SettingsSave(errors, [], null);
        lock (_gate)
        {
            var root = Load();
            var changed = new List<SettingDef>();
            foreach (var d in ServerSettings.All)
            {
                if (!values.TryGetValue(d.Key, out var v)) continue;
                var old = Get(root, d.Key);
                if (old == null ? v == d.Default : string.Equals(old, v, d.Kind == SettingKind.Bool ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                Set(root, d, v);
                changed.Add(d);
            }
            if (changed.Count == 0) return new SettingsSave(errors, changed, null);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(WriteOptions) + Environment.NewLine);
            string? backup = null;
            if (File.Exists(path))
            {
                backup = $"{path}.{ASH.Core.SeoulTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.bak";
                File.Replace(tmp, path, backup); // keeps the folder's ACL on the new file
            }
            else File.Move(tmp, path);
            return new SettingsSave(errors, changed, backup);
        }
    }
}

/// <summary>
/// Browser sessions of the settings page: after the admin token is typed once, an HttpOnly SameSite=Strict cookie carries a random id
/// that only this process knows (8 hours; a restart logs everyone out). The token itself is never stored in the browser.
/// </summary>
public sealed class AdminSessions
{
    public const string CookieName = "ash_admin";
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    readonly ConcurrentDictionary<string, DateTime> _until = new();

    public string Create()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _until) if (kv.Value <= now) _until.TryRemove(kv.Key, out _);
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _until[id] = now + Lifetime;
        return id;
    }

    public bool Valid(string? id)
    {
        if (string.IsNullOrEmpty(id) || !_until.TryGetValue(id, out var until)) return false;
        if (until > DateTime.UtcNow) return true;
        _until.TryRemove(id, out _);
        return false;
    }

    public void End(string? id) { if (!string.IsNullOrEmpty(id)) _until.TryRemove(id, out _); }
}
