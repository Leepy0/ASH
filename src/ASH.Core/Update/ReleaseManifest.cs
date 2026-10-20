using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ASH.Core.Update;

/// <summary>ASH.json: release description signed as a whole (the signature covers the file bytes, never a re-serialization).</summary>
[DataContract]
public sealed class ReleaseManifest
{
    public const int CurrentFormatVersion = 1;
    public const string FileName = "ASH.json";
    public const string SignatureFileName = "ASH.json.sig";

    [DataMember(Name = "product", Order = 0)] public string Product { get; set; } = "ASH";
    [DataMember(Name = "version", Order = 1)] public string Version { get; set; } = "";
    [DataMember(Name = "minSupported", Order = 2)] public string MinSupported { get; set; } = "";
    [DataMember(Name = "formatVersion", Order = 3)] public int FormatVersion { get; set; } = CurrentFormatVersion;
    [DataMember(Name = "rid", Order = 4)] public string Rid { get; set; } = "win-x64";
    [DataMember(Name = "file", Order = 5)] public string File { get; set; } = "";
    /// <summary>Lowercase hex SHA-256 of the setup file.</summary>
    [DataMember(Name = "sha256", Order = 6)] public string Sha256 { get; set; } = "";
    [DataMember(Name = "size", Order = 7)] public long Size { get; set; }
    /// <summary>UTC, yyyy-MM-ddTHH:mm:ssZ.</summary>
    [DataMember(Name = "published", Order = 8)] public string Published { get; set; } = "";
    [DataMember(Name = "notes", Order = 9)] public string Notes { get; set; } = "";

    private static readonly DataContractJsonSerializer Serializer = new(typeof(ReleaseManifest));

    public static string FormatPublished(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <exception cref="InvalidDataException">Not valid manifest JSON, or a required field is missing.</exception>
    public static ReleaseManifest Parse(byte[] json)
    {
        ReleaseManifest? m;
        try
        {
            using var ms = new MemoryStream(json);
            m = Serializer.ReadObject(ms) as ReleaseManifest;
        }
        catch (Exception e) when (e is SerializationException || e is ArgumentException || e is FormatException || e is InvalidCastException)
        {
            throw new InvalidDataException("배포 정보(ASH.json)를 읽을 수 없습니다: " + e.Message, e);
        }
        if (m == null || string.IsNullOrEmpty(m.Version) || string.IsNullOrEmpty(m.File) || string.IsNullOrEmpty(m.Sha256))
            throw new InvalidDataException("배포 정보(ASH.json)에 version/file/sha256이 없습니다.");
        m.MinSupported ??= "";
        m.Product ??= "";
        m.Rid ??= "";
        m.Published ??= "";
        m.Notes ??= "";
        return m;
    }

    public byte[] ToJson()
    {
        using var ms = new MemoryStream();
        Serializer.WriteObject(ms, this);
        return ms.ToArray();
    }
}
