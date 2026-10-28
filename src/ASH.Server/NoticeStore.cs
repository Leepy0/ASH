using System.Globalization;
using System.Text;
using System.Text.Json;
using ASH.Core;
using ASH.Core.Update;

namespace ASH.Server;

/// <summary>One notice the server keeps. Version notes (id "v&lt;version&gt;") come from GitHub; general ones (id "n&lt;number&gt;") are written by an admin.
/// State: draft (검토 대기) → approved (공지) or hidden (숨김). Only approved notices reach a PC.</summary>
public sealed class NoticeRec
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string State { get; set; } = NoticeStore.Draft;
    public bool Important { get; set; }
    public string Title { get; set; } = "";
    public List<string> Lines { get; set; } = new();
    /// <summary>UTC, set when approved (the order of general notices).</summary>
    public string Published { get; set; } = "";
    /// <summary>General notices: display period, yyyy-MM-dd Seoul dates, "" = open end.</summary>
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    /// <summary>The admin changed the text of a version note.</summary>
    public bool Edited { get; set; }
    /// <summary>GitHub's text changed after the admin edited or approved it: the kept text is untouched, the admin decides.</summary>
    public bool Changed { get; set; }
    /// <summary>What GitHub holds for a version note (the last pull).</summary>
    public VersionNote? Github { get; set; }
    public bool General => Version.Length == 0;
}

/// <summary>Notices in Jobs:Dir\notices\notices.json. Version notes pulled from GitHub land as drafts and are served only after an admin approves them.</summary>
public sealed class NoticeStore
{
    public const string Draft = "draft", Approved = "approved", Hidden = "hidden";
    public const int TitleMax = 100, LineMax = 200, LinesMax = 30;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    readonly object _lock = new();
    readonly ILogger _log;
    readonly string _file;

    public NoticeStore(string jobsDir, ILogger log)
    {
        _log = log;
        var dir = Path.Combine(jobsDir, "notices");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "notices.json");
    }

    List<NoticeRec> Load()
    {
        if (!File.Exists(_file)) return new();
        try { return JsonSerializer.Deserialize<List<NoticeRec>>(File.ReadAllText(_file, Encoding.UTF8), Json)?.Where(n => n != null && n.Id.Length > 0).ToList() ?? new(); }
        catch (JsonException e)
        {
            _log.LogError(e, "notices.json unreadable, set aside: {File}", _file);
            try { File.Move(_file, _file + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); } catch (IOException) { }
            return new();
        }
    }

    void Save(List<NoticeRec> all)
    {
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(all, Json), new UTF8Encoding(false));
        File.Move(tmp, _file, true);
    }

    public IReadOnlyList<NoticeRec> All() { lock (_lock) return Load(); }
    public NoticeRec? Get(string id) { lock (_lock) return Load().FirstOrDefault(n => n.Id == id); }

    static bool Same(string t1, bool i1, List<string> l1, string t2, bool i2, List<string> l2) => t1 == t2 && i1 == i2 && l1.SequenceEqual(l2);
    static bool SameAsGithub(NoticeRec n, VersionNote g) => Same(n.Title, n.Important, n.Lines, g.Title, g.Important, g.Lines);
    static void Take(NoticeRec n, VersionNote g) { n.Title = g.Title; n.Important = g.Important; n.Lines = g.Lines.ToList(); }

    /// <summary>Takes the notes of a pull. New version → draft. Known version: a draft the admin never edited (or a hidden one) follows GitHub; anything the admin
    /// edited or approved keeps its text and is flagged <see cref="NoticeRec.Changed"/> when GitHub now says something else. Returns how many notes are new or changed.</summary>
    public int Pull(NotesFile f)
    {
        lock (_lock)
        {
            var all = Load();
            int touched = 0;
            foreach (var g in f.Notes)
            {
                if (!SemVersion.TryParse(g.Version, out _) || g.Title.Length == 0) continue;
                var id = "v" + g.Version;
                var n = all.FirstOrDefault(x => x.Id == id);
                if (n == null) { n = new NoticeRec { Id = id, Version = g.Version }; Take(n, g); n.Github = Copy(g); all.Add(n); touched++; continue; }
                if (n.Github != null && Same(n.Github.Title, n.Github.Important, n.Github.Lines, g.Title, g.Important, g.Lines)) continue; // GitHub unchanged
                n.Github = Copy(g);
                if (!n.Edited && n.State != Approved) Take(n, g);
                else n.Changed = !SameAsGithub(n, g);
                touched++;
            }
            if (touched > 0) Save(all);
            return touched;
        }
    }

    static VersionNote Copy(VersionNote g) => new() { Version = g.Version, Important = g.Important, Title = g.Title, Lines = g.Lines.ToList() };

    /// <summary>Why the text cannot be kept, or null.</summary>
    public static string? Check(string title, List<string> lines)
    {
        if (title.Length == 0) return "제목이 비어 있습니다.";
        if (title.Length > TitleMax) return $"제목은 {TitleMax}자까지입니다.";
        if (lines.Count > LinesMax) return $"항목은 {LinesMax}줄까지입니다.";
        if (lines.Any(l => l.Length > LineMax)) return $"항목은 한 줄 {LineMax}자까지입니다.";
        return null;
    }

    /// <summary>One line per item, blanks dropped.</summary>
    public static List<string> SplitLines(string? text) =>
        (text ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

    /// <summary>yyyy-MM-dd or empty; null = not a date.</summary>
    public static string? Day(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return "";
        return DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    /// <summary>A new general notice, as a draft. Null for a bad title/lines/period (the reason is in <paramref name="err"/>).</summary>
    public NoticeRec? Add(string title, bool important, List<string> lines, string from, string to, out string? err)
    {
        title = title.Trim();
        err = Check(title, lines) ?? Period(from, to);
        if (err != null) return null;
        lock (_lock)
        {
            var all = Load();
            int next = all.Where(n => n.General && int.TryParse(n.Id.AsSpan(1), out _)).Select(n => int.Parse(n.Id.AsSpan(1))).DefaultIfEmpty(0).Max() + 1;
            var n = new NoticeRec { Id = "n" + next, Title = title, Important = important, Lines = lines, From = from, To = to };
            all.Add(n);
            Save(all);
            return n;
        }
    }

    static string? Period(string from, string to) => from.Length > 0 && to.Length > 0 && string.CompareOrdinal(from, to) > 0 ? "시작일이 종료일보다 늦습니다." : null;

    /// <summary>Changes the text (and, for a general notice, the period). A version note whose text equals GitHub's counts as unedited again.</summary>
    public string? Edit(string id, string title, bool important, List<string> lines, string? from = null, string? to = null)
    {
        title = title.Trim();
        var bad = Check(title, lines) ?? (from != null ? Period(from, to ?? "") : null);
        if (bad != null) return bad;
        lock (_lock)
        {
            var all = Load();
            var n = all.FirstOrDefault(x => x.Id == id);
            if (n == null) return "공지가 없습니다.";
            n.Title = title; n.Important = important; n.Lines = lines;
            if (n.General) { n.From = from ?? n.From; n.To = to ?? n.To; }
            else if (n.Github != null) { n.Edited = !SameAsGithub(n, n.Github); if (!n.Edited) n.Changed = false; }
            Save(all);
            return null;
        }
    }

    /// <summary>Drops the admin's text for GitHub's.</summary>
    public bool TakeGithub(string id)
    {
        lock (_lock)
        {
            var all = Load();
            var n = all.FirstOrDefault(x => x.Id == id);
            if (n?.Github == null) return false;
            Take(n, n.Github); n.Edited = false; n.Changed = false;
            Save(all);
            return true;
        }
    }

    public bool SetState(string id, string state)
    {
        lock (_lock)
        {
            var all = Load();
            var n = all.FirstOrDefault(x => x.Id == id);
            if (n == null) return false;
            if (state == Approved && n.State != Approved) n.Published = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            n.State = state;
            Save(all);
            return true;
        }
    }

    /// <summary>General notices only: a version note would come back as a draft with the next pull, so it is hidden instead.</summary>
    public bool Delete(string id)
    {
        lock (_lock)
        {
            var all = Load();
            if (all.RemoveAll(n => n.Id == id && n.General) == 0) return false;
            Save(all);
            return true;
        }
    }

    /// <summary>What a PC on <paramref name="current"/> gets: approved version notes with current &lt; version ≤ latest (an unknown current counts as older than all),
    /// newest first, then approved general notices inside their period (Seoul date), newest first.</summary>
    public NoticeList For(string? current, string latest, DateTime nowUtc)
    {
        // "0.31.0+abc123" (the plugin's InformationalVersion): the build part is cut; an unencoded "+" arrives as a space
        SemVersion.TryParse(current?.Trim().Split('+', ' ')[0], out var cur);
        SemVersion.TryParse(latest, out var top);
        var today = SeoulTime.FromUtc(nowUtc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var all = All().Where(n => n.State == Approved).ToList();
        var list = new NoticeList { Latest = latest };
        if (top != null)
        {
            var vs = all.Where(n => !n.General).Select(n => (V: SemVersion.TryParse(n.Version, out var v) ? v! : null!, N: n))
                .Where(x => x.V != null && x.V.CompareTo(top) <= 0 && (cur == null || x.V.CompareTo(cur) > 0)).ToList();
            vs.Sort((a, b) => b.V.CompareTo(a.V));
            list.Notices.AddRange(vs.Select(x => ToNotice(x.N)));
        }
        list.Notices.AddRange(all.Where(n => n.General && (n.From.Length == 0 || string.CompareOrdinal(n.From, today) <= 0) && (n.To.Length == 0 || string.CompareOrdinal(n.To, today) >= 0))
            .OrderByDescending(n => n.Published, StringComparer.Ordinal).ThenByDescending(n => n.Id.Length).ThenByDescending(n => n.Id, StringComparer.Ordinal)
            .Select(ToNotice));
        return list;
    }

    static Notice ToNotice(NoticeRec n) => new() { Id = n.Id, Version = n.Version, Important = n.Important, Title = n.Title, Lines = n.Lines.ToList(), Published = n.Published };
}
