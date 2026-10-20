using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace ASH.Core.Update;

/// <summary>
/// Verifies a manifest's detached ECDSA P-256 / SHA-256 signature (DER, as openssl and ECDsa.SignData(Rfc3279DerSequence) produce).
/// Holds one or more public keys (PEM "PUBLIC KEY" SPKI blocks); any one matching is enough.
/// SPKI is parsed by hand (ImportFromPem / ImportSubjectPublicKeyInfo do not exist on net48).
/// </summary>
public sealed class ManifestSignature
{
    // SEQUENCE{ SEQUENCE{ OID id-ecPublicKey, OID prime256v1 }, BIT STRING 00 04 ...} up to and including the 0x04 point marker
    private static readonly byte[] SpkiPrefix =
    {
        0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01,
        0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00, 0x04,
    };
    private const int SpkiLength = 91;
    private const string Begin = "-----BEGIN PUBLIC KEY-----", End = "-----END PUBLIC KEY-----";

    private readonly List<ECParameters> _keys = new();

    /// <summary>First 16 hex of SHA-256(SPKI DER) per key, comma-joined in PEM order.</summary>
    public string KeyId { get; }
    public int KeyCount => _keys.Count;

    /// <exception cref="ArgumentException">No valid P-256 public key in the text.</exception>
    public ManifestSignature(string pemWithOneOrMoreKeys)
    {
        var ids = new List<string>();
        string pem = pemWithOneOrMoreKeys ?? "";
        int pos = 0;
        while (true)
        {
            int b = pem.IndexOf(Begin, pos, StringComparison.Ordinal);
            if (b < 0) break;
            int e = pem.IndexOf(End, b, StringComparison.Ordinal);
            if (e < 0) throw new ArgumentException("공개키 PEM에 END 줄이 없습니다.", nameof(pemWithOneOrMoreKeys));
            byte[] der;
            try { der = Convert.FromBase64String(RemoveWhitespace(pem.Substring(b + Begin.Length, e - b - Begin.Length))); }
            catch (FormatException ex) { throw new ArgumentException("공개키 PEM의 base64가 올바르지 않습니다.", nameof(pemWithOneOrMoreKeys), ex); }
            _keys.Add(ParseSpki(der));
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(der);
                var sb = new StringBuilder();
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                ids.Add(sb.ToString());
            }
            pos = e + End.Length;
        }
        if (_keys.Count == 0) throw new ArgumentException("공개키 PEM(BEGIN PUBLIC KEY)이 없습니다.", nameof(pemWithOneOrMoreKeys));
        KeyId = string.Join(",", ids);
    }

    /// <summary>True if any held key verifies <paramref name="derSignature"/> over <paramref name="data"/>. Malformed signatures give false.</summary>
    public bool Verify(byte[] data, byte[] derSignature)
    {
        byte[] p1363;
        try { p1363 = DerToP1363(derSignature); }
        catch (ArgumentException) { return false; }
        foreach (var k in _keys)
        {
            try
            {
                using var ecdsa = ECDsa.Create(k)!;
                if (ecdsa.VerifyData(data, p1363, HashAlgorithmName.SHA256)) return true;
            }
            catch (CryptographicException) { }
        }
        return false;
    }

    private static ECParameters ParseSpki(byte[] der)
    {
        if (der.Length != SpkiLength) throw new ArgumentException("P-256 공개키(SPKI 91바이트)가 아닙니다.");
        for (int i = 0; i < SpkiPrefix.Length; i++)
            if (der[i] != SpkiPrefix[i]) throw new ArgumentException("P-256(prime256v1) EC 공개키가 아닙니다.");
        var x = new byte[32]; var y = new byte[32];
        Buffer.BlockCopy(der, SpkiPrefix.Length, x, 0, 32);
        Buffer.BlockCopy(der, SpkiPrefix.Length + 32, y, 0, 32);
        if (!OnCurve(x, y)) throw new ArgumentException("공개키 점이 P-256 곡선 위에 있지 않습니다.");
        var ep = new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } };
        try { using (ECDsa.Create(ep)) { } } // rejects a point that is not on the curve
        catch (CryptographicException ex) { throw new ArgumentException("공개키 점이 P-256 곡선 위에 있지 않습니다.", ex); }
        return ep;
    }

    // NIST P-256: y^2 = x^3 - 3x + b (mod p); ECDsa.Create does not check the point on every runtime
    private static readonly BigInteger P = BigInteger.Parse("0FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber);
    private static readonly BigInteger B = BigInteger.Parse("05AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B", NumberStyles.HexNumber);

    private static BigInteger BigEndian(byte[] b)
    {
        var le = new byte[b.Length + 1];
        for (int i = 0; i < b.Length; i++) le[i] = b[b.Length - 1 - i];
        return new BigInteger(le);
    }

    private static bool OnCurve(byte[] xb, byte[] yb)
    {
        BigInteger x = BigEndian(xb), y = BigEndian(yb);
        if (x >= P || y >= P) return false;
        BigInteger lhs = y * y % P, rhs = ((x * x % P) * x - 3 * x + B) % P;
        if (rhs.Sign < 0) rhs += P;
        return lhs == rhs;
    }

    /// <summary>
    /// Strict DER SEQUENCE{INTEGER r, INTEGER s} to r||s, 32 bytes each. Only minimal short-form lengths, positive INTEGERs
    /// without superfluous leading zeros, 0 &lt; r,s, and no trailing bytes are accepted.
    /// </summary>
    /// <exception cref="ArgumentException">Not a well-formed P-256 DER signature.</exception>
    internal static byte[] DerToP1363(byte[] der)
    {
        int p = 0;
        if (der == null || der.Length < 8 || der[p++] != 0x30) throw new ArgumentException("DER 서명이 아닙니다.");
        int total = Len(der, ref p);
        if (p + total != der.Length) throw new ArgumentException("DER 서명 길이가 맞지 않습니다.");
        var r = Int(der, ref p); var s = Int(der, ref p);
        if (p != der.Length) throw new ArgumentException("DER 서명 뒤에 데이터가 있습니다.");
        var o = new byte[64];
        Buffer.BlockCopy(r, 0, o, 32 - r.Length, r.Length);
        Buffer.BlockCopy(s, 0, o, 64 - s.Length, s.Length);
        return o;
    }

    // P-256 signatures never need long-form lengths (max 72 bytes), so 0x81.. is non-minimal or garbage
    private static int Len(byte[] d, ref int p)
    {
        if (p >= d.Length) throw new ArgumentException("DER 서명이 잘렸습니다.");
        int n = d[p++];
        if (n >= 0x80) throw new ArgumentException("DER 길이 형식 오류.");
        return n;
    }

    private static byte[] Int(byte[] d, ref int p)
    {
        if (p >= d.Length || d[p++] != 0x02) throw new ArgumentException("DER INTEGER가 아닙니다.");
        int n = Len(d, ref p);
        if (n == 0 || p + n > d.Length) throw new ArgumentException("DER INTEGER 길이 오류.");
        int start = p, end = p + n;
        p = end;
        if ((d[start] & 0x80) != 0) throw new ArgumentException("DER INTEGER가 음수입니다.");
        if (n > 1 && d[start] == 0 && (d[start + 1] & 0x80) == 0) throw new ArgumentException("DER INTEGER에 불필요한 앞자리 0이 있습니다.");
        if (d[start] == 0) { if (n == 1) throw new ArgumentException("서명 값이 0입니다."); start++; }
        if (end - start > 32) throw new ArgumentException("P-256 서명 정수가 너무 큽니다.");
        var v = new byte[end - start];
        Buffer.BlockCopy(d, start, v, 0, v.Length);
        return v;
    }

    private static string RemoveWhitespace(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) if (!char.IsWhiteSpace(c)) sb.Append(c);
        return sb.ToString();
    }
}
