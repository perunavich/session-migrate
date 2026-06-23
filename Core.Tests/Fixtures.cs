using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Storage;

namespace SessionMigrate.Core.Tests;

/// <summary>Builds synthetic Chromium profile files (Local State, Cookies) for offline tests.</summary>
internal static class Fixtures
{
    public static void WriteLocalState(string path, byte[] osCryptKey)
    {
        byte[] wrapped = Dpapi.Protect(osCryptKey);
        byte[] stored = new byte[5 + wrapped.Length];
        "DPAPI"u8.CopyTo(stored);
        wrapped.CopyTo(stored, 5);
        string encoded = Convert.ToBase64String(stored);
        File.WriteAllText(
            path,
            "{\"os_crypt\":{\"encrypted_key\":\"" + encoded + "\",\"app_bound_encrypted_key\":\"QUJD\"}}");
    }

    public static void CreateCookiesDb(
        string path, IEnumerable<(string Host, string Name, string Path, byte[] Blob)> rows) =>
        CookieDb.CreateWithRows(
            path,
            rows.Select(r => new CookieRecord(
                r.Host, r.Name, r.Path, r.Blob, Secure: true, HttpOnly: true, IsPersistent: true)));
}
