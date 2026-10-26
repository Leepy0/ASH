using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASH.Core.Model;
using ASH.Core.Runner;
using ASH.Tests.Model;
using Xunit;

namespace ASH.Tests.Runner;

public class ResumeTests
{
    const string Model = "ashrun";
    static string Fx(string n) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "runner", n);

    sealed class Collect(List<RunOutcome> into) : IProgress<RunOutcome> { public void Report(RunOutcome v) { lock (into) into.Add(v); } }

    /// <summary>A fake AutoMod: each run leaves a valid result.tsv and report in runs\&lt;RunId&gt;, and is counted.</summary>
    static Func<RunRequest, RunOutcome> Runner(string runs, List<string> calls) => r =>
    {
        var id = ModelRunner.RunId(r.Input, r.Scenario);
        lock (calls) calls.Add(id);
        var dir = Path.Combine(runs, id);
        Directory.CreateDirectory(dir);
        File.Copy(Fx("result.tsv"), Path.Combine(dir, "result.tsv"), true);
        File.Copy(Fx(Model + ".report"), Path.Combine(dir, Model + ".report"), true);
        return new RunOutcome(id, true, dir, TimeSpan.FromSeconds(7), null, 0, null, Path.Combine(dir, "result.tsv"), Path.Combine(dir, Model + ".report"));
    };

    [Fact]
    public void A_Resumed_Search_Takes_Finished_Runs_Again_And_Runs_Only_The_Rest()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_resume_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, Model + ".exe"), "x"); // never started: the runner is faked
            var runs = Path.Combine(dir, "runs");
            var input = LogicGeneratorTests.Sample();
            RunScenario A = new(1, new Dictionary<string, int> { ["G1"] = 2 }), B = new(2, new Dictionary<string, int> { ["G1"] = 3 });

            // the first attempt made A and B; then B's result went missing (the run that failed or was cut off)
            var calls = new List<string>();
            var first = new ModelExecutor(new ExecutorOptions(dir, Model, 1, RunsDir: runs), input, Runner(runs, calls));
            first.Run(1, A); first.Run(1, B);
            Assert.Equal(2, calls.Count);
            var idB = ModelRunner.RunId(input, B);
            File.Delete(Path.Combine(runs, idB, "result.tsv"));

            // resume: summary.tsv keeps A only, A is taken again without running, B runs
            Assert.Equal(1, SearchJob.KeepReadableRuns(runs, Model));
            var rowsA = SummaryTsv.ReadAll(Path.Combine(runs, "summary.tsv"));
            Assert.All(rowsA, r => Assert.Equal(ModelRunner.RunId(input, A), r.RunId));
            calls.Clear();
            var seen = new List<RunOutcome>();
            var again = new ModelExecutor(new ExecutorOptions(dir, Model, 1, RunsDir: runs, Progress: new Collect(seen), Reuse: true), input, Runner(runs, calls));
            var kA = again.Run(1, A);
            Assert.Empty(calls); Assert.Equal(1, again.Reused);
            Assert.Equal(first.Run(1, A).Select(k => k.Utilization), kA.Select(k => k.Utilization)); // the same numbers as the run itself
            calls.Clear();
            again.Run(1, B);
            Assert.Equal(new[] { idB }, calls);
            Assert.Equal(2, seen.Count); // progress counts the reused run too
            // A's rows are not written twice by the resumed run (the extra first.Run above appended its own, as a fresh run does)
            Assert.Equal(rowsA.Count * 2, SummaryTsv.ReadAll(Path.Combine(runs, "summary.tsv")).Count(r => r.RunId == ModelRunner.RunId(input, A)));
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Without_Reuse_Nothing_Is_Taken_From_Earlier_Runs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ash_resume_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, Model + ".exe"), "x");
            var runs = Path.Combine(dir, "runs");
            var calls = new List<string>();
            var s = new RunScenario(1, new Dictionary<string, int> { ["G1"] = 2 });
            new ModelExecutor(new ExecutorOptions(dir, Model, 1, RunsDir: runs), LogicGeneratorTests.Sample(), Runner(runs, calls)).Run(1, s);
            new ModelExecutor(new ExecutorOptions(dir, Model, 1, RunsDir: runs), LogicGeneratorTests.Sample(), Runner(runs, calls)).Run(1, s);
            Assert.Equal(2, calls.Count); // a changed model in the same folder must never reuse old results
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }
}
