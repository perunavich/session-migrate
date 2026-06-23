using System.Security.Cryptography;

namespace SessionMigrate.Core.Crypto;

// A short, non-reversible identifier for an os_crypt key (diagnostics only).
public static class KeyFingerprint
{
    // The first 8 bytes of SHA-256(key) as lowercase hex (16 chars).
    public static string Of(byte[] key) =>
        Convert.ToHexString(SHA256.HashData(key)[..8]).ToLowerInvariant();
}
