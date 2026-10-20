#if NET10_0_OR_GREATER
using System;
using System.IO;
using System.Text;
using ASH.Core.Update;
using Xunit;

namespace ASH.Tests.Release;

public sealed class AshReleaseToolTests : IDisposable
{
    private readonly string _d = Path.Combine(Path.GetTempPath(), "ash-rel-" + Guid.NewGuid().ToString("N"));
    public AshReleaseToolTests() => Directory.CreateDirectory(_d);
    public void Dispose() { try { Directory.Delete(_d, true); } catch (IOException) { } }

    private (int rc, string o, string e) Run(params string[] a)
    {
        var o = new StringWriter(); var e = new StringWriter();
        int rc = AshRelease.Program.Run(a, o, e);
        return (rc, o.ToString(), e.ToString());
    }

    private string P(string n) => Path.Combine(_d, n);

    private string MakeManifest()
    {
        File.WriteAllBytes(P("ASH-Setup-0.3.0.exe"), Encoding.ASCII.GetBytes("setup-bytes"));
        File.WriteAllText(P("notes.txt"), "고친 것\n- 하나");
        Assert.Equal(0, Run("manifest", "--file", P("ASH-Setup-0.3.0.exe"), "--version", "0.3.0", "--min", "0.2.0", "--notes", P("notes.txt"), "--out", P("ASH.json")).rc);
        return P("ASH.json");
    }

    [Fact] public void Keygen_Manifest_Sign_Verify_RoundTrip()
    {
        var kg = Run("keygen", "--out", P("keys"));
        Assert.Equal(0, kg.rc);
        Assert.Contains("BEGIN PRIVATE KEY", File.ReadAllText(P("keys\\ash-signing.pem")));
        Assert.Contains("BEGIN PUBLIC KEY", File.ReadAllText(P("keys\\update-key.pub")));

        string json = MakeManifest();
        var m = ReleaseManifest.Parse(File.ReadAllBytes(json));
        Assert.Equal("0.3.0", m.Version);
        Assert.Equal("0.2.0", m.MinSupported);
        Assert.Equal("ASH-Setup-0.3.0.exe", m.File);
        Assert.Equal(11, m.Size);
        Assert.Matches("^[0-9a-f]{64}$", m.Sha256);
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", m.Published);
        Assert.Contains("고친 것", m.Notes);

        Assert.Equal(0, Run("sign", "--key", P("keys\\ash-signing.pem"), "--in", json).rc);
        Assert.True(File.Exists(json + ".sig"));
        Assert.Equal(0, Run("verify", "--pub", P("keys\\update-key.pub"), "--in", json).rc);

        // the verifier in Core agrees (what the plugin/server will use)
        Assert.True(new ManifestSignature(File.ReadAllText(P("keys\\update-key.pub"))).Verify(File.ReadAllBytes(json), File.ReadAllBytes(json + ".sig")));

        // change one byte of the manifest -> verify fails with exit 1
        var bytes = File.ReadAllBytes(json); bytes[bytes.Length / 2] ^= 1; File.WriteAllBytes(json, bytes);
        Assert.Equal(1, Run("verify", "--pub", P("keys\\update-key.pub"), "--in", json).rc);
    }

    [Fact] public void Verify_With_Other_Key_Fails()
    {
        Run("keygen", "--out", P("a")); Run("keygen", "--out", P("b"));
        string json = MakeManifest();
        Run("sign", "--key", P("a\\ash-signing.pem"), "--in", json);
        Assert.Equal(1, Run("verify", "--pub", P("b\\update-key.pub"), "--in", json).rc);
        File.WriteAllText(P("both.pub"), File.ReadAllText(P("b\\update-key.pub")) + File.ReadAllText(P("a\\update-key.pub")));
        Assert.Equal(0, Run("verify", "--pub", P("both.pub"), "--in", json).rc);
    }

    [Fact] public void CheckResource_Finds_Embedded_Resource_And_Fails_When_Missing()
    {
        var core = typeof(ReleaseManifest).Assembly;
        var name = core.GetManifestResourceNames()[0];
        Assert.Equal(0, Run("check-resource", "--dll", core.Location, "--name", name).rc);
        var miss = Run("check-resource", "--dll", core.Location, "--name", "update-key.pub");
        Assert.Equal(1, miss.rc);
        Assert.Contains("update-key.pub", miss.e);
        Assert.Equal(1, Run("check-resource", "--dll", P("nope.dll"), "--name", "x").rc); // missing file = error exit
    }

    [Fact] public void Sign_Rejects_Non_P256_Key()
    {
        using var k = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        File.WriteAllText(P("p384.pem"), k.ExportPkcs8PrivateKeyPem());
        string json = MakeManifest();
        var r = Run("sign", "--key", P("p384.pem"), "--in", json);
        Assert.Equal(1, r.rc);
        Assert.False(File.Exists(json + ".sig"));
    }

    [Fact] public void Keygen_Refuses_To_Overwrite()
    {
        Assert.Equal(0, Run("keygen", "--out", P("k")).rc);
        string before = File.ReadAllText(P("k\\ash-signing.pem"));
        var r = Run("keygen", "--out", P("k"));
        Assert.Equal(1, r.rc);
        Assert.Contains("덮어쓰지", r.e);
        Assert.Equal(before, File.ReadAllText(P("k\\ash-signing.pem")));
    }

    [Fact] public void Bad_Usage_And_Inputs_Exit_1()
    {
        Assert.Equal(1, Run().rc);
        Assert.Equal(1, Run("nope").rc);
        Assert.Equal(1, Run("manifest", "--file", P("missing.exe"), "--version", "1.0.0", "--min", "1.0.0", "--out", P("x.json")).rc);
        File.WriteAllText(P("s.exe"), "x");
        Assert.Equal(1, Run("manifest", "--file", P("s.exe"), "--version", "one", "--min", "1.0.0", "--out", P("x.json")).rc);
        File.WriteAllText(P("ASH-Setup-1.0.0.exe"), "x");
        Assert.Equal(1, Run("manifest", "--file", P("ASH-Setup-1.0.0.exe"), "--version", "1.0.0", "--min", "1.0.1", "--out", P("x.json")).rc); // min > version
        Assert.Equal(1, Run("manifest", "--file", P("s.exe"), "--version", "1.0.0", "--min", "1.0.0", "--out", P("x.json")).rc);             // file name not ASH-Setup-*.exe
        Assert.Equal(0, Run("manifest", "--file", P("ASH-Setup-1.0.0.exe"), "--version", "1.0.0", "--min", "1.0.0", "--out", P("x.json")).rc);
        Assert.Equal(1, Run("sign", "--key", P("nokey.pem"), "--in", P("s.exe")).rc);
        Assert.Equal(1, Run("verify", "--pub", P("nokey.pub"), "--in", P("s.exe")).rc);
    }
}
#endif
