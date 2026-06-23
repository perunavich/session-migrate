using System.Security.Cryptography;
using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Storage;

namespace SessionMigrate.Core.Migration;

// Re-keying of a single encrypted cookie/credential blob from one os_crypt key to another.
public static class Reseal
{
    // Preserves the exact plaintext (and so the cookie host-bind prefix). Returns null for non-v10/v11
    // (v20 App-Bound, plain) or undecryptable values, which are left untouched.
    public static byte[]? ResealBlob(byte[] sourceKey, byte[] destKey, byte[] blob)
    {
        if (!CookieScheme.IsResealable(blob))
        {
            return null;
        }

        try
        {
            // Re-encrypt under the same scheme prefix the source row used (v10 or v11).
            byte[] plaintext = ChromiumCrypto.DecryptRaw(sourceKey, blob);
            return ChromiumCrypto.EncryptRaw(destKey, plaintext, ChromiumCrypto.PrefixOf(blob));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }
}
