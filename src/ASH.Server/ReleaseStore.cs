using System.Security.Cryptography;
using System.Text;
using ASH.Core.Update;

namespace ASH.Server;

/// <summary>Upload rejected; <see cref="Status"/> is the HTTP status to answer with.</summary>
public sealed class UploadException : Exception
{
    public int Status { get; }
    public UploadException(int status, string message) : base(message) { Status = status; }
}

public sealed record CurrentRelease(byte[] Json, ReleaseManifest Manifest);

/// <summary>
/// Release folder: current\ASH.json(.sig), files\&lt;setup&gt;, history\&lt;version&gt;\, incoming\&lt;guid&gt;\ (scratch, always removed).
/// An upload is fully validated in incoming\ first; only then the setup file moves to files\ and current\ is swapped.
/// </summary>
public sealed class ReleaseStore
{
    public const string HeaderName = "X-ASH-Admin-Token";
    private const long MaxManifestBytes = 1 << 20, MaxSignatureBytes = 4096;

    private readonly ILogger _log;
    private readonly byte[]? _token;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Test hook: called with "validated" and "moved" during an upload; a throw there simulates a mid-upload failure.</summary>
    internal Action<string>? Stage { get; set; }

    public string Dir { get; }
    public ManifestSignature? Key { get; }
    public bool UploadsEnabled => _token != null;
    public string CurrentDir => Path.Combine(Dir, "current");
    public string FilesDir => Path.Combine(Dir, "files");
    public string HistoryDir => Path.Combine(Dir, "history");

    /// <exception cref="ArgumentException">publicKeyPem is set but holds no valid P-256 key.</exception>
    public ReleaseStore(string dir, string? publicKeyPem, string? adminToken, ILogger log)
    {
        Dir = dir;
        _log = log;
        _token = string.IsNullOrEmpty(adminToken) ? null : Encoding.UTF8.GetBytes(adminToken);
        Key = string.IsNullOrWhiteSpace(publicKeyPem) ? null : new ManifestSignature(publicKeyPem);
        foreach (var d in new[] { CurrentDir, FilesDir, HistoryDir }) Directory.CreateDirectory(d);
        // leftovers of a crashed upload
        TryDelete(Path.Combine(Dir, "incoming"));
        if (Key == null) _log.LogWarning("서명 공개키가 없습니다: 업로드한 배포의 서명을 확인하지 않습니다.");
        else _log.LogInformation("서명 공개키 {KeyId}", Key.KeyId);
    }

    public static ReleaseStore Create(IConfiguration cfg, ILogger log)
    {
        var dir = cfg["Releases:Dir"];
        if (string.IsNullOrWhiteSpace(dir)) dir = "%ProgramData%\\ASH.Server\\releases";
        dir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir));
        // Update:PublicKeyPem (test/ops override): set = wins over the embedded key; set but empty = no key at all
        var pem = cfg["Update:PublicKeyPem"] ?? EmbeddedKey();
        return new ReleaseStore(dir, pem, cfg["Admin:Token"], log);
    }

    private static string? EmbeddedKey()
    {
        using var s = typeof(ReleaseStore).Assembly.GetManifestResourceStream("update-key.pub");
        if (s == null) return null;
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    public bool TokenMatches(string? given)
    {
        if (_token == null || given == null) return false;
        var g = Encoding.UTF8.GetBytes(given);
        return g.Length == _token.Length && CryptographicOperations.FixedTimeEquals(g, _token);
    }

    // FileShare.Delete: File.Replace on current\ must not fail because a download is reading it
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

    public byte[]? ReadManifestBytes() => ReadShared(Path.Combine(CurrentDir, ReleaseManifest.FileName));
    public byte[]? ReadSignatureBytes() => ReadShared(Path.Combine(CurrentDir, ReleaseManifest.SignatureFileName));

    public CurrentRelease? GetCurrent()
    {
        var json = ReadManifestBytes();
        if (json == null) return null;
        try { return new CurrentRelease(json, ReleaseManifest.Parse(json)); }
        catch (InvalidDataException e) { _log.LogError(e, "current\\ASH.json을 읽을 수 없습니다."); return null; }
    }

    /// <summary>Versions kept in history\, newest first.</summary>
    public IReadOnlyList<ReleaseManifest> History()
    {
        var list = new List<ReleaseManifest>();
        foreach (var d in Directory.GetDirectories(HistoryDir))
        {
            var b = ReadShared(Path.Combine(d, ReleaseManifest.FileName));
            if (b == null) continue;
            try { list.Add(ReleaseManifest.Parse(b)); } catch (InvalidDataException) { }
        }
        list.Sort((a, b) => SemVersion.TryParse(b.Version, out var vb) && SemVersion.TryParse(a.Version, out var va) ? vb!.CompareTo(va) : 0);
        return list;
    }

    public async Task<ReleaseManifest> PublishAsync(IFormFile manifestPart, IFormFile? signaturePart, IFormFile setupPart, bool force, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        var incoming = Path.Combine(Dir, "incoming", Guid.NewGuid().ToString("N"));
        string? movedNew = null; // setup file we put into files\ and must remove again if the upload fails later
        var committed = false;
        try
        {
            Directory.CreateDirectory(incoming);
            var json = await ReadSmall(manifestPart, MaxManifestBytes, "manifest");
            ReleaseManifest m;
            try { m = ReleaseManifest.Parse(json); }
            catch (InvalidDataException e) { throw new UploadException(400, e.Message); }
            if (!SemVersion.TryParse(m.Version, out var newVer)) throw new UploadException(400, "배포 정보의 version이 올바르지 않습니다: " + m.Version);

            var sig = signaturePart == null ? null : await ReadSmall(signaturePart, MaxSignatureBytes, "signature");
            if (Key != null)
            {
                if (sig == null) throw new UploadException(400, "이 서버는 서명된 배포만 받습니다(signature 없음).");
                if (!Key.Verify(json, sig)) throw new UploadException(400, "서명이 맞지 않습니다.");
            }
            else _log.LogWarning("서명 확인 없이 {Version} 업로드를 받습니다(공개키 없음).", m.Version);

            if (Path.GetFileName(setupPart.FileName) != m.File)
                throw new UploadException(400, $"설치 파일 이름({setupPart.FileName})이 배포 정보의 file({m.File})과 다릅니다.");
            var staged = Path.Combine(incoming, m.File);
            var (size, hash) = await CopyAndHash(setupPart, staged, ct);
            if (size != m.Size) throw new UploadException(400, $"설치 파일 크기가 다릅니다(받은 {size}, 배포 정보 {m.Size}).");
            if (hash != m.Sha256) throw new UploadException(400, "설치 파일 SHA-256이 배포 정보와 다릅니다.");

            var cur = GetCurrent();
            if (cur != null && !force && SemVersion.TryParse(cur.Manifest.Version, out var curVer) && newVer!.CompareTo(curVer) < 0)
                throw new UploadException(409, $"현재 게시된 버전({cur.Manifest.Version})보다 낮습니다. 되돌리려면 ?force=1");
            Stage?.Invoke("validated");

            var dest = Path.Combine(FilesDir, m.File);
            if (File.Exists(dest))
            {
                if (await HashFile(dest, ct) != m.Sha256) throw new UploadException(409, $"{m.File}이(가) 이미 있고 내용이 다릅니다. 버전을 올려서 다시 만드세요.");
                File.Move(staged, dest, overwrite: true);
            }
            else { File.Move(staged, dest); movedNew = dest; }
            Stage?.Invoke("moved");

            var sigPath = Path.Combine(CurrentDir, ReleaseManifest.SignatureFileName);
            var prevSig = cur == null ? null : ReadSignatureBytes();
            if (cur != null) Archive(cur, prevSig);
            try
            {
                if (sig != null) Swap(sigPath, sig); else File.Delete(sigPath);
                Swap(Path.Combine(CurrentDir, ReleaseManifest.FileName), json);
            }
            catch
            {
                // do not leave the old manifest next to the new signature
                try { if (prevSig != null) Swap(sigPath, prevSig); else File.Delete(sigPath); }
                catch (Exception e) { _log.LogError(e, "이전 서명 복원 실패"); }
                throw;
            }
            committed = true;
            _log.LogInformation("게시: {Version} ({File}, {Size} bytes, 서명 {Signed})", m.Version, m.File, m.Size, sig != null);
            return m;
        }
        finally
        {
            if (!committed && movedNew != null) try { File.Delete(movedNew); } catch (IOException) { }
            TryDelete(incoming);
            _gate.Release();
        }
    }

    private void Archive(CurrentRelease cur, byte[]? sig)
    {
        if (!SemVersion.TryParse(cur.Manifest.Version, out _)) { _log.LogWarning("이전 버전 이름이 이상해 history에 보관하지 않습니다: {V}", cur.Manifest.Version); return; }
        var d = Path.Combine(HistoryDir, cur.Manifest.Version);
        Directory.CreateDirectory(d);
        File.WriteAllBytes(Path.Combine(d, ReleaseManifest.FileName), cur.Json);
        var sp = Path.Combine(d, ReleaseManifest.SignatureFileName);
        if (sig != null) File.WriteAllBytes(sp, sig); else File.Delete(sp);
    }

    // temp file next to the target, then replace: readers see the old or the new file, never half of one
    private static void Swap(string dest, byte[] data)
    {
        var tmp = dest + ".tmp";
        File.WriteAllBytes(tmp, data);
        if (File.Exists(dest)) File.Replace(tmp, dest, null); else File.Move(tmp, dest);
    }

    private static async Task<byte[]> ReadSmall(IFormFile f, long max, string what)
    {
        if (f.Length > max) throw new UploadException(400, $"{what}이(가) 너무 큽니다.");
        using var ms = new MemoryStream();
        await using var s = f.OpenReadStream();
        await s.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static async Task<(long Size, string Hash)> CopyAndHash(IFormFile f, string path, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        var buf = new byte[81920];
        await using var src = f.OpenReadStream();
        await using var dst = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, buf.Length, useAsync: true);
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            sha.AppendData(buf, 0, n);
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            size += n;
        }
        return (size, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }

    private static async Task<string> HashFile(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
