using Microsoft.Data.Sqlite;
using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Storage;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class CookieDbTests : IDisposable
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    private readonly string _dir = Directory.CreateTempSubdirectory("cm-cookiedb-").FullName;

    [Fact]
    public void Read_ReturnsInsertedRows()
    {
        string db = Path.Combine(_dir, "Cookies");
        Fixtures.CreateCookiesDb(
            db,
            [
            (".github.com", "user_session", "/", ChromiumCrypto.Encrypt(Key, ".github.com", "abc")),
            (".google.com", "SID", "/", ChromiumCrypto.Encrypt(Key, ".google.com", "xyz")),
        ]);

        List<CookieRecord> rows = CookieDb.Read(db);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.HostKey == ".github.com" && r.Name == "user_session");
    }

    [Fact]
    public void CreateWithRows_RoundTripsThroughRead()
    {
        string db = Path.Combine(_dir, "Cookies");
        Fixtures.CreateCookiesDb(db, [(".x.com", "n", "/", ChromiumCrypto.Encrypt(Key, ".x.com", "v"))]);

        CookieRecord row = Assert.Single(CookieDb.Read(db));
        Assert.Equal("v", ChromiumCrypto.Decrypt(Key, row.HostKey, row.EncryptedValue));
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
