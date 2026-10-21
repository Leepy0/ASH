using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ASH.Core.Runner;

/// <summary>What a search decided, as runs\search.tsv: written by every ash-run / server search, read by the server when a local result is uploaded.</summary>
public sealed record SearchSummary(int ExitCode, string? Error, IReadOnlyDictionary<string, int> Decided, IReadOnlyList<IReadOnlyList<string>> Clusters,
    IReadOnlyList<string> ConfirmRunIds, IReadOnlyList<string> Warnings, IReadOnlyList<string> FailureMessages, int Runs, int Failures, double ElapsedSec);

/// <summary>Key-value lines (key TAB value...), one fact per line, so a torn or older file still reads what it has.</summary>
public static class SearchSummaryFile
{
    public const string FileName = "search.tsv";
    const string Header = "#ASH-SEARCH 1";

    static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

    public static void Write(string path, SearchSummary s)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(Header + "\n");
        sb.Append("exit\t").Append(s.ExitCode.ToString(inv)).Append('\n');
        if (s.Error != null) sb.Append("error\t").Append(Clean(s.Error)).Append('\n');
        sb.Append("runs\t").Append(s.Runs.ToString(inv)).Append('\n');
        sb.Append("failures\t").Append(s.Failures.ToString(inv)).Append('\n');
        sb.Append("elapsedSec\t").Append(s.ElapsedSec.ToString("0.#", inv)).Append('\n');
        foreach (var kv in s.Decided) sb.Append("decided\t").Append(Clean(kv.Key)).Append('\t').Append(kv.Value.ToString(inv)).Append('\n');
        foreach (var c in s.Clusters) sb.Append("cluster\t").Append(string.Join("\t", c.Select(Clean))).Append('\n');
        foreach (var r in s.ConfirmRunIds) sb.Append("confirm\t").Append(Clean(r)).Append('\n');
        foreach (var w in s.Warnings) sb.Append("warning\t").Append(Clean(w)).Append('\n');
        foreach (var f in s.FailureMessages) sb.Append("failure\t").Append(Clean(f)).Append('\n');
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>Throws InvalidDataException when the file is not a search summary.</summary>
    public static SearchSummary Read(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0 || lines[0].TrimEnd('\r') != Header) throw new InvalidDataException(path + ": 대수 탐색 결과 파일이 아닙니다.");
        int exit = 1, runs = 0, failures = 0; double elapsed = 0; string? error = null;
        var decided = new Dictionary<string, int>(StringComparer.Ordinal);
        var clusters = new List<IReadOnlyList<string>>(); var confirm = new List<string>(); var warnings = new List<string>(); var fails = new List<string>();
        int I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        foreach (var raw in lines.Skip(1))
        {
            var t = raw.TrimEnd('\r').Split('\t');
            if (t.Length < 2) continue;
            switch (t[0])
            {
                case "exit": exit = I(t[1]); break;
                case "error": error = t[1]; break;
                case "runs": runs = I(t[1]); break;
                case "failures": failures = I(t[1]); break;
                case "elapsedSec": double.TryParse(t[1], NumberStyles.Float, CultureInfo.InvariantCulture, out elapsed); break;
                case "decided": if (t.Length >= 3) decided[t[1]] = I(t[2]); break;
                case "cluster": clusters.Add(t.Skip(1).Where(x => x.Length > 0).ToList()); break;
                case "confirm": confirm.Add(t[1]); break;
                case "warning": warnings.Add(t[1]); break;
                case "failure": fails.Add(t[1]); break;
            }
        }
        return new SearchSummary(exit, error, decided, clusters, confirm, warnings, fails, runs, failures, elapsed);
    }

    /// <summary>The summary of a finished search job: decided counts, clusters and the run ids of the last confirm round.</summary>
    public static SearchSummary From(SearchJobResult r, bool debug = false)
    {
        var confirm = new List<string>();
        if (r.Search != null && r.Input != null)
            foreach (var t in r.Search.Trace.Where(t => t.Phase == "confirm").Reverse().Take(1))
                confirm.AddRange(t.Seeds.Select(seed => ModelRunner.RunId(r.Input, t.Scenario with { Seed = seed, Debug = debug })));
        var warnings = r.Warnings.Concat((IEnumerable<string>?)r.Search?.Warnings ?? Array.Empty<string>()).Distinct().ToList();
        return new SearchSummary(r.ExitCode, r.Error, r.Search?.Decided ?? new Dictionary<string, int>(), r.Clusters?.Clusters ?? new List<IReadOnlyList<string>>(),
            confirm, warnings, r.FailureMessages.Take(50).ToList(), r.Runs, r.Failures, r.Elapsed.TotalSeconds);
    }
}
