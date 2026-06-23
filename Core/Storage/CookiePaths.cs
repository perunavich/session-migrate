namespace SessionMigrate.Core.Storage;

// Resolves the well-known location of a profile's Cookies store.
public static class CookiePaths
{
    // Network\Cookies on modern Chromium, or the legacy top-level Cookies — or null if neither exists.
    public static string? Locate(string profileDir)
    {
        string modern = Path.Combine(profileDir, "Network", "Cookies");
        if (File.Exists(modern))
        {
            return modern;
        }

        string legacy = Path.Combine(profileDir, "Cookies");
        return File.Exists(legacy) ? legacy : null;
    }
}
