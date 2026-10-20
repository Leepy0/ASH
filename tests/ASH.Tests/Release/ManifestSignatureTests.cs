using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ASH.Core.Update;
using Xunit;

namespace ASH.Tests.Release;

public class ManifestSignatureTests
{
    private const string PubKey1 = SigVector.PubKey1;
    private const string PubKey2 = SigVector.PubKey2;
    private const string ManifestB64 = SigVector.ManifestB64;
    private const string SigB64 = SigVector.SigB64;

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

    [Fact] public void Off_Curve_Point_Is_Rejected()
    {
        var der = Convert.FromBase64String(string.Concat(PubKey2.Split('\n')[1], PubKey2.Split('\n')[2]));
        der[der.Length - 1] ^= 1; // Y no longer matches X
        Assert.Throws<ArgumentException>(() => new ManifestSignature("-----BEGIN PUBLIC KEY-----\n" + Convert.ToBase64String(der) + "\n-----END PUBLIC KEY-----"));
    }

    private static byte[] Der(byte[] r, byte[] s, byte[]? tail = null)
    {
        var body = new byte[] { 2, (byte)r.Length }.Concat(r).Concat(new byte[] { 2, (byte)s.Length }).Concat(s).ToArray();
        return new byte[] { 0x30, (byte)body.Length }.Concat(body).Concat(tail ?? new byte[0]).ToArray();
    }
    private static byte[] Rep(byte v, int n) => Enumerable.Repeat(v, n).ToArray();

    [Fact] public void Der_Short_Values_Are_Left_Padded()
    {
        var p = ManifestSignature.DerToP1363(Der(new byte[] { 1 }, new byte[] { 2 }));
        Assert.Equal(64, p.Length);
        Assert.Equal(1, p[31]); Assert.Equal(2, p[63]);
        Assert.All(p.Take(31), b => Assert.Equal(0, b));
        var q = ManifestSignature.DerToP1363(Der(new byte[] { 0, 0x80 }, new byte[] { 0x7F }));
        Assert.Equal(0x80, q[31]); Assert.Equal(0x7F, q[63]);
    }
    [Fact] public void Der_Max_Size_33_Byte_Integer_With_Sign_Zero_Is_Accepted()
    {
        var p = ManifestSignature.DerToP1363(Der(new byte[] { 0 }.Concat(Rep(0xFF, 32)).ToArray(), new byte[] { 0 }.Concat(Rep(0x80, 32)).ToArray()));
        Assert.Equal(Rep(0xFF, 32), p.Take(32).ToArray());
        Assert.Equal(Rep(0x80, 32), p.Skip(32).ToArray());
    }
    [Fact] public void Der_Strictness_Rejects_Malformed()
    {
        Assert.Throws<ArgumentException>(() => ManifestSignature.DerToP1363(Der(new byte[] { 1 }, new byte[] { 2 }, new byte[] { 0 })));      // trailing byte
        Assert.Throws<ArgumentException>(() => ManifestSignature.DerToP1363(new byte[] { 0x30, 0x81, 0x06, 2, 1, 1, 2, 1, 2 }));               // long-form (non-minimal) length
        Assert.Throws<ArgumentException>(() => ManifestSignature.DerToP1363(Der(new byte[] { 0 }, new byte[] { 2 })));                         // r = 0
        Assert.Throws<ArgumentException>(() => ManifestSignature.DerToP1363(Der(new byte[] { 1 }, new byte[] { 0 })));                         // s = 0
        Assert.Throws<ArgumentException>(() => ManifestSignature.DerToP1363(Der(new byte[] { 0x01 }.Concat(Rep(0x11, 32)).ToArray(), new byte[] { 2 }))); // 33 bytes, no sign zero
        Assert.Throws<ArgumentException>(() => ManifestSignature.DerToP1363(Der(new byte[] { 0x80 }, new byte[] { 2 })));                      // negative
        Assert.Throws<ArgumentException>(() => ManifestSignature.DerToP1363(Der(new byte[] { 0, 1 }, new byte[] { 2 })));                      // superfluous leading zero
        Assert.Throws<ArgumentException>(() => ManifestSignature.DerToP1363(new byte[] { 0x30, 0x08, 2, 0, 2, 1, 1, 2, 1, 1 }));             // empty INTEGER
        Assert.Throws<ArgumentException>(() => ManifestSignature.DerToP1363(new byte[] { 0x30, 0x09, 2, 1, 1, 2, 1, 2 }));                    // sequence length lies
        Assert.Throws<ArgumentException>(() => ManifestSignature.DerToP1363(new byte[] { 0x31, 0x06, 2, 1, 1, 2, 1, 2 }));                    // not a SEQUENCE
        Assert.Throws<ArgumentException>(() => ManifestSignature.DerToP1363(new byte[] { 0x30, 0x06, 3, 1, 1, 2, 1, 2 }));                    // not an INTEGER
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
