using System;
using System.Security.Cryptography;
using System.Text;
using ASH.Core.Update;
using Xunit;

namespace ASH.Tests.Release;

public class ManifestSignatureTests
{
    // Fixed vector made once with `AshRelease keygen/manifest/sign` (net10) and cross-checked with `openssl dgst -sha256 -verify`.
    // Keys are throwaway; the signature was made with the SECOND key. Runs on net48 too, which cannot export keys/DER signatures itself.
    private const string PubKey1 = "-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAElBStBl7Eb11sP5TirX0V2wDgaCbV\nQX5yQeLhnFjiJYbcKkDsVhP/Frkwyz2XiifkgkfijQX0IAXxnddIC5eDWQ==\n-----END PUBLIC KEY-----\n";
    private const string PubKey2 = "-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEPzjXuLLBxoa8Yfgly0cN0FI0VKS3\n2oRpgEmjVkRrAraLAJ8D1yrsMTAIpcWyfFyny4z7W19DVVv2bswAO8nSJA==\n-----END PUBLIC KEY-----\n";
    private const string ManifestB64 = "eyJwcm9kdWN0IjoiQVNIIiwidmVyc2lvbiI6IjAuMS4wIiwibWluU3VwcG9ydGVkIjoiMC4xLjAiLCJmb3JtYXRWZXJzaW9uIjoxLCJyaWQiOiJ3aW4teDY0IiwiZmlsZSI6IkFTSC1TZXR1cC0wLjEuMC5leGUiLCJzaGEyNTYiOiI3Y2ZkMDQ2MjIzMTJmNzAyYTdmZDNkOGIwZDUwMzBkZjQ2ZDQzNzk0MWY0ZTMzMDg4MWI2ZGI0ODBiN2M3ZGIxIiwic2l6ZSI6MTAsInB1Ymxpc2hlZCI6IjIwMjYtMTAtMjBUMjI6MjA6MTNaIiwibm90ZXMiOiJub3RlcyBcL3gifQ==";
    private const string SigB64 = "MEYCIQDusp6Nq7hB4UktxnNtZTpdwCa0faVIGHGuk4hKH/CxtgIhAJYIGAUlJX8gk5CmlZOPP+6l5DOOOjq6gR2GmiCZt6LC";

    private static byte[] Data => Convert.FromBase64String(ManifestB64);
    private static byte[] Sig => Convert.FromBase64String(SigB64);

    [Fact] public void Vector_Verifies_With_Signing_Key_Alone() => Assert.True(new ManifestSignature(PubKey2).Verify(Data, Sig));
    [Fact] public void Vector_Verifies_When_Signer_Is_Second_Of_Two_Keys()
    {
        var v = new ManifestSignature(PubKey1 + PubKey2);
        Assert.Equal(2, v.KeyCount);
        Assert.True(v.Verify(Data, Sig));
    }
    [Fact] public void Vector_Fails_With_Other_Key() => Assert.False(new ManifestSignature(PubKey1).Verify(Data, Sig));
    [Fact] public void Vector_Fails_When_One_Byte_Changes()
    {
        var d = Data; d[10] ^= 1;
        Assert.False(new ManifestSignature(PubKey1 + PubKey2).Verify(d, Sig));
    }
    [Fact] public void Vector_Fails_When_Signature_Byte_Changes()
    {
        var s = Sig; s[20] ^= 1;
        Assert.False(new ManifestSignature(PubKey2).Verify(Data, s));
    }
    [Fact] public void Malformed_Signature_Gives_False_Not_Exception()
    {
        var v = new ManifestSignature(PubKey2);
        Assert.False(v.Verify(Data, new byte[0]));
        Assert.False(v.Verify(Data, new byte[] { 1, 2, 3 }));
        Assert.False(v.Verify(Data, new byte[64]));
        Assert.False(v.Verify(Data, Convert.FromBase64String(SigB64.Substring(0, 40))));
    }

    [Fact] public void KeyId_Is_First_16_Hex_Of_Spki_Sha256_Comma_Joined()
    {
        Assert.Equal("07f8686c99d7041e", new ManifestSignature(PubKey2).KeyId);
        Assert.Equal("48943b631168a6fa,07f8686c99d7041e", new ManifestSignature(PubKey1 + "\n" + PubKey2).KeyId);
        // independent check
        var der = Convert.FromBase64String(string.Concat(PubKey2.Split('\n')[1], PubKey2.Split('\n')[2]));
        using var sha = SHA256.Create();
        Assert.StartsWith(new ManifestSignature(PubKey2).KeyId, BitConverter.ToString(sha.ComputeHash(der)).Replace("-", "").ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("-----BEGIN PUBLIC KEY-----\n!!notbase64!!\n-----END PUBLIC KEY-----")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE")]
    public void Bad_Pem_Throws_ArgumentException(string pem) => Assert.Throws<ArgumentException>(() => new ManifestSignature(pem));

    [Fact] public void Non_P256_Spki_Is_Rejected()
    {
        // same length as P-256 SPKI but secp256k1-style OID bytes altered -> prefix check must fail
        var der = Convert.FromBase64String(string.Concat(PubKey2.Split('\n')[1], PubKey2.Split('\n')[2]));
        der[22] ^= 0xFF;
        string pem = "-----BEGIN PUBLIC KEY-----\n" + Convert.ToBase64String(der) + "\n-----END PUBLIC KEY-----";
        Assert.Throws<ArgumentException>(() => new ManifestSignature(pem));
    }

#if NET10_0_OR_GREATER
    [Fact] public void Generated_Keys_Sign_Der_And_Verify_Across_Many_Signatures()
    {
        using var a = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var b = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var v = new ManifestSignature(a.ExportSubjectPublicKeyInfoPem() + b.ExportSubjectPublicKeyInfoPem());
        for (int i = 0; i < 100; i++) // DER integer lengths vary (leading 00, short r/s)
        {
            var data = Encoding.UTF8.GetBytes("manifest " + i);
            var key = i % 2 == 0 ? a : b;
            var der = key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            Assert.True(v.Verify(data, der), "sig " + i);
            data[0] ^= 1;
            Assert.False(v.Verify(data, der));
        }
    }

    [Fact] public void Unrelated_Key_Does_Not_Verify()
    {
        using var a = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var data = new byte[] { 1, 2, 3 };
        Assert.False(new ManifestSignature(other.ExportSubjectPublicKeyInfoPem()).Verify(data, a.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)));
    }

    [Fact] public void Rsa_Public_Key_Is_Rejected()
    {
        using var rsa = RSA.Create(2048);
        Assert.Throws<ArgumentException>(() => new ManifestSignature(rsa.ExportSubjectPublicKeyInfoPem()));
    }
#endif
}
