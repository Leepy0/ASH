namespace ASH.Tests.Release;

/// <summary>Fixed vector made once with `AshRelease keygen/manifest/sign` (net10) and cross-checked with `openssl dgst -sha256 -verify`. Keys are throwaway; the signature was made with the SECOND key.
/// Manifest = a valid ASH.json for a 10-byte "fake setup" ASH-Setup-0.1.0.exe. Usable on net48, which cannot export keys or make DER signatures itself.</summary>
internal static class SigVector
{
    public const string PubKey1 = "-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAElBStBl7Eb11sP5TirX0V2wDgaCbV\nQX5yQeLhnFjiJYbcKkDsVhP/Frkwyz2XiifkgkfijQX0IAXxnddIC5eDWQ==\n-----END PUBLIC KEY-----\n";
    public const string PubKey2 = "-----BEGIN PUBLIC KEY-----\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEPzjXuLLBxoa8Yfgly0cN0FI0VKS3\n2oRpgEmjVkRrAraLAJ8D1yrsMTAIpcWyfFyny4z7W19DVVv2bswAO8nSJA==\n-----END PUBLIC KEY-----\n";
    public const string ManifestB64 = "eyJwcm9kdWN0IjoiQVNIIiwidmVyc2lvbiI6IjAuMS4wIiwibWluU3VwcG9ydGVkIjoiMC4xLjAiLCJmb3JtYXRWZXJzaW9uIjoxLCJyaWQiOiJ3aW4teDY0IiwiZmlsZSI6IkFTSC1TZXR1cC0wLjEuMC5leGUiLCJzaGEyNTYiOiI3Y2ZkMDQ2MjIzMTJmNzAyYTdmZDNkOGIwZDUwMzBkZjQ2ZDQzNzk0MWY0ZTMzMDg4MWI2ZGI0ODBiN2M3ZGIxIiwic2l6ZSI6MTAsInB1Ymxpc2hlZCI6IjIwMjYtMTAtMjBUMjI6MjA6MTNaIiwibm90ZXMiOiJub3RlcyBcL3gifQ==";
    public const string SigB64 = "MEYCIQDusp6Nq7hB4UktxnNtZTpdwCa0faVIGHGuk4hKH/CxtgIhAJYIGAUlJX8gk5CmlZOPP+6l5DOOOjq6gR2GmiCZt6LC";
    public const string SetupText = "fake setup";
}
