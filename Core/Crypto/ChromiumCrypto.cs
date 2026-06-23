using System.Security.Cryptography;
using System.Text;

namespace SessionMigrate.Core.Crypto;

// Chromium "v10"/"v11" cookie blob: prefix(3) | nonce(12) | ciphertext | tag(16), AES-256-GCM under the
// os_crypt key (no GCM AAD). Modern Chrome (M127+) prepends SHA-256(host_key) to the value before
// encrypting; older builds (Edge, Vivaldi, Chrome-for-Testing) don't.
// EncryptRaw/DecryptRaw move a cookie's exact plaintext across keys (reseal — prefix preserved);
// Encrypt/Decrypt work at the value level (the v20 harvest/inject path), adding/stripping the prefix.
public static class ChromiumCrypto
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HostHashSize = 32;
    private const int PrefixSize = 3;

    private static readonly byte[] V10 = "v10"u8.ToArray();
    private static readonly byte[] V11 = "v11"u8.ToArray();

    public static byte[] HostKeyHash(string hostKey)
    {
        ArgumentNullException.ThrowIfNull(hostKey);
        return SHA256.HashData(Encoding.UTF8.GetBytes(hostKey));
    }

    public static byte[] PrefixOf(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        if (!IsKnownPrefix(blob))
        {
            throw new FormatException("not a v10/v11 cookie blob");
        }

        return blob[..PrefixSize];
    }

    public static byte[] EncryptRaw(byte[] key, ReadOnlySpan<byte> plaintext, byte[]? prefix = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        prefix ??= V10;

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using (var gcm = new AesGcm(key, TagSize))
        {
            gcm.Encrypt(nonce, plaintext, cipher, tag);
        }

        return Concat(prefix, nonce, cipher, tag);
    }

    public static byte[] DecryptRaw(byte[] key, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(blob);

        int minLength = PrefixSize + NonceSize + TagSize;
        if (blob.Length < minLength || !IsKnownPrefix(blob))
        {
            throw new FormatException("not a v10/v11 cookie blob");
        }

        int cipherStart = PrefixSize + NonceSize;
        int cipherLength = blob.Length - cipherStart - TagSize;
        ReadOnlySpan<byte> blobSpan = blob;
        ReadOnlySpan<byte> nonce = blobSpan.Slice(PrefixSize, NonceSize);
        ReadOnlySpan<byte> cipher = blobSpan.Slice(cipherStart, cipherLength);
        ReadOnlySpan<byte> tag = blobSpan.Slice(blob.Length - TagSize, TagSize);

        byte[] plaintext = new byte[cipherLength];
        using (var gcm = new AesGcm(key, TagSize))
        {
            gcm.Decrypt(nonce, cipher, tag, plaintext);
        }

        return plaintext;
    }

    // bindToHost (default) prepends SHA-256(host_key); pass false for the legacy no-prefix format.
    public static byte[] Encrypt(byte[] key, string hostKey, string value, bool bindToHost = true)
    {
        ArgumentNullException.ThrowIfNull(value);

        byte[] valueBytes = Encoding.UTF8.GetBytes(value);
        return bindToHost
            ? EncryptRaw(key, Concat(HostKeyHash(hostKey), valueBytes))
            : EncryptRaw(key, valueBytes);
    }

    // Strips the host-bind prefix when it matches hostKey; a non-match reads as a legacy unbound cookie.
    public static string Decrypt(byte[] key, string hostKey, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(hostKey);

        byte[] plaintext = DecryptRaw(key, blob);
        if (HasHostBind(plaintext, hostKey))
        {
            return Encoding.UTF8.GetString(plaintext, HostHashSize, plaintext.Length - HostHashSize);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    // A bind that doesn't match the row's own host_key is what Chrome drops as tampering.
    public static bool IsHostBound(byte[] key, string hostKey, byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(hostKey);
        return HasHostBind(DecryptRaw(key, blob), hostKey);
    }

    private static bool IsKnownPrefix(byte[] blob) =>
        blob.Length >= PrefixSize &&
        (blob.AsSpan(0, PrefixSize).SequenceEqual(V10) || blob.AsSpan(0, PrefixSize).SequenceEqual(V11));

    private static bool HasHostBind(byte[] plaintext, string hostKey) =>
        plaintext.Length >= HostHashSize &&
        plaintext.AsSpan(0, HostHashSize).SequenceEqual(HostKeyHash(hostKey));

    private static byte[] Concat(params byte[][] parts)
    {
        byte[] result = new byte[parts.Sum(p => p.Length)];
        int offset = 0;
        foreach (byte[] part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
