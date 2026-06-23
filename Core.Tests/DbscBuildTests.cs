using System.Text;
using Microsoft.Data.Sqlite;
using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Harvest;
using SessionMigrate.Core.Migration;
using SessionMigrate.Core.Profile;
using SessionMigrate.Core.Storage;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class DbscBuildTests : IDisposable
{
    private static readonly byte[] SourceKey = Enumerable.Range(5, 32).Select(i => (byte)i).ToArray();

    private readonly string _dir = Directory.CreateTempSubdirectory("cm-dbsc-").FullName;

    [Fact]
    public void Build_CarriesDbsc_ResealsBearer_AndOverlaysHarvestedV20()
    {
        string srcUserData = Path.Combine(_dir, "src");
        string srcProfile = Path.Combine(srcUserData, "Default");
        Directory.CreateDirectory(Path.Combine(srcProfile, "Network"));
        Fixtures.WriteLocalState(Path.Combine(srcUserData, "Local State"), SourceKey);

        Fixtures.CreateCookiesDb(
            Path.Combine(srcProfile, "Network", "Cookies"),
            [
            (".github.com", "user_session", "/", ChromiumCrypto.Encrypt(SourceKey, ".github.com", "gh")),
            (".google.com", "__Secure-1PSID", "/", Encoding.ASCII.GetBytes("v20-cannot-reseal")),
        ]);
        File.WriteAllText(Path.Combine(srcProfile, "Network", "Device Bound Sessions"), "dbsc");
        File.WriteAllText(Path.Combine(srcProfile, "trusted_vault.pb"), "vault");

        var harvested = new List<HarvestedCookie>
        {
            new() { Name = "__Secure-1PSID", Value = "durable-token", Domain = ".google.com", Path = "/" },
        };

        string destUserData = Path.Combine(_dir, "dest");
        DbscBuildResult result = DbscBuild.Build(srcUserData, "Default", destUserData, harvested);

        // DBSC key + trusted_vault carried verbatim.
        Assert.True(File.Exists(Path.Combine(destUserData, "Default", "Network", "Device Bound Sessions")));
        Assert.True(File.Exists(Path.Combine(destUserData, "Default", "trusted_vault.pb")));
        Assert.Equal(1, result.Overlay.Updated);

        byte[] destKey = LocalState.ReadOsCryptKey(Path.Combine(destUserData, "Local State"));
        List<CookieRecord> rows = CookieDb.Read(Path.Combine(destUserData, "Default", "Network", "Cookies"));

        // Bearer cookie resealed; App-Bound row overlaid as v10 under the dest key.
        Assert.Equal("gh", ChromiumCrypto.Decrypt(destKey, ".github.com", rows.Single(r => r.Name == "user_session").EncryptedValue));
        CookieRecord psid = rows.Single(r => r.Name == "__Secure-1PSID");
        Assert.Equal("v10", CookieScheme.Of(psid.EncryptedValue));
        Assert.Equal("durable-token", ChromiumCrypto.Decrypt(destKey, ".google.com", psid.EncryptedValue));
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
