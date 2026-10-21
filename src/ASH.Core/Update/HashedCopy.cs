using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ASH.Core.Update;

/// <summary>Copies a stream while hashing it, and stops one byte past the expected size so a wrong or endless source cannot fill the disk.</summary>
public static class HashedCopy
{
    /// <returns>Bytes copied (at most <paramref name="expected"/> + 1 or so) and the lowercase hex SHA-256 of them.</returns>
    public static async Task<(long Size, string Sha256)> RunAsync(Stream src, Stream dst, long expected, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        var buf = new byte[81920];
        long size = 0;
        int n;
        while ((n = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
        {
            sha.TransformBlock(buf, 0, n, null, 0);
            await dst.WriteAsync(buf, 0, n, ct).ConfigureAwait(false);
            size += n;
            if (size > expected) break;
        }
        sha.TransformFinalBlock(buf, 0, 0);
        var sb = new StringBuilder(64);
        foreach (var b in sha.Hash!) sb.Append(b.ToString("x2"));
        return (size, sb.ToString());
    }
}
