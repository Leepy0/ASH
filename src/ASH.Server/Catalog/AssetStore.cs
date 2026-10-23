using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ASH.Core.Cel;

namespace ASH.Server.Catalog;

public sealed class AssetInfo
{
    public string Id { get; set; } = "";          // "s-" + 8 hex
    public string TypeName { get; set; } = "";
    public string Name { get; set; } = "";        // unique within the type, [A-Za-z0-9_] (Excel Asset column value)
    public string Note { get; set; } = "";
    public int ConveyorDir { get; set; }          // 0/90/180/270, memo only
    public int Boxes { get; set; }
    public string Source { get; set; } = "";      // original file name
    public SimplifyOptions Options { get; set; } = new();
    public double LengthM { get; set; } public double WidthM { get; set; } public double HeightM { get; set; }
    public DateTime CreatedUtc { get; set; } public DateTime UpdatedUtc { get; set; } public string UpdatedBy { get; set; } = "";
}

/// <summary>&lt;Jobs:Dir&gt;\catalog\assets\&lt;id&gt;\{source.&lt;ext&gt;, asset.cel, preview.svg, asset.json}; in memory like JobStore; a folder with a bad asset.json is logged and skipped.</summary>
public sealed class AssetStore
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    static readonly Regex NameRx = new("^[A-Za-z0-9_]{1,40}$", RegexOptions.Compiled);
    static readonly string[] Files = { "asset.cel", "preview.svg", "asset.json" };
    readonly object _lock = new();
    readonly Dictionary<string, AssetInfo> _all = new();
    readonly ILogger<AssetStore> _log;
    public string Dir { get; }

    public AssetStore(string jobsDir, ILogger<AssetStore> log)
    {
        _log = log;
        Dir = System.IO.Path.Combine(jobsDir, "catalog", "assets");
        Directory.CreateDirectory(Dir);
        foreach (var d in Directory.GetDirectories(Dir))
        {
            var n = System.IO.Path.GetFileName(d);
            if (n.StartsWith(".tmp-")) { try { Directory.Delete(d, true); } catch (IOException) { } continue; } // left by a crashed Create
            try
            {
                var a = JsonSerializer.Deserialize<AssetInfo>(File.ReadAllText(System.IO.Path.Combine(d, "asset.json")), Json);
                if (a == null || a.Id != n) throw new JsonException("id mismatch");
                _all[a.Id] = a;
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {
                _log.LogError(e, "asset skipped: {Dir}", d);
            }
        }
    }

    public IReadOnlyList<AssetInfo> All() { lock (_lock) return _all.Values.OrderBy(a => a.TypeName, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
    public IReadOnlyList<AssetInfo> ForType(string typeName) => All().Where(a => a.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase)).ToList();
    public AssetInfo? Find(string id) { lock (_lock) return _all.GetValueOrDefault(id); }
    public AssetInfo? FindByName(string typeName, string name) => ForType(typeName).FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    string Folder(string id) => System.IO.Path.Combine(Dir, id);

    public string Path(string id, string file)
    {
        if (!Files.Contains(file) || Find(id) == null) throw new ArgumentException("알 수 없는 에셋 파일입니다.");
        return System.IO.Path.Combine(Folder(id), file);
    }

    static void CheckName(string name)
    {
        if (!NameRx.IsMatch(name)) throw new ArgumentException("이름은 영문·숫자·_ 1~40자여야 합니다.");
    }

    static void CheckArgs(string typeName, int conveyorDir)
    {
        if (string.IsNullOrWhiteSpace(typeName)) throw new ArgumentException("타입 이름이 비었습니다.");
        if (conveyorDir is not (0 or 90 or 180 or 270)) throw new ArgumentException("컨베이어 방향은 0/90/180/270 이어야 합니다.");
    }

    /// <summary>Copies the upload to source.&lt;ext&gt;, converts, writes the three files. ArgumentException for a bad name or a duplicate (type, name); conversion exceptions pass through and nothing is left behind.</summary>
    public AssetInfo Create(string typeName, string name, string note, int conveyorDir, string uploadPath, string originalFileName, SimplifyOptions o, string by)
    {
        CheckName(name);
        CheckArgs(typeName, conveyorDir);
        var ext = System.IO.Path.GetExtension(originalFileName).ToLowerInvariant();
        if (ext is not (".stl" or ".obj")) throw new NotSupportedException("STL/OBJ 파일만 올릴 수 있습니다.");
        lock (_lock)
        {
            if (FindByName(typeName, name) != null) throw new ArgumentException($"타입 {typeName}에 같은 이름의 에셋이 있습니다: {name}");
            var id = "s-" + Guid.NewGuid().ToString("N")[..8];
            var tmp = System.IO.Path.Combine(Dir, ".tmp-" + id);
            Directory.CreateDirectory(tmp);
            try
            {
                var src = System.IO.Path.Combine(tmp, "source" + ext);
                File.Copy(uploadPath, src);
                var now = DateTime.UtcNow;
                var a = new AssetInfo { Id = id, TypeName = typeName, Name = name, Source = originalFileName, CreatedUtc = now };
                Fill(a, tmp, src, note, conveyorDir, o, by, now);
                Directory.Move(tmp, Folder(id));
                _all[id] = a;
                return a;
            }
            catch { try { Directory.Delete(tmp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } throw; }
        }
    }

    /// <summary>Converts src and writes asset.cel/preview.svg/asset.json into dir; a is updated in place.</summary>
    static void Fill(AssetInfo a, string dir, string src, string note, int conveyorDir, SimplifyOptions o, string by, DateTime now)
    {
        var r = AssetConverter.Convert(src, a.Name, o);
        a.Note = note; a.ConveyorDir = conveyorDir; a.Options = o; a.Boxes = r.Boxes.Count;
        a.LengthM = r.Stats.LengthM; a.WidthM = r.Stats.WidthM; a.HeightM = r.Stats.HeightM; a.UpdatedUtc = now; a.UpdatedBy = by;
        File.WriteAllText(System.IO.Path.Combine(dir, "asset.cel"), r.Cel);
        File.WriteAllText(System.IO.Path.Combine(dir, "preview.svg"), r.Svg);
        File.WriteAllText(System.IO.Path.Combine(dir, "asset.json"), JsonSerializer.Serialize(a, Json));
    }

    /// <summary>Re-runs the conversion on the kept source with new options/name/note; previous asset.cel/asset.json go to .bak.</summary>
    public AssetInfo Reconvert(string id, string name, string note, int conveyorDir, SimplifyOptions o, string by)
    {
        CheckName(name);
        if (conveyorDir is not (0 or 90 or 180 or 270)) throw new ArgumentException("컨베이어 방향은 0/90/180/270 이어야 합니다.");
        lock (_lock)
        {
            var old = Find(id) ?? throw new ArgumentException("에셋을 찾을 수 없습니다.");
            var other = FindByName(old.TypeName, name);
            if (other != null && other.Id != id) throw new ArgumentException($"타입 {old.TypeName}에 같은 이름의 에셋이 있습니다: {name}");
            var dir = Folder(id);
            var src = Directory.GetFiles(dir, "source.*").Single();
            var tmp = System.IO.Path.Combine(Dir, ".tmp-" + id);
            Directory.CreateDirectory(tmp);
            try
            {
                var a = JsonSerializer.Deserialize<AssetInfo>(JsonSerializer.Serialize(old, Json), Json)!; // a copy: the live entry changes only after the files do
                a.Name = name;
                Fill(a, tmp, src, note, conveyorDir, o, by, DateTime.UtcNow);
                foreach (var f in Files) // each file swaps with its own backup in one step, so a failure never leaves the folder without it
                {
                    var from = System.IO.Path.Combine(tmp, f); var to = System.IO.Path.Combine(dir, f);
                    if (File.Exists(to)) File.Replace(from, to, f == "preview.svg" ? null : to + ".bak"); else File.Move(from, to);
                }
                _all[id] = a;
                return a;
            }
            finally { try { Directory.Delete(tmp, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
        }
    }

    public bool Delete(string id)
    {
        lock (_lock)
        {
            if (!_all.ContainsKey(id)) return false;
            Directory.Delete(Folder(id), true); // first: a failure keeps the entry
            _all.Remove(id);
            return true;
        }
    }
}
