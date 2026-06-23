using SessionMigrate.Core.Profile;
using SessionMigrate.Core.Storage;

namespace SessionMigrate.Core.Migration;

// Re-key outcome for one encrypted store in the migrated profile.
public sealed record StoreResult(string Store, bool Present, int Resealed);

public sealed record MigrationSummary(int FilesCopied, int TotalResealed, IReadOnlyList<StoreResult> Stores);

// Clones a whole Chromium profile to a fresh destination: copies every file (passwords, autofill,
// Local/Session Storage, IndexedDB, Service Workers, the DBSC key, …), mints a new os_crypt key in the
// destination Local State, then re-keys each encrypted SQLite store from the source key to the new one.
// v20 App-Bound values are left untouched (they need the live harvest path).
public static class ProfileMigrator
{
    private static readonly EncryptedStore[] Stores =
    [
        new("Cookies", ["Network/Cookies", "Cookies"], [("cookies", "encrypted_value")]),
        new("Passwords", ["Login Data"], [("logins", "password_value")]),
        new("Account passwords", ["Login Data For Account"], [("logins", "password_value")]),
        new("Autofill / tokens", ["Web Data"], [("credit_cards", "card_number_encrypted"), ("token_service", "encrypted_token")]),
        new("Account autofill", ["Account Web Data"], [("credit_cards", "card_number_encrypted"), ("token_service", "encrypted_token")]),
        new("Extension cookies", ["Network/Extension Cookies", "Extension Cookies"], [("cookies", "encrypted_value")]),
    ];

    // Pass sourceKey to use an externally supplied os_crypt key (e.g. one unwrapped from a passphrase
    // bundle) instead of reading it from the source via DPAPI.
    public static MigrationSummary Migrate(
        string sourceUserDataDir,
        string sourceProfile,
        string destUserDataDir,
        string destProfile = "Default",
        byte[]? sourceKey = null)
    {
        string sourceLocalState = Path.Combine(sourceUserDataDir, "Local State");
        string sourceProfileDir = Path.Combine(sourceUserDataDir, sourceProfile);
        string destProfileDir = Path.Combine(destUserDataDir, destProfile);

        byte[] resolvedSourceKey = sourceKey ?? LocalState.ReadOsCryptKey(sourceLocalState);
        Directory.CreateDirectory(destUserDataDir);
        string destLocalState = Path.Combine(destUserDataDir, "Local State");
        byte[] destKey = LocalState.SeedWithFreshKey(sourceLocalState, destLocalState);
        LocalState.RegisterProfile(destLocalState, destProfile);

        int filesCopied = ProfileCopier.CopyTree(sourceProfileDir, destProfileDir);

        var results = new List<StoreResult>();
        int totalResealed = 0;
        foreach (EncryptedStore store in Stores)
        {
            // Re-key every present location (e.g. both Network\Cookies and a legacy top-level Cookies),
            // not just the first — a leftover store left under the old key would be unreadable.
            List<string> paths = store.RelativePaths
                .Select(p => Path.Combine(destProfileDir, p.Replace('/', Path.DirectorySeparatorChar)))
                .Where(File.Exists)
                .ToList();
            if (paths.Count == 0)
            {
                results.Add(new StoreResult(store.Name, Present: false, Resealed: 0));
                continue;
            }

            int resealed = paths.Sum(path => store.Columns.Sum(c =>
                SqliteResealer.ResealBlobColumn(path, c.Table, c.Column, blob => Reseal.ResealBlob(resolvedSourceKey, destKey, blob))));
            totalResealed += resealed;
            results.Add(new StoreResult(store.Name, Present: true, resealed));
        }

        File.WriteAllText(Path.Combine(destUserDataDir, "First Run"), string.Empty);
        return new MigrationSummary(filesCopied, totalResealed, results);
    }

    private sealed record EncryptedStore(
        string Name, string[] RelativePaths, (string Table, string Column)[] Columns);
}
