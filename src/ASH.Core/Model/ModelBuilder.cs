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

public sealed record ModelBuildRequest(string ProjectDir, string ModelName, ProjectInput Input, Layout.Layout Layout, ExportOptions AgvsOptions);
/// <summary>Build is null when no AutoMod was given (generate only) or validation failed (ArcDir then has no files).</summary>
public sealed record ModelBuildResult(string ArcDir, AmBuildResult? Build, List<Finding> Findings, List<string> Warnings);

public static class ModelBuilder
{
    public static ModelBuildResult Build(ModelBuildRequest r, string? amHome, TimeSpan timeout)
    {
        if (string.IsNullOrEmpty(r.ModelName) || r.ModelName.Contains("..") || r.ModelName.IndexOfAny(new[] { '/', (char)92, ':' }) >= 0
            || !LayoutChecker.IsValidName(r.ModelName)) throw new ArgumentException("invalid model name: " + r.ModelName);
        var arc = Path.Combine(r.ProjectDir, r.ModelName + ".arc");
        var l = r.Layout; var o = r.AgvsOptions; var input = r.Input;
        var findings = InputValidator.Validate(input, l.Cps.Select(c => c.Name).ToList());
        if (findings.Any(f => f.Code == FindingCode.InputError)) return new ModelBuildResult(arc, null, findings, Warn(findings));

        var warnings = Warn(findings); warnings.AddRange(ExportRunner.Run(l, o, arc).Warnings);
        var standby = new Dictionary<string, string>();
        foreach (var c in l.Cps.Where(c => c.Type == "standby" && c.Group.Length > 0)) { if (!standby.ContainsKey(c.Group)) standby[c.Group] = c.Name; }
        var info = new LayoutInfo(l.Cps.Select(c => c.Name).ToList(), l.Blocks.Select(b => b.Name).ToList(), standby);

        var agv = Path.Combine(arc, "agv.asy");
        var sys = AsyReader.ReadFile(agv);
        LogicGenerator.AddDispatchLists(sys, input, info);
        AsyWriter.WriteFile(sys, agv);

        var pm = LogicGenerator.BuildProcessModel(input, info, o.Units);
        Point2 Tx(Point2 p) => new Point2((p.X - o.Origin.X) * o.Scale, (p.Y - o.Origin.Y) * o.Scale);
        foreach (var b in l.Blocks) pm.Blocks.Add(new BlockEntity(b.Name, b.Cap, new Rect2(Tx(b.Rect.Min), Tx(b.Rect.Max))));
        var ext = Path.Combine(arc, "user_ext.m");
        if (!File.Exists(ext)) File.WriteAllText(ext, LogicGenerator.DefaultUserExt, new UTF8Encoding(false));
        pm.UserExtSource = File.ReadAllText(ext);
        ProcessAsyWriter.WriteFile(pm, Path.Combine(arc, r.ModelName + "~.asy"));

        var pts = l.Paths.SelectMany(p => p.Segments).SelectMany(s => new[] { s.Start, s.End })
            .Concat(l.Cps.Select(c => c.Position)).Concat(l.Blocks.SelectMany(b => new[] { b.Rect.Min, b.Rect.Max })).Select(Tx).ToList();
        var draw = pts.Count == 0 ? new Rect2(new Point2(-10, -10), new Point2(10, 10))
            : new Rect2(new Point2(pts.Min(p => p.X), pts.Min(p => p.Y)), new Point2(pts.Max(p => p.X), pts.Max(p => p.Y)));
        var pad = Math.Max(draw.Max.X - draw.Min.X, draw.Max.Y - draw.Min.Y) * 0.1;
        File.WriteAllText(Path.Combine(arc, "model.amo"),
            ModelAmoWriter.Render(r.ModelName, o.Units, input.Settings.SimLengthSec / 3600, new[] { "agv" }, draw.Expand(Math.Max(pad, 1)), o.Version));
        ParamsFile.Write(input, new RunScenario(input.Settings.SeedBase, new Dictionary<string, int>()), Path.Combine(arc, "params.txt"));

        AmBuildResult? build = null;
        if (amHome != null)
        {
            PrepareBuildDir(r.ProjectDir, r.ModelName);
            build = AmBuilder.Build(amHome, r.ProjectDir, r.ModelName, timeout);
        }
        return new ModelBuildResult(arc, build, findings, warnings);
    }

    static List<string> Warn(List<Finding> f) => f.Where(x => x.Code == FindingCode.InputWarning).Select(x => x.Message).ToList();

    /// <summary>A rebuild in the same folder does not relink unless the old &lt;Model&gt;.dir is gone.</summary>
    public static void PrepareBuildDir(string projectDir, string modelName)
    {
        var dir = Path.Combine(projectDir, modelName + ".dir");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
