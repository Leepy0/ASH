using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace ASH.Core.Update;

/// <summary>
/// Description of a downloadable zip (the server or the runner): {"product","version","file","sha256","size"}.
/// Signed as exact file bytes with the release key, like ASH.json. Extra fields are ignored.
/// </summary>
[DataContract]
public sealed class PackageManifest
{
    public const string ServerProduct = "ASH.Server", RunnerProduct = "ASH.Runner";
    public const string ServerManifestName = "ASH.Server.json", ServerSignatureName = "ASH.Server.json.sig";
    public const string RunnerManifestName = "ASH.Runner.json", RunnerSignatureName = "ASH.Runner.json.sig";

    [DataMember(Name = "product", Order = 0)] public string Product { get; set; } = "";
    [DataMember(Name = "version", Order = 1)] public string Version { get; set; } = "";
    [DataMember(Name = "file", Order = 2)] public string File { get; set; } = "";
    /// <summary>Lowercase hex SHA-256 of the zip.</summary>
    [DataMember(Name = "sha256", Order = 3)] public string Sha256 { get; set; } = "";
    [DataMember(Name = "size", Order = 4)] public long Size { get; set; }

    private static readonly Regex ServerFile = new Regex(@"^ASH-Server-[0-9A-Za-z.+-]+-win-x64\.zip\z", RegexOptions.CultureInvariant);
    private static readonly Regex RunnerFile = new Regex(@"^ASH-Runner-[0-9A-Za-z.+-]+-win-x64\.zip\z", RegexOptions.CultureInvariant);
    private static readonly Regex Sha256Pattern = new Regex(@"^[0-9a-f]{64}\z", RegexOptions.CultureInvariant);
    private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(PackageManifest));

    /// <summary>Plain runner zip name only (no path separators or other extension).</summary>
    public static bool IsRunnerFileName(string? name) => name != null && RunnerFile.IsMatch(name);

    public static PackageManifest ParseServer(byte[] json) => Parse(json, ServerProduct, ServerFile);

    public static PackageManifest ParseRunner(byte[] json) => Parse(json, RunnerProduct, RunnerFile);

    /// <exception cref="InvalidDataException">Not valid JSON for this product, or a field is missing or unsafe.</exception>
    private static PackageManifest Parse(byte[] json, string product, Regex fileRule)
    {
        PackageManifest? m;
        try
        {
            using var ms = new MemoryStream(json);
            m = Serializer.ReadObject(ms) as PackageManifest;
        }
        catch (Exception e) when (e is SerializationException || e is ArgumentException || e is FormatException || e is InvalidCastException)
        {
            throw new InvalidDataException(product + " 배포 정보를 읽을 수 없습니다: " + e.Message, e);
        }
        if (m == null) throw new InvalidDataException(product + " 배포 정보가 비어 있습니다.");
        m.Product ??= ""; m.Version ??= ""; m.File ??= ""; m.Sha256 ??= "";
        if (m.Product != product) throw new InvalidDataException($"배포 정보의 product가 {product}가 아닙니다: {m.Product}");
        if (!SemVersion.TryParse(m.Version, out _)) throw new InvalidDataException(product + " 배포 정보의 version이 올바르지 않습니다: " + m.Version);
        if (!fileRule.IsMatch(m.File)) throw new InvalidDataException(product + " 배포 정보의 file 이름이 올바르지 않습니다: " + m.File);
        if (!Sha256Pattern.IsMatch(m.Sha256)) throw new InvalidDataException(product + " 배포 정보의 sha256은 소문자 hex 64자여야 합니다.");
        if (m.Size <= 0) throw new InvalidDataException(product + " 배포 정보의 size가 올바르지 않습니다.");
        return m;
    }

    /// <summary>True when this package is newer than <paramref name="current"/> (SemVer). An unreadable current version counts as older.</summary>
    public bool IsNewerThan(string? current)
    {
        if (!SemVersion.TryParse(Version, out var mine)) return false;
        return !SemVersion.TryParse(current, out var cur) || mine!.CompareTo(cur) > 0;
    }
}
