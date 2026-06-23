using Microsoft.Data.Sqlite;

namespace SessionMigrate.Core.Storage;

// Copies a Chromium "Cookies" SQLite file safely, even while the browser is open. A live profile keeps
// recent cookies in the -wal side file; copying only the main DB would drop them, so this copies the
// WAL too and folds it in with a checkpoint, leaving a self-contained snapshot.
public static class CookieFile
{
    public static void CopyConsolidated(string sourceCookies, string destPath)
    {
        ProfileCopier.CopyFile(sourceCookies, destPath);

        string sourceWal = sourceCookies + "-wal";
        if (File.Exists(sourceWal))
        {
            // -shm is shared memory that SQLite rebuilds; only the WAL carries committed data.
            // The checkpoint below is what makes the missing -shm safe — don't drop it.
            ProfileCopier.CopyFile(sourceWal, destPath + "-wal");
            Checkpoint(destPath);
        }
    }

    private static void Checkpoint(string dbPath)
    {
        using SqliteConnection conn = CookieDb.Open(dbPath, SqliteOpenMode.ReadWrite);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
        cmd.ExecuteNonQuery();
    }
}
