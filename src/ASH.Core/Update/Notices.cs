using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace ASH.Core.Update;

/// <summary>
/// Notices: what changed in a version (written in the repo as release-notes/&lt;version&gt;.md, shipped with every release as ASH.Notes.json holding all
/// versions) and general announcements. The server pulls the notes, an admin edits and approves them (nothing reaches a PC unapproved), and the plugin
/// shows, at ZWCAD start, every approved note newer than itself in one window (important ones as a popup).
/// </summary>
[DataContract]
public sealed class VersionNote
{
    [DataMember(Name = "version", Order = 0)] public string Version { get; set; } = "";
    /// <summary>Shown as a popup at ZWCAD start; otherwise a line on the command line.</summary>
    [DataMember(Name = "important", Order = 1)] public bool Important { get; set; }
    [DataMember(Name = "title", Order = 2)] public string Title { get; set; } = "";
    /// <summary>Short lines, one change each, words not sentences.</summary>
    [DataMember(Name = "lines", Order = 3)] public List<string> Lines { get; set; } = new();
}

/// <summary>ASH.Notes.json, a release asset: the notes of every version in the repo, newest first.</summary>
[DataContract]
public sealed class NotesFile
{
    public const string FileName = "ASH.Notes.json";
    [DataMember(Name = "notes", Order = 0)] public List<VersionNote> Notes { get; set; } = new();

    // the serializer skips initializers: a missing list comes back null
    public static NotesFile Parse(byte[] json)
    {
        var f = Json.Read<NotesFile>(json, "공지 원본(ASH.Notes.json)");
        f.Notes ??= new();
        foreach (var n in f.Notes) { n.Version ??= ""; n.Title ??= ""; n.Lines ??= new(); }
        return f;
    }
    public byte[] ToJson() => Json.Write(this);

    /// <summary>release-notes/&lt;version&gt;.md: first line "# title", an optional line "중요", then "- line" items; other lines are ignored.
    /// The version is the file name without .md.</summary>
    public static VersionNote ParseMarkdown(string version, string text)
    {
        var n = new VersionNote { Version = version };
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var l = raw.Trim();
            if (l.StartsWith("#")) { if (n.Title.Length == 0) n.Title = l.TrimStart('#').Trim(); }
            else if (l == "중요" || l.Equals("important", StringComparison.OrdinalIgnoreCase)) n.Important = true;
            else if (l.StartsWith("- ") || l.StartsWith("* ")) n.Lines.Add(l.Substring(2).Trim());
        }
        return n;
    }
}

/// <summary>One notice as the server keeps and serves it. Id: "v&lt;version&gt;" for a version note, "n&lt;number&gt;" for a general one.</summary>
[DataContract]
public sealed class Notice
{
    [DataMember(Name = "id", Order = 0)] public string Id { get; set; } = "";
    /// <summary>The version a version note describes; "" for a general notice.</summary>
    [DataMember(Name = "version", Order = 1)] public string Version { get; set; } = "";
    [DataMember(Name = "important", Order = 2)] public bool Important { get; set; }
    [DataMember(Name = "title", Order = 3)] public string Title { get; set; } = "";
    [DataMember(Name = "lines", Order = 4)] public List<string> Lines { get; set; } = new();
    /// <summary>UTC, yyyy-MM-ddTHH:mm:ssZ, when the admin approved it (the order of general notices).</summary>
    [DataMember(Name = "published", Order = 5)] public string Published { get; set; } = "";
}

/// <summary>GET /api/notices?current=&lt;plugin version&gt;: the approved version notes newer than current (and not newer than the server's published
/// plugin version), newest first, then the approved general notices that are in their display period, newest first. Latest = the server's published version.</summary>
[DataContract]
public sealed class NoticeList
{
    public const string Path = "api/notices";
    [DataMember(Name = "latest", Order = 0)] public string Latest { get; set; } = "";
    [DataMember(Name = "notices", Order = 1)] public List<Notice> Notices { get; set; } = new();

    public static NoticeList Parse(byte[] json)
    {
        var l = Json.Read<NoticeList>(json, "공지 목록");
        l.Latest ??= ""; l.Notices ??= new();
        foreach (var n in l.Notices) { n.Id ??= ""; n.Version ??= ""; n.Title ??= ""; n.Lines ??= new(); n.Published ??= ""; }
        return l;
    }
    public byte[] ToJson() => Json.Write(this);
}

static class Json
{
    public static T Read<T>(byte[] json, string what) where T : class
    {
        try
        {
            using var ms = new MemoryStream(json);
            return new DataContractJsonSerializer(typeof(T)).ReadObject(ms) as T ?? throw new InvalidDataException(what + "이(가) 비어 있습니다.");
        }
        catch (Exception e) when (e is SerializationException || e is ArgumentException || e is FormatException || e is InvalidCastException)
        {
            throw new InvalidDataException(what + "을(를) 읽을 수 없습니다: " + e.Message, e);
        }
    }

    public static byte[] Write<T>(T value)
    {
        using var ms = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(ms, value);
        return ms.ToArray();
    }
}
