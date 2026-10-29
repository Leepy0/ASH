using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Core.AutoMod;
using ASH.Core.Input;
using ASH.Core.Layout;
using ASH.Core.Model;

namespace ASH.Core.Runner;

/// <summary>
/// One model per cycle-group count. A cycle group's vehicles never park, so an unused one would stand on the lane into its load CP and block it:
/// each count runs in a model built with MinCount = MaxCount = that count (&lt;Model&gt;_c&lt;n&gt;[x&lt;n&gt;…], made from the source files the way the server
/// makes a model, with the base model's pictures and hooks). A project without cycle groups runs its base model as before.
/// </summary>
public static class ModelVariants
{
    /// <summary>One lock per project folder: AutoMod builds in one folder share files there (am2err.dat), so they run one at a time.</summary>
    static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary><paramref name="model"/> without cycle groups, else model + "_c" + the cycle groups' counts in group order joined by "x" (Agv_c3, Agv_c3x2);
    /// a group missing from the scenario counts as its MinCount, as the run id does.</summary>
    public static string NameFor(ProjectInput input, string model, RunScenario s)
    {
        var n = input.Groups.Where(g => g.IsCycle).Select(g => ModelRunner.Count(g, s)).ToList();
        return n.Count == 0 ? model : model + "_c" + string.Join("x", n);
    }

    /// <summary>The report in a run folder: &lt;baseModel&gt;.report, else the one of a cycle group's count model (&lt;baseModel&gt;_c*.report); null when neither is there.</summary>
    public static string? ReportPath(string runDir, string baseModel)
    {
        var rep = Path.Combine(runDir, baseModel + ".report");
        if (File.Exists(rep)) return rep;
        return Directory.Exists(runDir) ? Directory.GetFiles(runDir, baseModel + "_c*.report").FirstOrDefault() : null;
    }

    /// <summary>Makes and builds &lt;projectDir&gt;\&lt;variant&gt; once; a build newer than the source files is taken as it is. <paramref name="build"/>(dir, model)
    /// replaces AutoMod's build (the server's wrapper); without it <paramref name="amHome"/> is needed. Throws InvalidOperationException (Korean) when the
    /// source files are missing, the input has errors, no AutoMod is given or the build fails: never the base model instead.</summary>
    public static void Ensure(string projectDir, string baseModel, string variant, ProjectInput input, RunScenario s, string? amHome, TimeSpan timeout,
        Func<string, string, AmBuildResult>? build = null)
    {
        // shortcut: an in-process lock; two processes (two ash-run on one folder) could still build the same variant at once
        lock (Locks.GetOrAdd(Path.GetFullPath(projectDir), _ => new object()))
        {
            if (Built(projectDir, baseModel, variant)) return;
            if (build == null && amHome == null)
                throw new InvalidOperationException($"AutoMod 설치 폴더(AmHome)가 설정되지 않아 순환 그룹 대수별 모델 {variant}을(를) 빌드할 수 없습니다.");
            Generate(projectDir, baseModel, variant, input, s);
            ModelBuilder.PrepareBuildDir(projectDir, variant);
            var b = build != null ? build(projectDir, variant) : AmBuilder.Build(amHome!, projectDir, variant, timeout);
            if (!b.Success)
            {
                var err = (b.MakeErrText + "\n" + b.Am2ErrText).Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
                throw new InvalidOperationException($"순환 그룹 대수별 모델 {variant}을(를) 빌드하지 못했습니다: " + (err != null ? err + " · " : "") + b.Facts);
            }
        }
    }

    /// <summary>&lt;variant&gt;.arc in <paramref name="projectDir"/> from its source files (layout.tsv, input.cells.tsv, ash.settings.tsv) with each cycle group's
    /// MinCount = MaxCount = its count in <paramref name="s"/>, the vehicle pictures from &lt;baseModel&gt;.arc\assets and its user_ext.m. No build.</summary>
    public static void Generate(string projectDir, string baseModel, string variant, ProjectInput input, RunScenario s)
    {
        var missing = JobPackage.RequiredSource.Where(p => !File.Exists(Path.Combine(projectDir, p))).ToList();
        if (missing.Count > 0) throw new InvalidOperationException("순환 그룹 대수별 모델을 만들 원본이 없습니다: " + string.Join(", ", missing));
        var counts = input.Groups.Where(g => g.IsCycle).ToDictionary(g => g.Group, g => ModelRunner.Count(g, s));
        string baseArc = Path.Combine(projectDir, baseModel + ".arc"), arc = Path.Combine(projectDir, variant + ".arc");
        var ext = Path.Combine(baseArc, "user_ext.m"); // the project's hooks; the generator writes the default one only when there is none
        if (File.Exists(ext) && !string.Equals(baseArc, arc, StringComparison.OrdinalIgnoreCase)) { Directory.CreateDirectory(arc); File.Copy(ext, Path.Combine(arc, "user_ext.m"), true); }
        string? Cel(string type, string name) { var f = Path.Combine(baseArc, "assets", SourceBuild.CelFile(name)); return File.Exists(f) ? File.ReadAllText(f) : null; }
        var r = SourceBuild.Run(projectDir, variant, Cel, new List<string>(), p =>
        {
            for (int i = 0; i < p.Groups.Count; i++)
                if (counts.TryGetValue(p.Groups[i].Group, out var n)) p.Groups[i] = p.Groups[i] with { MinCount = n, MaxCount = n };
            return p;
        });
        var errors = r.Findings.Where(f => f.Code == FindingCode.InputError).Select(f => f.Message).ToList();
        if (errors.Count > 0) throw new InvalidOperationException($"순환 그룹 대수별 모델 {variant}을(를) 만들지 못했습니다: " + string.Join(" / ", errors.Take(3)));
    }

    /// <summary>Built (AmBuilder.IsBuilt) after the source files and the base hooks were last written: a later AGVMODEL makes it again.</summary>
    static bool Built(string projectDir, string baseModel, string variant)
    {
        if (!AmBuilder.IsBuilt(projectDir, variant)) return false;
        var made = File.GetLastWriteTimeUtc(Path.Combine(projectDir, variant + ".arc", "model.amo"));
        return JobPackage.RequiredSource.Select(p => Path.Combine(projectDir, p)).Concat(new[] { Path.Combine(projectDir, baseModel + ".arc", "user_ext.m") })
            .All(f => !File.Exists(f) || File.GetLastWriteTimeUtc(f) <= made);
    }
}
