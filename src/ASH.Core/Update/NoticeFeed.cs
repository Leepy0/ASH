using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ASH.Core.Update;

/// <summary>
/// The plugin's side of the notices: fetch from the server, decide what is unseen and whether it is a popup, build the window text, and keep
/// the per-user seen list. No UI and no ZWCAD type here, so it is testable.
/// </summary>
public static class NoticeFeed
{
    public const string SeenFileName = "notices.seen";
    private const int MaxBytes = 1 << 20;

    /// <summary>%APPDATA%\ASH\notices.seen: one seen key per line.</summary>
    public static string SeenPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ASH", SeenFileName);

    /// <summary>GET {server}/api/notices?current=&lt;current&gt;. Failures (timeout, connection, status, bad JSON) are thrown for the caller to swallow or show.
    /// "0.0.0" as <paramref name="current"/> asks for every approved note.</summary>
    public static async Task<NoticeList> FetchAsync(Uri server, string current, TimeSpan timeout, HttpMessageHandler? handler = null, CancellationToken ct = default)
    {
        using var http = new HttpClient(handler ?? UpdateClient.DefaultHandler()) { Timeout = System.Threading.Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = MaxBytes };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var url = server.AbsoluteUri.TrimEnd('/') + "/" + NoticeList.Path + "?current=" + Uri.EscapeDataString(current);
        using var r = await http.GetAsync(url, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
        r.EnsureSuccessStatusCode();
        return NoticeList.Parse(await r.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
    }

    /// <summary>The seen key. A version note: its id ("v0.31.0"). A general notice: id and published time ("n3@2026-10-29T01:02:03Z"), so a
    /// notice the admin edited and approved again (new published time) shows again.</summary>
    public static string Key(Notice n) => n.Version.Length > 0 ? n.Id : n.Id + "@" + n.Published;

    /// <summary>Missing or unreadable file = nothing seen (a notice may show once more, never the reverse).</summary>
    public static HashSet<string> LoadSeen(string path)
    {
        try { return new HashSet<string>(File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0), StringComparer.Ordinal); }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return new HashSet<string>(StringComparer.Ordinal); }
    }

    /// <summary>Appends the keys of <paramref name="shown"/> that are not in the file yet. IO errors are thrown.</summary>
    public static void MarkSeen(string path, IEnumerable<Notice> shown)
    {
        var have = LoadSeen(path);
        var add = shown.Where(n => n.Id.Length > 0).Select(Key).Where(k => have.Add(k)).ToList();
        if (add.Count == 0) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, string.Concat(add.Select(k => k + "\r\n")), new UTF8Encoding(false));
    }

    /// <summary>The notices (with an id) whose key is not in <paramref name="seen"/>, in the server's order.</summary>
    public static List<Notice> Unseen(IEnumerable<Notice> all, ISet<string> seen) => all.Where(n => n.Id.Length > 0 && !seen.Contains(Key(n))).ToList();

    /// <summary>A popup when any of them is important; otherwise one line.</summary>
    public static bool ShouldPopup(IReadOnlyCollection<Notice> unseen) => unseen.Any(n => n.Important);

    /// <summary>True when the server's latest version is newer than <paramref name="current"/> (unparsable = false).</summary>
    public static bool UpdateAvailable(string latest, string current) =>
        SemVersion.TryParse(latest, out var l) && SemVersion.TryParse(current, out var c) && l!.CompareTo(c!) > 0;

    /// <summary>Command line and palette status for the non-popup case.</summary>
    public static string Summary(int count) => $"공지 {count}건 · AGVNOTICE로 보기";

    /// <summary>Window sections: version notes first ("v0.31.0 · 제목"), then general ones ("공지 · 제목"), each in the given order.</summary>
    public static List<(string Header, List<string> Bullets)> Sections(IEnumerable<Notice> notices)
    {
        var all = notices.ToList();
        return all.Where(n => n.Version.Length > 0).Concat(all.Where(n => n.Version.Length == 0))
            .Select(n => ((n.Version.Length > 0 ? "v" + n.Version : "공지") + (n.Title.Length > 0 ? " · " + n.Title : ""), n.Lines.Select(l => "• " + l).ToList()))
            .ToList();
    }

    /// <summary>The window text: a header line then its bullets, a blank line between notices.</summary>
    public static string Text(IEnumerable<Notice> notices) =>
        string.Join("\r\n\r\n", Sections(notices).Select(s => string.Join("\r\n", new[] { s.Header }.Concat(s.Bullets))));
}
