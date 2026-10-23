using System.Text.Json;

namespace ASH.Server;

/// <summary>작업 폴더의 index.json: {"materials":["M1",…]}. input.cells.tsv의 Materials 시트 Material 열(1행 머리글)에서 읽는다.</summary>
public static class FileIndex
{
    public const string FileName = "index.json";
    sealed record Doc(List<string> Materials);
    static readonly JsonSerializerOptions Opt = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IReadOnlyList<string> MaterialsFromCells(string cellsPath)
    {
        try
        {
            if (!File.Exists(cellsPath)) return Array.Empty<string>();
            int col = 0;
            var cells = new List<(int Row, int Col, string Value)>();
            foreach (var line in File.ReadLines(cellsPath))
            {
                var f = line.Split('	');
                if (f.Length < 5 || f[0] != "Materials" || !int.TryParse(f[1], out var r) || !int.TryParse(f[2], out var c)) continue;
                var v = f[4].Replace("\\\\", "\\");
                if (r == 1) { if (col == 0 && v == "Material") col = c; }
                else if (r >= 3 && v.Length > 0) cells.Add((r, c, v));
            }
            return col == 0 ? Array.Empty<string>() : cells.Where(x => x.Col == col).OrderBy(x => x.Row).Select(x => x.Value).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    public static IReadOnlyList<string> Ensure(JobStore js, string id)
    {
        var path = Path.Combine(js.JobDir(id), FileName);
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<Doc>(File.ReadAllText(path), Opt)?.Materials ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException) { }
        var list = MaterialsFromCells(Path.Combine(js.ProjectDir(id), ASH.Core.Input.CellDump.FileName));
        try
        {
            Directory.CreateDirectory(js.JobDir(id));
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Doc(list.ToList()), Opt));
            File.Move(path + ".tmp", path, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return list;
    }

    public static void BuildMissing(JobStore js, ILogger log)
    {
        foreach (var j in js.All())
            if (j.Finished && !File.Exists(Path.Combine(js.JobDir(j.Id), FileName)))
                try { Ensure(js, j.Id); } catch (Exception e) { log.LogWarning("index.json 실패 {Id}: {Error}", j.Id, e.Message); }
    }
}
