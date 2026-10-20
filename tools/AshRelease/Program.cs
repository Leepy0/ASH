using System.Security.Cryptography;
using System.Text;
using ASH.Core.Update;

namespace AshRelease;

/// <summary>Release PC tool: keygen / manifest / sign / verify. Exit codes: 0 ok, 1 error.</summary>
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
        if (!SemVersion.TryParse(version, out _)) throw new ArgumentException("--version 이 SemVer(MAJOR.MINOR.PATCH[-pre])가 아닙니다: " + version);
        if (!SemVersion.TryParse(min, out _)) throw new ArgumentException("--min 이 SemVer가 아닙니다: " + min);
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
        if (k.KeySize != 256) throw new ArgumentException("P-256 개인키가 아닙니다.");
        byte[] sig = k.SignData(File.ReadAllBytes(inPath), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
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
}
