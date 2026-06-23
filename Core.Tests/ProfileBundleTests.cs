using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using SessionMigrate.Core.Bundle;
using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Profile;
using SessionMigrate.Core.Storage;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class ProfileBundleTests : IDisposable
{
    private static readonly byte[] SourceKey = Enumerable.Range(3, 32).Select(i => (byte)i).ToArray();

    private readonly string _dir = Directory.CreateTempSubdirectory("cm-bundle-").FullName;

    [Fact]
    public void ExportThenImport_RoundTripsCookies_AndCarriesLevelDb()
    {
        string bundle = BuildSourceAndExport("correct-passphrase");

        Assert.True(File.Exists(Path.Combine(bundle, "keys.enc.json")));
        Assert.True(File.Exists(Path.Combine(bundle, "meta.json")));
        Assert.True(File.Exists(Path.Combine(bundle, "profile", "Local State")));
        Assert.True(File.Exists(Path.Combine(bundle, "profile", "Network", "Cookies")));
        Assert.True(File.Exists(Path.Combine(bundle, "profile", "Local Storage", "leveldb", "000003.log")));

        string dest = Path.Combine(_dir, "dest");
        ProfileBundle.Import(bundle, dest, "correct-passphrase");

        byte[] destKey = LocalState.ReadOsCryptKey(Path.Combine(dest, "Local State"));
        List<CookieRecord> rows = CookieDb.Read(Path.Combine(dest, "Default", "Network", "Cookies"));
        Assert.Equal("tok-1", ChromiumCrypto.Decrypt(destKey, ".github.com", rows.Single().EncryptedValue));
        Assert.True(File.Exists(Path.Combine(dest, "Default", "Local Storage", "leveldb", "000003.log")));
    }

    [Fact]
    public void Import_WrongPassphrase_Throws()
    {
        string bundle = BuildSourceAndExport("right");

        Assert.ThrowsAny<CryptographicException>(
            () => ProfileBundle.Import(bundle, Path.Combine(_dir, "dest"), "wrong"));
    }

    private string BuildSourceAndExport(string passphrase)
    {
        string srcUserData = Path.Combine(_dir, "src");
        string srcProfile = Path.Combine(srcUserData, "Default");
        Directory.CreateDirectory(Path.Combine(srcProfile, "Network"));
        Fixtures.WriteLocalState(Path.Combine(srcUserData, "Local State"), SourceKey);
        Fixtures.CreateCookiesDb(
            Path.Combine(srcProfile, "Network", "Cookies"),
            [(".github.com", "user_session", "/", ChromiumCrypto.Encrypt(SourceKey, ".github.com", "tok-1"))]);

        string levelDb = Path.Combine(srcProfile, "Local Storage", "leveldb");
        Directory.CreateDirectory(levelDb);
        File.WriteAllBytes(Path.Combine(levelDb, "000003.log"), [9, 8, 7]);

        string bundle = Path.Combine(_dir, "bundle");
        BundleMeta meta = ProfileBundle.Export(
            srcUserData, "Default", "Google Chrome", bundle, passphrase, "2026-01-01T00:00:00.0000000Z");
        Assert.Equal(1, meta.Report.V10);
        return bundle;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
