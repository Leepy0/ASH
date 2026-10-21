using ASH.Core.Update;

namespace ASH.Server;

/// <summary>
/// The newest signed ASH.Runner zip: &lt;Releases:Dir&gt;\runner\current\ASH.Runner.json(.sig) and runner\files\ASH-Runner-&lt;ver&gt;-win-x64.zip.
/// ash-run on the analysis PC asks /api/ash/runner for the manifest, checks the signature itself, then downloads the zip from /download.
/// The server also verifies before it stores, so only signed content is ever served. Fed by the GitHub pull.
/// </summary>
public sealed class RunnerStore
{
    private const long MaxManifestBytes = 1 << 16, MaxSignatureBytes = 4096;
    private const int KeepZips = 2; // newest + one older, for a manual rollback

    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string Dir { get; }
    public ManifestSignature? Key { get; }
    public string CurrentDir => Path.Combine(Dir, "current");
    public string FilesDir => Path.Combine(Dir, "files");

    public RunnerStore(string releasesDir, ManifestSignature? key, ILogger log)
    {
        Dir = Path.Combine(releasesDir, "runner");
        Key = key;
        _log = log;
        Directory.CreateDirectory(CurrentDir);
        Directory.CreateDirectory(FilesDir);
        TryDelete(Path.Combine(Dir, "incoming"));
    }

    // FileShare.Delete: a swap of current\ must not fail because a client is reading it
    private static byte[]? ReadShared(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var b = new byte[fs.Length];
            fs.ReadExactly(b);
            return b;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public byte[]? ReadManifestBytes() => ReadShared(Path.Combine(CurrentDir, PackageManifest.RunnerManifestName));
    public byte[]? ReadSignatureBytes() => ReadShared(Path.Combine(CurrentDir, PackageManifest.RunnerSignatureName));

    /// <summary>The published runner, or null when none (or the stored manifest is unreadable).</summary>
    public PackageManifest? Current
    {
        get
        {
            var b = ReadManifestBytes();
            if (b == null) return null;
            try { return PackageManifest.ParseRunner(b); }
            catch (InvalidDataException e) { _log.LogError(e, "runner\\current\\ASH.Runner.json을 읽을 수 없습니다."); return null; }
        }
    }

    /// <summary>
    /// Verifies the signature, the file name, the size and the SHA-256, then stores the zip and makes it current.
    /// A version that is not newer than the current one is rejected (no downgrade). Throws <see cref="UploadException"/> with the reason.
    /// </summary>
    public async Task<PackageManifest> PublishAsync(byte[] json, byte[]? sig, string fileName, Stream zip, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        var incoming = Path.Combine(Dir, "incoming", Guid.NewGuid().ToString("N"));
        string? moved = null;
        var committed = false;
        try
        {
            if (Key == null) throw new UploadException(400, "서명 키 없음 — Runner 배포는 서명된 것만 받습니다.");
            if (json.Length > MaxManifestBytes || (sig != null && sig.Length > MaxSignatureBytes)) throw new UploadException(400, "Runner 배포 정보가 너무 큽니다.");
            if (sig == null || !Key.Verify(json, sig)) throw new UploadException(400, "Runner 배포 서명이 맞지 않습니다.");
            PackageManifest m;
            try { m = PackageManifest.ParseRunner(json); }
            catch (InvalidDataException e) { throw new UploadException(400, e.Message); }
            if (fileName != m.File) throw new UploadException(400, $"Runner zip 이름({fileName})이 배포 정보의 file({m.File})과 다릅니다.");
            var cur = Current;
            if (cur != null && !m.IsNewerThan(cur.Version)) throw new UploadException(409, $"Runner {m.Version}은(는) 현재 게시된 {cur.Version}보다 새롭지 않습니다.");

            Directory.CreateDirectory(incoming);
            var staged = Path.Combine(incoming, m.File);
            long size; string hash;
            await using (var dst = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                (size, hash) = await HashedCopy.RunAsync(zip, dst, m.Size, ct);
            if (size != m.Size) throw new UploadException(400, $"Runner zip 크기가 다릅니다(받은 {size}, 배포 정보 {m.Size}).");
            if (hash != m.Sha256) throw new UploadException(400, "Runner zip SHA-256이 배포 정보와 다릅니다.");

            var dest = Path.Combine(FilesDir, m.File);
            if (File.Exists(dest)) File.Delete(dest); // an unreferenced leftover of the same name (a crashed publish)
            File.Move(staged, dest);
            moved = dest;

            // signature first: a client that reads between the two swaps gets a new signature with the old manifest, fails verification and retries later
            Swap(Path.Combine(CurrentDir, PackageManifest.RunnerSignatureName), sig);
            Swap(Path.Combine(CurrentDir, PackageManifest.RunnerManifestName), json);
            committed = true;
            Prune(m.File);
            _log.LogInformation("Runner 게시: {Version} ({File}, {Size} bytes)", m.Version, m.File, m.Size);
            return m;
        }
        finally
        {
            if (!committed && moved != null) try { File.Delete(moved); } catch (IOException) { }
            TryDelete(incoming);
            _gate.Release();
        }
    }

    // keeps the newest KeepZips zips (by version); the current one is always among them
    private void Prune(string currentFile)
    {
        try
        {
            var zips = Directory.GetFiles(FilesDir, "ASH-Runner-*-win-x64.zip")
                .Select(Path.GetFileName).Where(f => PackageManifest.IsRunnerFileName(f)).Select(f => f!).ToList();
            zips.Sort((a, b) => Version(b).CompareTo(Version(a)));
            foreach (var f in zips.Skip(KeepZips).Where(f => f != currentFile))
                try { File.Delete(Path.Combine(FilesDir, f)); } catch (IOException) { }
        }
        catch (IOException) { }
    }

    private static readonly SemVersion Zero = SemVersion.TryParse("0.0.0", out var z) ? z! : throw new InvalidOperationException();

    private static SemVersion Version(string file)
    {
        var v = file["ASH-Runner-".Length..^"-win-x64.zip".Length];
        return SemVersion.TryParse(v, out var s) ? s! : Zero;
    }

    private static void Swap(string dest, byte[] data)
    {
        var tmp = dest + ".tmp";
        File.WriteAllBytes(tmp, data);
        if (File.Exists(dest)) File.Replace(tmp, dest, null); else File.Move(tmp, dest);
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
