using System.Text;
using Microsoft.Data.Sqlite;
using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Harvest;
using SessionMigrate.Core.Migration;
using SessionMigrate.Core.Storage;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class CookieOverlayTests : IDisposable
{
    private static readonly byte[] DestKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    private readonly string _dir = Directory.CreateTempSubdirectory("cm-overlay-").FullName;

    [Fact]
    public void Apply_RewritesV20RowAsV10DomainBound_UnderDestKey()
    {
        string db = Path.Combine(_dir, "Cookies");
        Fixtures.CreateCookiesDb(
            db,
            [
            (".google.com", "SID", "/", Encoding.ASCII.GetBytes("v20-stale-cannot-decrypt")),
        ]);

        var harvested = new[]
        {
            new HarvestedCookie { Name = "SID", Value = "the-real-value", Domain = ".google.com", Path = "/" },
            new HarvestedCookie { Name = "nope", Value = "x", Domain = ".absent.com", Path = "/" },
        };

        OverlayResult result = CookieOverlay.Apply(db, DestKey, harvested);

        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Unmatched);

        CookieRecord row = CookieDb.Read(db).Single();
        Assert.Equal("v10", CookieScheme.Of(row.EncryptedValue));
        Assert.True(ChromiumCrypto.IsHostBound(DestKey, ".google.com", row.EncryptedValue));
        Assert.Equal("the-real-value", ChromiumCrypto.Decrypt(DestKey, ".google.com", row.EncryptedValue));
    }

    [Fact]
    public void Apply_ForceRotate_SkipsShortBoundCookies()
    {
        string db = Path.Combine(_dir, "Cookies");
        Fixtures.CreateCookiesDb(
            db,
            [
            (".google.com", "__Secure-1PSIDTS", "/", Encoding.ASCII.GetBytes("v20-bound")),
            (".google.com", "__Secure-1PSID", "/", Encoding.ASCII.GetBytes("v20-durable")),
        ]);

        var harvested = new[]
        {
            new HarvestedCookie { Name = "__Secure-1PSIDTS", Value = "bound", Domain = ".google.com", Path = "/" },
            new HarvestedCookie { Name = "__Secure-1PSID", Value = "durable", Domain = ".google.com", Path = "/" },
        };

        OverlayResult result = CookieOverlay.Apply(db, DestKey, harvested, forceRotate: true);

        Assert.Equal(1, result.Updated);   // only the durable cookie overlaid
        Assert.Equal(1, result.Skipped);   // the bound *PSIDTS skipped
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
