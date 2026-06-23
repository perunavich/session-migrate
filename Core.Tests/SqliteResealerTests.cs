using Microsoft.Data.Sqlite;
using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Storage;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class SqliteResealerTests : IDisposable
{
    private static readonly byte[] SourceKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] DestKey = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();

    private readonly string _dir = Directory.CreateTempSubdirectory("cm-resealer-").FullName;

    [Fact]
    public void ResealBlobColumn_RowidTable_RekeysEveryRow()
    {
        // Login Data's `logins` is an ordinary rowid table; rows are matched by rowid.
        string db = Path.Combine(_dir, "Login Data");
        using (var conn = OpenCreate(db))
        {
            Exec(conn, "CREATE TABLE logins(origin_url TEXT, password_value BLOB)");
            Insert(conn, "https://a.example", ChromiumCrypto.Encrypt(SourceKey, "a.example", "pw-a", bindToHost: false));
            Insert(conn, "https://b.example", ChromiumCrypto.Encrypt(SourceKey, "b.example", "pw-b", bindToHost: false));
        }

        int changed = SqliteResealer.ResealBlobColumn(db, "logins", "password_value", Reseal);

        Assert.Equal(2, changed);
        Assert.Equal("pw-a", Decrypt(db, "logins", "password_value", "origin_url", "https://a.example"));
        Assert.Equal("pw-b", Decrypt(db, "logins", "password_value", "origin_url", "https://b.example"));
    }

    [Fact]
    public void ResealBlobColumn_WithoutRowidTable_KeepsSiblingsDistinct()
    {
        // cookies is WITHOUT ROWID with source_port in the key, so two rows share host/name/path.
        string db = Path.Combine(_dir, "Cookies");
        using (var conn = OpenCreate(db))
        {
            Exec(
                conn,
                "CREATE TABLE cookies(host_key TEXT, name TEXT, path TEXT, encrypted_value BLOB, " +
                "source_port INTEGER, PRIMARY KEY (host_key, name, path, source_port))");
            InsertCookie(conn, ChromiumCrypto.Encrypt(SourceKey, ".google.com", "v-443"), 443);
            InsertCookie(conn, ChromiumCrypto.Encrypt(SourceKey, ".google.com", "v-80"), 80);
        }

        int changed = SqliteResealer.ResealBlobColumn(db, "cookies", "encrypted_value", Reseal);

        Assert.Equal(2, changed);
        Assert.Equal("v-443", DecryptCookie(db, 443));
        Assert.Equal("v-80", DecryptCookie(db, 80));
    }

    [Fact]
    public void ResealBlobColumn_MissingTableOrColumn_IsNoOp()
    {
        string db = Path.Combine(_dir, "Web Data");
        using (var conn = OpenCreate(db))
        {
            Exec(conn, "CREATE TABLE token_service(service TEXT, encrypted_token BLOB)");
        }

        Assert.Equal(0, SqliteResealer.ResealBlobColumn(db, "no_such_table", "encrypted_token", Reseal));
        Assert.Equal(0, SqliteResealer.ResealBlobColumn(db, "token_service", "no_such_column", Reseal));
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

    private static byte[]? Reseal(byte[] blob) =>
        ChromiumCrypto.EncryptRaw(DestKey, ChromiumCrypto.DecryptRaw(SourceKey, blob));

    private static SqliteConnection OpenCreate(string path)
    {
        SQLitePCL.Batteries_V2.Init();
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ConnectionString);
        conn.Open();
        return conn;
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void Insert(SqliteConnection conn, string url, byte[] blob)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO logins VALUES ($u, $v)";
        cmd.Parameters.AddWithValue("$u", url);
        cmd.Parameters.AddWithValue("$v", blob);
        cmd.ExecuteNonQuery();
    }

    private static void InsertCookie(SqliteConnection conn, byte[] blob, int port)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO cookies VALUES ('.google.com', 'SID', '/', $v, $port)";
        cmd.Parameters.AddWithValue("$v", blob);
        cmd.Parameters.AddWithValue("$port", port);
        cmd.ExecuteNonQuery();
    }

    private static string Decrypt(string db, string table, string column, string keyColumn, string keyValue)
    {
        using var conn = OpenCreate(db);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT \"{column}\" FROM \"{table}\" WHERE \"{keyColumn}\" = $k";
        cmd.Parameters.AddWithValue("$k", keyValue);
        return ChromiumCrypto.Decrypt(DestKey, string.Empty, (byte[])cmd.ExecuteScalar()!);
    }

    private static string DecryptCookie(string db, int port)
    {
        using var conn = OpenCreate(db);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT encrypted_value FROM cookies WHERE source_port = $port";
        cmd.Parameters.AddWithValue("$port", port);
        return ChromiumCrypto.Decrypt(DestKey, ".google.com", (byte[])cmd.ExecuteScalar()!);
    }
}
