using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using ASH.Core.Update;

namespace AshRelease;

/// <summary>Release PC tool: keygen / manifest / sign / verify / check-resource. Exit codes: 0 ok, 1 error.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        return Run(args, Console.Out, Console.Error);
    }

    public const string PrivateKeyName = "ash-signing.pem";
    public const string PublicKeyName = "update-key.pub";

    public static int Run(string[] args, TextWriter o, TextWriter e)
    {
        try
        {
            if (args.Length == 0) return Usage(e);
            var opt = Options(args.Skip(1).ToArray());
            switch (args[0])
            {
                case "keygen": return KeyGen(opt, o);
                case "manifest": return Manifest(opt, o);
                case "sign": return Sign(opt, o);
                case "verify": return Verify(opt, o, e);
                case "check-resource": return CheckResource(opt, o, e);
                case "notes": return Notes(opt, o, e);
                default: return Usage(e);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or CryptographicException or InvalidDataException)
        {
            e.WriteLine("오류: " + ex.Message);
            return 1;
        }
    }

    private static int Usage(TextWriter e)
    {
        e.WriteLine("사용법:");
        e.WriteLine("  AshRelease keygen   --out <폴더>");
        e.WriteLine("  AshRelease manifest --file <Setup.exe> --version <v> --min <v> [--notes <파일>] [--rid win-x64] --out <ASH.json>");
        e.WriteLine("  AshRelease sign     --key <ash-signing.pem> --in <ASH.json>      (-> <ASH.json>.sig)");
        e.WriteLine("  AshRelease verify   --pub <update-key.pub> --in <ASH.json>");
        e.WriteLine("  AshRelease check-resource --dll <어셈블리> --name <리소스 이름>      (없으면 종료 코드 1)");
        e.WriteLine("  AshRelease notes    --dir <release-notes> [--version <v>] --out <ASH.Notes.json>   (<v>.md가 없으면 경고만)");
        return 1;
    }

    private static Dictionary<string, string> Options(string[] a)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--") || i + 1 >= a.Length) throw new ArgumentException("옵션 형식 오류: " + a[i]);
            d[a[i].Substring(2)] = a[++i];
        }
        return d;
    }

    private static string Req(Dictionary<string, string> opt, string k) =>
        opt.TryGetValue(k, out var v) && v.Length > 0 ? v : throw new ArgumentException($"--{k} 가 필요합니다.");

    private static int KeyGen(Dictionary<string, string> opt, TextWriter o)
    {
        string dir = Req(opt, "out");
        string priv = Path.Combine(dir, PrivateKeyName), pub = Path.Combine(dir, PublicKeyName);
        if (File.Exists(priv) || File.Exists(pub)) throw new IOException($"이미 있는 키 파일은 덮어쓰지 않습니다: {dir}");
        Directory.CreateDirectory(dir);
        using var k = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string pubPem = k.ExportSubjectPublicKeyInfoPem();
        File.WriteAllText(priv, k.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(pub, pubPem);
        o.WriteLine($"개인키: {priv}  (릴리스 PC 밖으로 내보내거나 커밋하지 마세요)");
        o.WriteLine($"공개키: {pub}  (저장소 루트 update-key.pub 로 복사)");
        o.WriteLine("키 ID: " + new ManifestSignature(pubPem).KeyId);
        return 0;
    }

    private static int Manifest(Dictionary<string, string> opt, TextWriter o)
    {
        string file = Req(opt, "file"), version = Req(opt, "version"), min = Req(opt, "min"), outPath = Req(opt, "out");
        if (!SemVersion.TryParse(version, out var ver)) throw new ArgumentException("--version 이 SemVer(MAJOR.MINOR.PATCH[-pre])가 아닙니다: " + version);
        if (!SemVersion.TryParse(min, out var minV)) throw new ArgumentException("--min 이 SemVer가 아닙니다: " + min);
        if (minV!.CompareTo(ver) > 0) throw new ArgumentException($"--min {min} 이 --version {version} 보다 높습니다.");
        if (!ReleaseManifest.IsValidFileName(Path.GetFileName(file))) throw new ArgumentException("설치 파일 이름은 ASH-Setup-<버전>.exe 형식이어야 합니다: " + Path.GetFileName(file));
        if (!File.Exists(file)) throw new FileNotFoundException("설치 파일이 없습니다: " + file);
        string notes = opt.TryGetValue("notes", out var np) ? File.ReadAllText(np).Trim() : "";
        using var fs = File.OpenRead(file);
        string sha = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
        var m = new ReleaseManifest
        {
            Version = version,
            MinSupported = min,
            Rid = opt.TryGetValue("rid", out var rid) ? rid : "win-x64",
            File = Path.GetFileName(file),
            Sha256 = sha,
            Size = fs.Length,
            Published = ReleaseManifest.FormatPublished(DateTime.UtcNow),
            Notes = notes,
        };
        File.WriteAllBytes(outPath, m.ToJson());
        o.WriteLine($"배포 정보 작성: {outPath} (버전 {version}, 최소 {min}, {m.File}, {m.Size:N0}바이트)");
        return 0;
    }

    private static int Sign(Dictionary<string, string> opt, TextWriter o)
    {
        string keyPath = Req(opt, "key"), inPath = Req(opt, "in");
        using var k = ECDsa.Create();
        k.ImportFromPem(File.ReadAllText(keyPath));
        if (k.ExportParameters(false).Curve.Oid?.Value != "1.2.840.10045.3.1.7") throw new ArgumentException("P-256(prime256v1) 개인키가 아닙니다.");
        byte[] data = File.ReadAllBytes(inPath);
        byte[] sig = k.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        if (!new ManifestSignature(k.ExportSubjectPublicKeyInfoPem()).Verify(data, sig)) throw new CryptographicException("만든 서명이 자체 검증에 실패했습니다.");
        File.WriteAllBytes(inPath + ".sig", sig);
        o.WriteLine($"서명 작성: {inPath}.sig ({sig.Length}바이트)");
        return 0;
    }

    private static int Verify(Dictionary<string, string> opt, TextWriter o, TextWriter e)
    {
        string pubPath = Req(opt, "pub"), inPath = Req(opt, "in");
        var v = new ManifestSignature(File.ReadAllText(pubPath));
        if (v.Verify(File.ReadAllBytes(inPath), File.ReadAllBytes(inPath + ".sig")))
        {
            o.WriteLine("서명 확인 성공 (키 " + v.KeyId + ")");
            return 0;
        }
        e.WriteLine("서명이 맞지 않습니다.");
        return 1;
    }

    /// <summary>ASH.Notes.json from every release-notes/&lt;version&gt;.md (newest first; a file whose name is no version is skipped with a warning).
    /// A missing note for --version is a warning, never a failure: the release still ships.</summary>
    private static int Notes(Dictionary<string, string> opt, TextWriter o, TextWriter e)
    {
        string dir = Req(opt, "dir"), outPath = Req(opt, "out");
        var list = new List<(SemVersion V, VersionNote N)>();
        if (Directory.Exists(dir))
            foreach (var f in Directory.GetFiles(dir, "*.md"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (!SemVersion.TryParse(name, out var v)) { if (!name.Equals("README", StringComparison.OrdinalIgnoreCase)) o.WriteLine($"경고: 버전 이름이 아닌 파일은 건너뜀: {Path.GetFileName(f)}"); continue; }
                list.Add((v!, NotesFile.ParseMarkdown(name, File.ReadAllText(f, Encoding.UTF8))));
            }
        else o.WriteLine("경고: 릴리스 노트 폴더가 없음: " + dir);
        if (opt.TryGetValue("version", out var want) && !list.Any(x => x.N.Version == want)) o.WriteLine($"경고: 이번 버전 {want}의 릴리스 노트(release-notes/{want}.md)가 없음");
        list.Sort((a, b) => b.V.CompareTo(a.V));
        File.WriteAllBytes(outPath, new NotesFile { Notes = list.Select(x => x.N).ToList() }.ToJson());
        o.WriteLine($"공지 원본 작성: {outPath} (버전 {list.Count}개)");
        return 0;
    }

    /// <summary>Exit 0 when the managed assembly embeds a manifest resource with this exact name (metadata only, nothing is loaded).</summary>
    private static int CheckResource(Dictionary<string, string> opt, TextWriter o, TextWriter e)
    {
        string dll = Req(opt, "dll"), name = Req(opt, "name");
        using var pe = new PEReader(File.OpenRead(dll));
        if (!pe.HasMetadata) throw new InvalidDataException("관리 어셈블리가 아닙니다: " + dll);
        var md = pe.GetMetadataReader();
        foreach (var h in md.ManifestResources)
            if (md.GetString(md.GetManifestResource(h).Name) == name)
            {
                o.WriteLine($"리소스 확인: {Path.GetFileName(dll)} 에 {name} 있음");
                return 0;
            }
        e.WriteLine($"리소스 없음: {dll} 에 {name} 이(가) 내장되어 있지 않습니다.");
        return 1;
    }
}
