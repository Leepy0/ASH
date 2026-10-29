using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Core.Cel;
using ASH.Core.Export;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Runner;
using ASH.Core.Settings;

namespace ASH.Core.Model;

/// <summary>
/// The model from a source package (layout.tsv, input.cells.tsv, ash.settings.tsv) the way AGVMODEL makes it from the drawing and the workbook, so the server's
/// generator is the one that counts. AGVMODEL uses <see cref="Options"/> and <see cref="Overrides"/> too.
/// </summary>
public static class SourceBuild
{
    /// <summary>Builds &lt;model&gt;.arc in <paramref name="projectDir"/> (no AutoMod build). <paramref name="cel"/>(typeName, assetName) gives a vehicle asset's .cel text,
    /// null = no such asset; a null callback = no asset store. Excel reader and asset warnings go to <paramref name="warnings"/>, the rest is in the result
    /// (InputError findings = no files). A file that cannot be read throws (FormatException, IOException, InvalidOperationException, ArgumentException, ExportException).
    /// <paramref name="adjust"/> changes the input read before the model is made (ModelVariants: a cycle group's count).</summary>
    public static ModelBuildResult Run(string projectDir, string model, Func<string, string, string?>? cel, List<string> warnings, Func<ProjectInput, ProjectInput>? adjust = null)
    {
        if (!ModelBuilder.IsValidModelName(model)) throw new ArgumentException("invalid model name: " + model); // before the assets go into its .arc
        ProjectInput input;
        using (var wb = CellDump.Load(Path.Combine(projectDir, CellDump.FileName)))
        {
            input = ExcelReader.Read(wb, out var xw);
            warnings.AddRange(xw.Select(f => f.Message));
        }
        if (adjust != null) input = adjust(input);
        var s = SettingsFile.Load(Path.Combine(projectDir, JobPackage.SettingsInPackage));
        var layout = LayoutSnapshot.Read(Path.Combine(projectDir, LayoutSnapshot.FileName));
        var ov = Overrides(input, Path.Combine(projectDir, model + ".arc"), cel, warnings);
        return ModelBuilder.Build(new ModelBuildRequest(projectDir, model, input, layout, Options(input, s), VehicleOverrides: ov.Count > 0 ? ov : null), null, TimeSpan.Zero);
    }

    /// <summary>Units, origin and scale from ash.settings.tsv; the groups from Excel's AGV_Groups, else the settings' Groups.</summary>
    public static ExportOptions Options(ProjectInput input, ProjectSettings s) =>
        new ExportOptions(Units: s.Units, Origin: s.Origin, Scale: s.Scale, Groups: input.Groups.Count == 0 ? SettingsFile.ParseGroups(s.Groups) : ModelBuilder.VehicleGroups(input));

    /// <summary>Vehicle pictures by group for the AGV_Types rows with an Asset (see AssetOverrides.Build: a failure is a warning and the default picture). A usable .cel
    /// (at least one box) is kept as &lt;arcDir&gt;\assets\&lt;name&gt;.cel, so the package carries it. <paramref name="cel"/> may throw what AssetOverrides.Build handles.</summary>
    public static Dictionary<string, VehicleTemplateOverride> Overrides(ProjectInput input, string arcDir, Func<string, string, string?>? cel, List<string> warnings)
    {
        if (!input.AgvTypes.Any(t => t.Asset.Length > 0)) return new Dictionary<string, VehicleTemplateOverride>();
        if (cel == null) { warnings.Add("에셋 저장소가 없어 에셋 그림을 쓰지 않습니다. 기본 그림을 씁니다."); return new Dictionary<string, VehicleTemplateOverride>(); }
        return AssetOverrides.ForGroups(AssetOverrides.Build(input.AgvTypes, AssetOverrides.VehSegTemplate(), (ty, n) =>
        {
            var text = cel(ty, n);
            // the reason becomes the warning ("타입 X: 에셋 'n' - <why>. 기본 그림을 씁니다.")
            if (text == null) throw new IOException("카탈로그에 없는 이름입니다");
            try { if (CelReader.ReadBoxes(text).Count == 0) throw new IOException("에셋 파일에 상자가 없습니다"); }
            catch (FormatException) { throw new IOException("에셋 파일을 읽을 수 없습니다"); } // never ship a bad file in the package
            var dir = Path.Combine(arcDir, "assets");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, CelFile(n)), text, new UTF8Encoding(false));
            return text;
        }, warnings), input.Groups);
    }

    /// <summary>The file name an asset's picture is kept under in &lt;arcDir&gt;\assets.</summary>
    internal static string CelFile(string name) => string.Concat(name.Select(c => Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c)) + ".cel";
}
