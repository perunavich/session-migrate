using Microsoft.Data.Sqlite;
using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Storage;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class CookieFileTests : IDisposable
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    private readonly string _dir = Directory.CreateTempSubdirectory("cm-cookiefile-").FullName;

    [Fact]
    public void CopyConsolidated_ProducesAReadableCopyWithAllRows()
    {
        string source = Path.Combine(_dir, "Cookies");
        Fixtures.CreateCookiesDb(
            source,
            [
            (".github.com", "user_session", "/", ChromiumCrypto.Encrypt(Key, ".github.com", "a")),
            (".reddit.com", "session", "/", ChromiumCrypto.Encrypt(Key, ".reddit.com", "b")),
        ]);

        string dest = Path.Combine(_dir, "copy", "Cookies");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        CookieFile.CopyConsolidated(source, dest);

        Assert.Equal(2, CookieDb.Read(dest).Count);
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
