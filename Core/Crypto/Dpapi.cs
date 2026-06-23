using System.Security.Cryptography;

namespace SessionMigrate.Core.Crypto;

// Windows DPAPI wrap/unwrap for the os_crypt key, scoped to the current user. The optional entropy
// lets tests simulate separate key "domains" on one machine without switching users (a value wrapped
// with entropy A cannot be unwrapped with entropy B).
public static class Dpapi
{
    public static byte[] Protect(byte[] data, byte[]? entropy = null) =>
        ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] data, byte[]? entropy = null) =>
        ProtectedData.Unprotect(data, entropy, DataProtectionScope.CurrentUser);
}
