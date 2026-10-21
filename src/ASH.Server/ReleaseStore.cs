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
    public const int MinTokenLength = 16;
    private const long MaxManifestBytes = 1 << 20, MaxSignatureBytes = 4096;

    private readonly ILogger _log;
    private readonly byte[]? _token;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Test hook: called with "validated", "moved" and "sig-swapped" during an upload; a throw there simulates a mid-upload failure.</summary>
    internal Action<string>? Stage { get; set; }

    public string Dir { get; }
    public ManifestSignature? Key { get; }
    public bool UploadsEnabled => _token != null;
    /// <summary>Update:AllowUnsigned=true: a server without a public key still takes uploads (dev/test only).</summary>
    public bool AllowUnsigned { get; }
    public string CurrentDir => Path.Combine(Dir, "current");
    public string FilesDir => Path.Combine(Dir, "files");
    public string HistoryDir => Path.Combine(Dir, "history");

    /// <exception cref="ArgumentException">publicKeyPem is set but holds no valid P-256 key.</exception>
    public ReleaseStore(string dir, string? publicKeyPem, string? adminToken, ILogger log, bool allowUnsigned = false)
    {
        Dir = dir;
        AllowUnsigned = allowUnsigned;
        _log = log;
        if (!string.IsNullOrEmpty(adminToken) && adminToken.Length < MinTokenLength)
            _log.LogWarning("Admin:Token이 {Min}자 미만이라 업로드를 끕니다.", MinTokenLength);
        else if (!string.IsNullOrEmpty(adminToken)) _token = Encoding.UTF8.GetBytes(adminToken);
        Key = string.IsNullOrWhiteSpace(publicKeyPem) ? null : new ManifestSignature(publicKeyPem);
        foreach (var d in new[] { CurrentDir, FilesDir, HistoryDir }) Directory.CreateDirectory(d);
        // leftovers of a crashed upload
        TryDelete(Path.Combine(Dir, "incoming"));
        if (Key == null && AllowUnsigned) _log.LogWarning("서명 공개키가 없습니다: Update:AllowUnsigned=true라 서명을 확인하지 않고 업로드를 받습니다.");
        else if (Key == null) _log.LogWarning("서명 공개키가 없습니다: 업로드를 모두 거절합니다(Update:AllowUnsigned=true로만 허용).");
        else _log.LogInformation("서명 공개키 {KeyId}", Key.KeyId);
    }

    public static ReleaseStore Create(IConfiguration cfg, IHostEnvironment env, ILogger log)
    {
        var dir = cfg["Releases:Dir"];
        if (string.IsNullOrWhiteSpace(dir)) dir = "%ProgramData%\\ASH.Server\\releases";
        dir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir));
        var pem = ResolveKeyPem(cfg["Update:PublicKeyPem"], EmbeddedKey(), env.IsDevelopment(), log);
        return new ReleaseStore(dir, pem, cfg["Admin:Token"], log, cfg.GetValue<bool>("Update:AllowUnsigned"));
    }

    /// <summary>
    /// Update:PublicKeyPem is a test/dev override of the embedded key: when set it wins (a warning if it replaces an embedded key).
    /// An empty or whitespace-only override ("no key at all") only counts in Development, so a stray empty setting cannot switch signature checking off in production.
    /// </summary>
    internal static string? ResolveKeyPem(string? over, string? embedded, bool isDevelopment, ILogger log)
    {
        if (over == null) return embedded;
        if (string.IsNullOrWhiteSpace(over) && !isDevelopment)
        {
            log.LogWarning("Update:PublicKeyPem이 비어 있지만 개발 환경이 아니라 무시합니다.");
            return embedded;
        }
        if (embedded != null) log.LogWarning("Update:PublicKeyPem 설정이 내장 공개키(update-key.pub)를 대체합니다. 운영에서는 설정하지 마세요.");
        return over;
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
        var json = await ReadSmall(manifestPart, MaxManifestBytes, "manifest");
        var sig = signaturePart == null ? null : await ReadSmall(signaturePart, MaxSignatureBytes, "signature");
        await using var setup = setupPart.OpenReadStream();
        return await PublishAsync(json, sig, Path.GetFileName(setupPart.FileName), setup, force, ct);
    }

    /// <summary>Same checks as an upload; the setup stream is read only after the manifest and signature pass (used by the GitHub pull too).</summary>
    public async Task<ReleaseManifest> PublishAsync(byte[] json, byte[]? sig, string setupName, Stream setup, bool force, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        var incoming = Path.Combine(Dir, "incoming", Guid.NewGuid().ToString("N"));
        string? movedNew = null; // setup file we put into files\ and must remove again if the upload fails later
        var committed = false;
        try
        {
            if (Key == null && !AllowUnsigned) throw new UploadException(400, "서명 키 없음 — Update:AllowUnsigned=true로만 허용");
            Directory.CreateDirectory(incoming);
            if (json.Length > MaxManifestBytes) throw new UploadException(400, "manifest이(가) 너무 큽니다.");
            if (sig != null && sig.Length > MaxSignatureBytes) throw new UploadException(400, "signature이(가) 너무 큽니다.");
            ReleaseManifest m;
            try { m = ReleaseManifest.Parse(json); }
            catch (InvalidDataException e) { throw new UploadException(400, e.Message); }
            if (!SemVersion.TryParse(m.Version, out var newVer)) throw new UploadException(400, "배포 정보의 version이 올바르지 않습니다: " + m.Version);

            if (SemVersion.TryParse(m.MinSupported, out var minVer) && minVer!.CompareTo(newVer) > 0)
                throw new UploadException(400, $"minSupported({m.MinSupported})가 version({m.Version})보다 높습니다.");

            if (Key != null)
            {
                if (sig == null) throw new UploadException(400, "이 서버는 서명된 배포만 받습니다(signature 없음).");
                if (!Key.Verify(json, sig)) throw new UploadException(400, "서명이 맞지 않습니다.");
            }
            else _log.LogWarning("서명 확인 없이 {Version} 업로드를 받습니다(공개키 없음).", m.Version);

            if (setupName != m.File)
                throw new UploadException(400, $"설치 파일 이름({setupName})이 배포 정보의 file({m.File})과 다릅니다.");
            var staged = Path.Combine(incoming, m.File);
            var (size, hash) = await CopyAndHash(setup, staged, m.Size, ct);
            if (size != m.Size) throw new UploadException(400, $"설치 파일 크기가 다릅니다(받은 {size}, 배포 정보 {m.Size}).");
            if (hash != m.Sha256) throw new UploadException(400, "설치 파일 SHA-256이 배포 정보와 다릅니다.");

            var cur = GetCurrent();
            if (cur != null && !force && SemVersion.TryParse(cur.Manifest.Version, out var curVer) && newVer!.CompareTo(curVer) < 0)
                throw new UploadException(409, $"현재 게시된 버전({cur.Manifest.Version})보다 낮습니다. 되돌리려면 ?force=1");
            Stage?.Invoke("validated");

            var dest = Path.Combine(FilesDir, m.File);
            if (File.Exists(dest))
            {
                // same bytes already published: keep the file (it may be downloading right now)
                if (await HashFile(dest, ct) != m.Sha256) throw new UploadException(409, $"{m.File}이(가) 이미 있고 내용이 다릅니다. 버전을 올려서 다시 만드세요.");
            }
            else { File.Move(staged, dest); movedNew = dest; }
            Stage?.Invoke("moved");

            var sigPath = Path.Combine(CurrentDir, ReleaseManifest.SignatureFileName);
            var prevSig = cur == null ? null : ReadSignatureBytes();
            if (cur != null) Archive(cur, prevSig);
            try
            {
                if (sig != null) Swap(sigPath, sig); else File.Delete(sigPath);
                Stage?.Invoke("sig-swapped");
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

    // stops one byte past the expected size, so a wrong or endless source cannot fill the disk
    private static async Task<(long Size, string Hash)> CopyAndHash(Stream src, string path, long expected, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        var buf = new byte[81920];
        await using var dst = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, buf.Length, useAsync: true);
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            sha.AppendData(buf, 0, n);
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            size += n;
            if (size > expected) break;
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
