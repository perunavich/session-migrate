using System.Text;
using Microsoft.Data.Sqlite;
using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Migration;
using SessionMigrate.Core.Profile;
using SessionMigrate.Core.Storage;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class ProfileMigratorTests : IDisposable
{
    private static readonly byte[] SourceKey = Enumerable.Range(7, 32).Select(i => (byte)i).ToArray();

    private readonly string _dir = Directory.CreateTempSubdirectory("cm-migrator-").FullName;

    [Fact]
    public void Migrate_ClonesProfile_RekeysCookiesAndPasswords_AndCarriesLevelDb()
    {
        string sourceUserData = Path.Combine(_dir, "src");
        string sourceProfile = Path.Combine(sourceUserData, "Default");
        Directory.CreateDirectory(Path.Combine(sourceProfile, "Network"));
        Fixtures.WriteLocalState(Path.Combine(sourceUserData, "Local State"), SourceKey);

        Fixtures.CreateCookiesDb(
            Path.Combine(sourceProfile, "Network", "Cookies"),
            [
            (".github.com", "user_session", "/", ChromiumCrypto.Encrypt(SourceKey, ".github.com", "gh-tok")),
            (".google.com", "__Secure-1PSID", "/", Encoding.ASCII.GetBytes("v20-app-bound")),
        ]);
        CreateLoginData(Path.Combine(sourceProfile, "Login Data"), "https://site.example", "hunter2");

        // A leveldb file that must be carried verbatim (Local Storage).
        string levelDbDir = Path.Combine(sourceProfile, "Local Storage", "leveldb");
        Directory.CreateDirectory(levelDbDir);
        File.WriteAllBytes(Path.Combine(levelDbDir, "000003.log"), [1, 2, 3, 4]);

        string destUserData = Path.Combine(_dir, "dest");
        MigrationSummary summary = ProfileMigrator.Migrate(sourceUserData, "Default", destUserData);

        // Fresh destination key, written and readable.
        byte[] destKey = LocalState.ReadOsCryptKey(Path.Combine(destUserData, "Local State"));
        Assert.True(File.Exists(Path.Combine(destUserData, "First Run")));

        // Cookies re-keyed under the new key; v20 left untouched.
        List<CookieRecord> cookies = CookieDb.Read(Path.Combine(destUserData, "Default", "Network", "Cookies"));
        CookieRecord bearer = cookies.Single(c => c.Name == "user_session");
        Assert.Equal("gh-tok", ChromiumCrypto.Decrypt(destKey, ".github.com", bearer.EncryptedValue));

        // Passwords re-keyed under the new key.
        Assert.Equal("hunter2", ReadPassword(Path.Combine(destUserData, "Default", "Login Data"), destKey));

        // Local Storage carried verbatim.
        byte[] carried = File.ReadAllBytes(
            Path.Combine(destUserData, "Default", "Local Storage", "leveldb", "000003.log"));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, carried);

        // Summary reflects what happened.
        Assert.Contains(summary.Stores, s => s.Store == "Cookies" && s.Present && s.Resealed == 1);
        Assert.Contains(summary.Stores, s => s.Store == "Passwords" && s.Present && s.Resealed == 1);
        Assert.True(summary.FilesCopied >= 3);
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

    private static void CreateLoginData(string path, string url, string password)
    {
        SQLitePCL.Batteries_V2.Init();
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ConnectionString);
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE logins(origin_url TEXT, password_value BLOB); " +
            "INSERT INTO logins VALUES ($u, $v)";
        cmd.Parameters.AddWithValue("$u", url);
        cmd.Parameters.AddWithValue("$v", ChromiumCrypto.Encrypt(SourceKey, url, password, bindToHost: false));
        cmd.ExecuteNonQuery();
    }

    private static string ReadPassword(string loginDataPath, byte[] destKey)
    {
        SQLitePCL.Batteries_V2.Init();
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = loginDataPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);
        conn.Open();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT password_value FROM logins LIMIT 1";
        return ChromiumCrypto.Decrypt(destKey, string.Empty, (byte[])cmd.ExecuteScalar()!);
    }
}
