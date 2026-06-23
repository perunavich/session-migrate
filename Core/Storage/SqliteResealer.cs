using Microsoft.Data.Sqlite;

namespace SessionMigrate.Core.Storage;

// Re-keys an encrypted BLOB column of any Chromium SQLite store (cookies, logins, credit cards, OAuth
// tokens…). Each row's blob is passed through a transform that decrypts under the source key and
// re-encrypts under the destination key. Rows are matched by rowid where the table has one, and by the
// real primary key for WITHOUT ROWID tables (cookies), so siblings never overwrite.
public static class SqliteResealer
{
    // The transform returns null to leave a row unchanged. A missing table or column is a no-op.
    // Returns the number of rows rewritten.
    public static int ResealBlobColumn(
        string dbPath, string table, string column, Func<byte[], byte[]?> transform)
    {
        using SqliteConnection conn = CookieDb.Open(dbPath, SqliteOpenMode.ReadWrite);
        if (!HasColumn(conn, table, column))
        {
            return 0;
        }

        using SqliteTransaction tx = conn.BeginTransaction();
        int changed = HasRowid(conn, table)
            ? ResealByRowid(conn, tx, table, column, transform)
            : ResealByPrimaryKey(conn, tx, table, column, transform);
        tx.Commit();
        return changed;
    }

    private static int ResealByRowid(
        SqliteConnection conn, SqliteTransaction tx, string table, string column, Func<byte[], byte[]?> transform)
    {
        var rows = new List<(long Rowid, byte[] Blob)>();
        using (SqliteCommand read = conn.CreateCommand())
        {
            read.CommandText = $"SELECT rowid, \"{column}\" FROM \"{table}\"";
            using SqliteDataReader reader = read.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(1))
                {
                    rows.Add((reader.GetInt64(0), reader.GetFieldValue<byte[]>(1)));
                }
            }
        }

        using SqliteCommand update = conn.CreateCommand();
        update.Transaction = tx;
        update.CommandText = $"UPDATE \"{table}\" SET \"{column}\" = $v WHERE rowid = $r";
        SqliteParameter valueParam = update.Parameters.Add(new SqliteParameter { ParameterName = "$v" });
        SqliteParameter rowidParam = update.Parameters.Add(new SqliteParameter { ParameterName = "$r" });

        int changed = 0;
        foreach ((long rowid, byte[] blob) in rows)
        {
            byte[]? updated = transform(blob);
            if (updated is null)
            {
                continue;
            }

            valueParam.Value = updated;
            rowidParam.Value = rowid;
            changed += update.ExecuteNonQuery();
        }

        return changed;
    }

    private static int ResealByPrimaryKey(
        SqliteConnection conn, SqliteTransaction tx, string table, string column, Func<byte[], byte[]?> transform)
    {
        string[] keyColumns = PrimaryKeyColumns(conn, table);
        var rows = new List<(byte[] Blob, object?[] Keys)>();
        using (SqliteCommand read = conn.CreateCommand())
        {
            string keyList = string.Join(", ", keyColumns.Select(c => $"\"{c}\""));
            read.CommandText = $"SELECT \"{column}\", {keyList} FROM \"{table}\"";
            using SqliteDataReader reader = read.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0))
                {
                    continue;
                }

                byte[] blob = reader.GetFieldValue<byte[]>(0);
                object?[] keys = Enumerable.Range(0, keyColumns.Length)
                    .Select(i => reader.IsDBNull(i + 1) ? null : reader.GetValue(i + 1))
                    .ToArray();
                rows.Add((blob, keys));
            }
        }

        string whereClause = string.Join(" AND ", keyColumns.Select((c, i) => $"\"{c}\" IS $k{i}"));
        using SqliteCommand update = conn.CreateCommand();
        update.Transaction = tx;
        update.CommandText = $"UPDATE \"{table}\" SET \"{column}\" = $v WHERE {whereClause}";
        SqliteParameter valueParam = update.Parameters.Add(new SqliteParameter { ParameterName = "$v" });
        SqliteParameter[] keyParams = keyColumns
            .Select((_, i) => update.Parameters.Add(new SqliteParameter { ParameterName = $"$k{i}" }))
            .ToArray();

        int changed = 0;
        foreach ((byte[] blob, object?[] keys) in rows)
        {
            byte[]? updated = transform(blob);
            if (updated is null)
            {
                continue;
            }

            valueParam.Value = updated;
            for (int i = 0; i < keyParams.Length; i++)
            {
                keyParams[i].Value = keys[i] ?? (object)DBNull.Value;
            }

            changed += update.ExecuteNonQuery();
        }

        return changed;
    }

    private static bool HasColumn(SqliteConnection conn, string table, string column)
    {
        // Table names here are fixed constants, so interpolating the PRAGMA is safe.
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{table}')";
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasRowid(SqliteConnection conn, string table)
    {
        try
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT rowid FROM \"{table}\" LIMIT 0";
            cmd.ExecuteReader().Dispose();
            return true;
        }
        catch (SqliteException)
        {
            // WITHOUT ROWID tables (e.g. cookies) reject the rowid pseudo-column.
            return false;
        }
    }

    private static string[] PrimaryKeyColumns(SqliteConnection conn, string table)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info('{table}')";
        using SqliteDataReader reader = cmd.ExecuteReader();

        var keys = new List<(int Order, string Name)>();
        while (reader.Read())
        {
            int pk = reader.GetInt32(5);
            if (pk > 0)
            {
                keys.Add((pk, reader.GetString(1)));
            }
        }

        return keys.OrderBy(k => k.Order).Select(k => k.Name).ToArray();
    }
}
