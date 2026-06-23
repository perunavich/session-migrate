namespace SessionMigrate.Core.Storage;

// Copies a Chromium profile directory tree, live-safe: opens each file with shared read so an open
// browser doesn't block the copy, and skips leveldb LOCK files (which can't be copied and are recreated
// on launch). SQLite side files (-wal/-shm) are copied as-is; a later reseal opens each store
// read/write, which folds the WAL in.
public static class ProfileCopier
{
    // Returns the number of files copied.
    public static int CopyTree(string sourceDir, string destDir)
    {
        int copied = 0;
        foreach (string source in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            // LOCK is a leveldb lock (recreated). Local State holds the os_crypt key and is written
            // separately at the User Data root, so never carry it into the profile copy.
            string fileName = Path.GetFileName(source);
            if (fileName is "LOCK" or "Local State")
            {
                continue;
            }

            string dest = Path.Combine(destDir, Path.GetRelativePath(sourceDir, source));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            try
            {
                CopyShared(source, dest);
                copied++;
            }
            catch (IOException)
            {
                // A transiently-locked file is skipped — best-effort while the browser is open.
            }
        }

        return copied;
    }

    // Copies a single file with shared read (creating the destination directory).
    public static void CopyFile(string source, string dest)
    {
        string? dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        CopyShared(source, dest);
    }

    private static void CopyShared(string source, string dest)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using FileStream output = File.Create(dest);
        input.CopyTo(output);
    }
}
