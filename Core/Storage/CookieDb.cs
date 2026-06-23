using Microsoft.Data.Sqlite;

namespace SessionMigrate.Core.Storage;

// One cookie row, with the columns the migration reports on.
public sealed record CookieRecord(
    string HostKey,
    string Name,
    string Path,
    byte[] EncryptedValue,
    bool Secure,
    bool HttpOnly,
    bool IsPersistent);

// Reads the cookies table of a Chromium "Cookies" SQLite file (and creates one for samples and tests),
// using Windows' own winsqlite3. Re-keying is done by SqliteResealer.
public static class CookieDb
{
    private const string CreateTableSql =
        "CREATE TABLE cookies(host_key TEXT, name TEXT, path TEXT, value TEXT, encrypted_value BLOB, " +
        "is_secure INTEGER, is_httponly INTEGER, PRIMARY KEY (host_key, name, path))";

    static CookieDb() => SQLitePCL.Batteries_V2.Init();

    // Expects a consolidated snapshot — a live WAL-mode profile should be copied through
    // CookieFile.CopyConsolidated first, or recent cookies are missed.
    public static List<CookieRecord> Read(string dbPath)
    {
        using SqliteConnection conn = Open(dbPath, SqliteOpenMode.ReadOnly);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM cookies";
        using SqliteDataReader reader = cmd.ExecuteReader();

        var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            ordinals[reader.GetName(i)] = i;
        }

        int host = ordinals["host_key"];
        int name = ordinals["name"];
        int path = ordinals["path"];
        int value = ordinals["encrypted_value"];
        int secure = ordinals.GetValueOrDefault("is_secure", -1);
        int httpOnly = ordinals.GetValueOrDefault("is_httponly", -1);
        int persistent = ordinals.GetValueOrDefault("is_persistent", -1);

        List<CookieRecord> rows = [];
        while (reader.Read())
        {
            rows.Add(new CookieRecord(
                reader.GetString(host),
                reader.GetString(name),
                reader.GetString(path),
                reader.GetFieldValue<byte[]>(value),
                secure >= 0 && reader.GetInt64(secure) != 0,
                httpOnly >= 0 && reader.GetInt64(httpOnly) != 0,
                persistent < 0 || reader.GetInt64(persistent) != 0));
        }

        return rows;
    }

    // Creates a fresh Cookies file with the given rows (used for samples and tests).
    public static void CreateWithRows(string path, IEnumerable<CookieRecord> rows)
    {
        using SqliteConnection conn = Open(path, SqliteOpenMode.ReadWriteCreate);
        using (SqliteCommand create = conn.CreateCommand())
        {
            create.CommandText = CreateTableSql;
            create.ExecuteNonQuery();
        }

        using SqliteTransaction tx = conn.BeginTransaction();
        foreach (CookieRecord row in rows)
        {
            using SqliteCommand insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO cookies VALUES ($h, $n, $p, '', $v, $s, $o)";
            insert.Parameters.AddWithValue("$h", row.HostKey);
            insert.Parameters.AddWithValue("$n", row.Name);
            insert.Parameters.AddWithValue("$p", row.Path);
            insert.Parameters.AddWithValue("$v", row.EncryptedValue);
            insert.Parameters.AddWithValue("$s", row.Secure ? 1 : 0);
            insert.Parameters.AddWithValue("$o", row.HttpOnly ? 1 : 0);
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }

    // Pooling is off so the file can be deleted afterwards.
    internal static SqliteConnection Open(string dbPath, SqliteOpenMode mode)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = mode,
            Pooling = false,
        }.ConnectionString);
        conn.Open();
        return conn;
    }
}
