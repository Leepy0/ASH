using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Core.Asy;
using ASH.Core.AutoMod;
using ASH.Core.Export;
using ASH.Core.Geometry;
using ASH.Core.Input;
using ASH.Core.Layout;

namespace ASH.Core.Model;

/// <summary>MaterialOverrides replaces Input.Materials. VehicleOverrides, when set, wins over AgvsOptions.VehicleOverrides.</summary>
public sealed record ModelBuildRequest(string ProjectDir, string ModelName, ProjectInput Input, Layout.Layout Layout, ExportOptions AgvsOptions,
    IReadOnlyList<MaterialInfo>? MaterialOverrides = null, IReadOnlyDictionary<string, VehicleTemplateOverride>? VehicleOverrides = null, bool AutoView = false);
/// <summary>Build is null when no AutoMod was given (generate only) or validation failed (ArcDir then has no files, Export is null).
/// Export is the agv.asy step: diff summary against the previous file and its backup.</summary>
public sealed record ModelBuildResult(string ArcDir, AmBuildResult? Build, List<Finding> Findings, List<string> Warnings, ExportResult? Export = null);

public static class ModelBuilder
{
    /// <summary>A name usable as the &lt;Model&gt;.arc/.dir folder name inside the project folder.</summary>
    public static bool IsValidModelName(string? n) =>
        !string.IsNullOrEmpty(n) && !n!.Contains("..") && n.IndexOfAny(new[] { '/', (char)92, ':' }) < 0 && LayoutChecker.IsValidName(n);

    public static ModelBuildResult Build(ModelBuildRequest r, string? amHome, TimeSpan timeout)
    {
        if (!IsValidModelName(r.ModelName)) throw new ArgumentException("invalid model name: " + r.ModelName);
        var arc = Path.Combine(r.ProjectDir, r.ModelName + ".arc");
        var l = r.Layout; var o = r.AgvsOptions; var input = r.Input;
        if (r.VehicleOverrides != null) o = o with { VehicleOverrides = r.VehicleOverrides };
        if (r.MaterialOverrides != null)
        {
            input = new ProjectInput { Settings = r.Input.Settings };
            input.AgvTypes.AddRange(r.Input.AgvTypes); input.Groups.AddRange(r.Input.Groups); input.Materials.AddRange(r.MaterialOverrides);
            input.Routes.AddRange(r.Input.Routes); input.Stations.AddRange(r.Input.Stations); input.Ext.AddRange(r.Input.Ext);
            input.Conveyors.AddRange(r.Input.Conveyors);
        }
        var findings = Validate(input, l, o.Scale);
        if (findings.Any(f => f.Code == FindingCode.InputError)) return new ModelBuildResult(arc, null, findings, Warn(findings));

        // supply-cycle check only on valid input; route lengths in meters (SpeedLoaded is m/s)
        string? lengthError = null;
        try
        {
            var len = Runner.GroupClusters.Build(l, input, ASH.Core.Traffic.TrafficTable.Build(l)).SupplyLength.ToDictionary(kv => kv.Key, kv => kv.Value * o.Scale);
            findings.AddRange(InputValidator.SupplyCapacity(input, len));
        }
        catch (Exception e) { lengthError = "경로 길이 계산 실패: " + e.Message; } // an optional warning must not stop the build

        var warnings = Warn(findings); if (lengthError != null) warnings.Add(lengthError);
        if (!string.Equals(input.Settings.Units.Trim(), o.Units.Trim(), StringComparison.OrdinalIgnoreCase))
            warnings.Add($"input.xlsx Settings의 Units({input.Settings.Units})는 쓰지 않습니다. 모델 단위는 ash.settings.tsv의 Units({o.Units})이고, 길이·속도 입력은 단위와 상관없이 m, m/s로 넣습니다.");
        var export = ExportRunner.Run(l, o, arc);
        warnings.AddRange(export.Warnings);
        // conveyors: their own movement system; the process logic moves the products into its stations
        var conv = Path.Combine(arc, ConveyorAsyWriter.SystemName + ".asy");
        if (l.Conveyors.Count > 0) ConveyorAsyWriter.WriteFile(l.Conveyors, input.Conveyors, o, conv);
        else if (File.Exists(conv)) File.Delete(conv);
        var standby = new Dictionary<string, string>();
        foreach (var c in l.Cps.Where(c => c.Type == "standby" && c.Group.Length > 0)) { if (!standby.ContainsKey(c.Group)) standby[c.Group] = c.Name; }
        // standby lanes too short for the group's vehicles to queue in (the queue would reach back into a junction or a block)
        warnings.AddRange(ASH.Core.Layout.StandbyQueueCheck.Check(l, input, o.Scale));
        // CP positions in model metres (as ArcLayout reads them back) for the deadlock rows
        var cpm = new Dictionary<string, Point2>();
        foreach (var c in l.Cps) { var q = o.ToModel(c.Position); cpm[c.Name] = new Point2(q.X / o.UnitsPerMeter, q.Y / o.UnitsPerMeter); }
        var info = new LayoutInfo(l.Cps.Select(c => c.Name).ToList(), l.Blocks.Select(b => b.Name).ToList(), standby, CpMetres: cpm);

        var agv = Path.Combine(arc, "agv.asy");
        var sys = AsyReader.ReadFile(agv);
        LogicGenerator.AddDispatchLists(sys, input, info);
        AsyWriter.WriteFile(sys, agv);

        var pm = LogicGenerator.BuildProcessModel(input, info, o.Units);
        Point2 Tx(Point2 p) => o.ToModel(p);
        foreach (var b in l.Blocks) pm.Blocks.Add(new BlockEntity(b.Name, b.Cap, new Rect2(Tx(b.Rect.Min), Tx(b.Rect.Max))));
        var ext = Path.Combine(arc, "user_ext.m");
        if (!File.Exists(ext)) File.WriteAllText(ext, LogicGenerator.DefaultUserExt, new UTF8Encoding(false));
        pm.UserExtSource = File.ReadAllText(ext);
        ProcessAsyWriter.WriteFile(pm, Path.Combine(arc, r.ModelName + "~.asy"));

        var pts = l.Paths.SelectMany(p => p.Segments).SelectMany(s => new[] { s.Start, s.End })
            .Concat(l.Cps.Select(c => c.Position)).Concat(l.Blocks.SelectMany(b => new[] { b.Rect.Min, b.Rect.Max }))
            .Concat(l.Conveyors.SelectMany(c => c.Segments).SelectMany(s => new[] { s.Start, s.End })).Select(Tx).ToList();
        double upm = o.UnitsPerMeter;
        var draw = pts.Count == 0 ? new Rect2(new Point2(-10 * upm, -10 * upm), new Point2(10 * upm, 10 * upm))
            : new Rect2(new Point2(pts.Min(p => p.X), pts.Min(p => p.Y)), new Point2(pts.Max(p => p.X), pts.Max(p => p.Y)));
        var pad = Math.Max(draw.Max.X - draw.Min.X, draw.Max.Y - draw.Min.Y) * 0.1;
        File.WriteAllText(Path.Combine(arc, "model.amo"),
            ModelAmoWriter.Render(r.ModelName, o.Units, input.Settings.SimLengthSec / 3600, (l.Conveyors.Count > 0 ? new[] { "agv", ConveyorAsyWriter.SystemName } : new[] { "agv" }), draw.Expand(Math.Max(pad, upm)), o.Version, r.AutoView));
        // 3D picture assets: static system ash_3d (none: a stale ash_3d.asy is removed); the build below starts from a fresh <Model>.dir, so a changed file is read
        Cel.AssetShapes.AddTo(arc, l.Assets.Select(a => Cel.AssetShapes.InMetres(a, o.Origin, o.Scale)), o.Units);
        ParamsFile.Write(input, new RunScenario(input.Settings.SeedBase, new Dictionary<string, int>()), Path.Combine(arc, "params.txt"));
        InputSignature.Write(arc, input); // ash-run refuses a model whose baked-in inputs differ from the current Excel

        AmBuildResult? build = null;
        if (amHome != null)
        {
            var am = AmBuilder.FindBuildExe(amHome);
            if (am == null)
                // not an exception: the model files are written and the user can fix AmHome and build again; <Model>.dir is made by AutoMod's build, so it is missing too
                warnings.Add($"AutoMod를 찾지 못해 빌드하지 않았습니다: {AmBuilder.BuildExeCandidates(amHome)} 이(가) 없습니다. 프로젝트 폴더의 ash.settings.tsv에서 AmHome을 AutoMod 설치 폴더로 고치세요. 모델 파일은 {arc}에 만들었습니다(<모델>.dir 폴더는 AutoMod가 빌드할 때 만듭니다).");
            else
            {
                PrepareBuildDir(r.ProjectDir, r.ModelName);
                try { build = AmBuilder.Build(amHome, r.ProjectDir, r.ModelName, timeout); }
                catch (System.ComponentModel.Win32Exception e) { throw new InvalidOperationException($"AutoMod({am})를 실행하지 못했습니다: {e.Message}", e); }
            }
        }
        return new ModelBuildResult(arc, build, findings, warnings, export);
    }

    /// <summary>The input checks Build runs first (any InputError stops it before a file is written); AGVSEND runs them without building.</summary>
    public static List<Finding> Validate(ProjectInput input, Layout.Layout l, double scale)
    {
        var findings = InputValidator.Validate(input, l.Cps.Select(c => c.Name).ToList());
        findings.AddRange(ConveyorCheck.Check(input, l, scale)); // Conveyors rows against the AGV_CONV polylines
        findings.AddRange(RouteGroupCheck.Check(input, l)); // Routes rows pointing at another group's CPs
        return findings;
    }

    /// <summary>One AGVS vehicle type per group, named after the group (the logic's vehicle procedures and dispatch filters are per AGVS type),
    /// with the numbers of the group's AGV_Types row: several groups may share a row.</summary>
    public static List<VehicleGroupSpec> VehicleGroups(ProjectInput input) => input.Groups.Select(g =>
    {
        var t = input.AgvTypes.FirstOrDefault(x => string.Equals(x.Type, g.Type, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"AGV_Groups {g.Group}: 유형 {g.Type}이 AGV_Types에 없습니다.");
        return new VehicleGroupSpec(g.Group, g.Group, g.MaxCount, t.SpeedLoaded, t.Length, t.Width, Accel: t.Accel, Decel: t.Decel, Kind: string.IsNullOrWhiteSpace(t.Kind) ? "Standard" : t.Kind, SpeedEmpty: t.SpeedEmpty,
            SpeedCurve: t.SpeedLoadedCurve ?? 0, SpeedEmptyCurve: t.SpeedEmptyCurve ?? 0, StandbyCps: g.StandbyCPs);
    }).ToList();

    static List<string> Warn(List<Finding> f) => f.Where(x => x.Code == FindingCode.InputWarning).Select(x => x.Message).ToList();

    /// <summary>A rebuild in the same folder does not relink unless the old &lt;Model&gt;.dir is gone.</summary>
    public static void PrepareBuildDir(string projectDir, string modelName)
    {
        var dir = Path.Combine(projectDir, modelName + ".dir");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
