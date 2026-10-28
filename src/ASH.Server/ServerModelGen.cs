using System.Text;
using ASH.Core.Layout;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Server.Catalog;

namespace ASH.Server;

/// <summary>Source package (layout.tsv, input.cells.tsv, ash.settings.tsv) to model at upload: the server's generator makes the .arc, so a generator fix needs a server update only.</summary>
internal static class ServerModelGen
{
    const int MaxErrorLines = 20;

    /// <summary>False = the package carries its own .arc (nothing done). True = the model was made in <paramref name="projectDir"/> and the log lines were added to <paramref name="logPath"/>.
    /// Input errors and unreadable files throw ArgumentException with the text for the 400 answer; the caller removes the job folder.</summary>
    public static bool Run(string projectDir, string model, AssetStore assets, string serverVersion, string logPath)
    {
        if (!JobPackage.IsSource(projectDir, model)) return false;
        var warnings = new List<string>();
        ModelBuildResult r;
        try { r = SourceBuild.Run(projectDir, model, (ty, n) => Cel(assets, ty, n), warnings); }
        catch (Exception e) when (e is FormatException or IOException or InvalidOperationException or ArgumentException)
        {
            throw new ArgumentException("모델을 만들지 못했습니다: " + e.Message);
        }
        var errors = r.Findings.Where(f => f.Code == FindingCode.InputError).Select(f => (f.EntityId?.Contains('!') == true ? f.EntityId + " " : "") + f.Message).ToList();
        if (errors.Count > 0) throw new ArgumentException(ErrorText(errors));

        using var log = new StampedWriter(new StreamWriter(logPath, true, new UTF8Encoding(false)) { AutoFlush = true });
        log.WriteLine("모델 생성: 서버 v" + serverVersion);
        foreach (var w in warnings.Concat(r.Warnings).Distinct()) log.WriteLine("경고: " + w);
        return true;
    }

    internal static string ErrorText(IReadOnlyList<string> errors)
    {
        var sb = new StringBuilder($"입력 오류 {errors.Count}건");
        foreach (var e in errors.Take(MaxErrorLines)) sb.Append('\n').Append(e);
        if (errors.Count > MaxErrorLines) sb.Append($"\n외 {errors.Count - MaxErrorLines}건");
        return sb.ToString();
    }

    /// <summary>The plugin's pick (AssetCatalogClient.FetchCel): the asset of that name in the type first, else any type's; null when none or its file is gone.</summary>
    static string? Cel(AssetStore assets, string typeName, string name)
    {
        var m = assets.All().Where(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
        var a = m.FirstOrDefault(x => x.TypeName.Equals(typeName, StringComparison.OrdinalIgnoreCase)) ?? m.FirstOrDefault();
        if (a == null) return null;
        var p = assets.Path(a.Id, "asset.cel");
        return File.Exists(p) ? File.ReadAllText(p) : null;
    }
}
