using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using ASH.Core.Input;

namespace ASH.Core.Runner;

/// <summary>A file in a job package: path inside the zip ('/' separated), size, SHA-256 (lower-case hex).</summary>
public sealed record PackageFile(string Path, long Size, string Sha256);

/// <summary>
/// What the plugin sends to the server for a run: the model folder &lt;Model&gt;.arc (as AGVMODEL wrote it, without build output) and the cell snapshot
/// input.cells.tsv. The workbook itself is not sent: company document encryption leaves it readable by Excel only, and the snapshot is exactly what the model
/// was built from. The server builds the model itself, so no .exe or .dir is needed. The same rules check the zip on the server.
/// </summary>
public static class JobPackage
{
    public const long MaxEntryBytes = 512L * 1024 * 1024, MaxTotalBytes = 2L * 1024 * 1024 * 1024;
    public const int MaxEntries = 5000;

    /// <summary>Paths (inside the package) that must be there for a model name.</summary>
    public static IReadOnlyList<string> Required(string model) =>
        new[] { model + ".arc/agv.asy", model + ".arc/model.amo", model + ".arc/" + model + "~.asy", CellDump.FileName };

    /// <summary>Missing required files in a project folder, as package paths; empty = ready to send.</summary>
    public static IReadOnlyList<string> Missing(string projectDir, string model) =>
        Required(model).Where(p => !File.Exists(Path.Combine(projectDir, p.Replace('/', Path.DirectorySeparatorChar)))).ToList();

    /// <summary>Writes the package for a project folder to <paramref name="zipPath"/>: every file of &lt;Model&gt;.arc plus the snapshot (and ash.settings.tsv when
    /// present); with <paramref name="includeResults"/> also ash-run's results from runs\. Throws InvalidOperationException naming the missing files when a required one is not there.</summary>
    /// <summary>Missing files for a local result upload (ash-run's summary and search decision), as package paths; empty = ready.</summary>
    public static IReadOnlyList<string> MissingResults(string projectDir) =>
        new[] { "runs/summary.tsv", "runs/" + SearchSummaryFile.FileName }.Where(p => !File.Exists(Path.Combine(projectDir, p.Replace('/', Path.DirectorySeparatorChar)))).ToList();

    /// <summary>True when an unpacked package carries a local result (ash-run's runs folder) instead of asking the server to run.</summary>
    public static bool HasResults(string projectDir) => MissingResults(projectDir).Count == 0;

    public static IReadOnlyList<PackageFile> Create(string projectDir, string model, string zipPath, bool includeResults = false)
    {
        var missing = Missing(projectDir, model);
        if (missing.Count > 0) throw new InvalidOperationException("보낼 파일이 없습니다: " + string.Join(", ", missing));
        var files = new List<(string Rel, string Full)>();
        var arc = Path.Combine(projectDir, model + ".arc");
        foreach (var f in Directory.GetFiles(arc, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            if (Path.GetFileName(f).IndexOf(".asy.bak", StringComparison.OrdinalIgnoreCase) < 0) // ExportRunner's backups (<x>.asy.bak.<time>) stay at home
                files.Add((model + ".arc/" + f.Substring(arc.Length + 1).Replace(Path.DirectorySeparatorChar, '/'), f));
        foreach (var extra in new[] { CellDump.FileName, "ash.settings.tsv" })
        {
            var p = Path.Combine(projectDir, extra);
            if (File.Exists(p)) files.Add((extra, p));
        }
        if (includeResults)
        {
            // a local result: the summary, the decision and every run's result.tsv (the block maps); reports and AutoMod output stay at home
            var miss = MissingResults(projectDir);
            if (miss.Count > 0) throw new InvalidOperationException("ash-run 결과가 없습니다: " + string.Join(", ", miss));
            var runs = Path.Combine(projectDir, "runs");
            foreach (var name in new[] { "summary.tsv", "impact.tsv", SearchSummaryFile.FileName })
                if (File.Exists(Path.Combine(runs, name))) files.Add(("runs/" + name, Path.Combine(runs, name)));
            foreach (var d in Directory.GetDirectories(runs).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var r = Path.Combine(d, "result.tsv");
                if (File.Exists(r)) files.Add(("runs/" + Path.GetFileName(d) + "/result.tsv", r));
            }
        }
        var list = new List<PackageFile>();
        using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            foreach (var (rel, full) in files)
            {
                var e = zip.CreateEntry(rel, CompressionLevel.Optimal);
                using var src = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var dst = e.Open();
                src.CopyTo(dst);
                list.Add(new PackageFile(rel, src.Length, Sha256(full)));
            }
        return list;
    }

    static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>A safe relative path for an entry name, or null: no rooted path, drive, "..", empty segment or control character.</summary>
    public static string? SafePath(string entryName)
    {
        var n = entryName.Replace('\\', '/');
        if (n.Length == 0 || n.StartsWith("/") || n.Contains(":") || n.Any(c => c < 32)) return null;
        var parts = n.Split('/');
        if (parts.Any(p => p.Length == 0 || p == "." || p == "..")) return null;
        return n;
    }

    /// <summary>Unpacks a package into an empty folder after checking it: safe paths only, size limits, the required files for the model present.
    /// Throws InvalidDataException with the reason. Returns the files with their hashes.</summary>
    public static IReadOnlyList<PackageFile> Extract(string zipPath, string destDir, string model)
    {
        var list = new List<PackageFile>();
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(e => !e.FullName.EndsWith("/") && !e.FullName.EndsWith("\\")).ToList(); // folder entries carry nothing
        if (entries.Count > MaxEntries) throw new InvalidDataException($"파일이 너무 많습니다({entries.Count}개, 최대 {MaxEntries}).");
        long total = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var rel = SafePath(e.FullName) ?? throw new InvalidDataException("허용되지 않는 경로: " + e.FullName);
            if (!names.Add(rel)) throw new InvalidDataException("같은 경로가 두 번 있습니다: " + rel);
            if (e.Length > MaxEntryBytes) throw new InvalidDataException($"파일이 너무 큽니다: {rel}");
            total += e.Length;
            if (total > MaxTotalBytes) throw new InvalidDataException("압축을 푼 크기가 너무 큽니다.");
        }
        var missing = Required(model).Where(r => !names.Contains(r)).ToList();
        if (missing.Count > 0) throw new InvalidDataException("필수 파일이 없습니다: " + string.Join(", ", missing));
        Directory.CreateDirectory(destDir);
        var root = Path.GetFullPath(destDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var e in entries)
        {
            var rel = SafePath(e.FullName)!;
            var full = Path.GetFullPath(Path.Combine(destDir, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("허용되지 않는 경로: " + e.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            using (var src = e.Open())
            using (var dst = new FileStream(full, FileMode.CreateNew, FileAccess.Write))
            {
                // the declared length is checked above; copy at most that much more so a lying header cannot fill the disk
                var buf = new byte[81920]; long left = e.Length + 1; int n;
                while ((n = src.Read(buf, 0, (int)Math.Min(buf.Length, left))) > 0) { dst.Write(buf, 0, n); left -= n; if (left <= 0) throw new InvalidDataException("크기가 맞지 않는 파일: " + rel); }
            }
            list.Add(new PackageFile(rel, e.Length, Sha256(full)));
        }
        return list;
    }
}
