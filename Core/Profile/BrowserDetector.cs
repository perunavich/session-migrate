namespace SessionMigrate.Core.Profile;

// An installed Chromium browser and the profiles found under its User Data directory.
public sealed record BrowserInstall(string Name, string UserDataDir, IReadOnlyList<string> Profiles);

// Finds installed Chromium browsers by probing their well-known User Data directories (presence of a
// Local State file), so detection works for any user without launching anything. Opera and Opera GX
// live under Roaming and have no User Data path segment.
public static class BrowserDetector
{
    private static readonly Candidate[] Catalog =
    [
        new("Google Chrome", "LOCALAPPDATA", @"Google\Chrome\User Data"),
        new("Microsoft Edge", "LOCALAPPDATA", @"Microsoft\Edge\User Data"),
        new("Brave", "LOCALAPPDATA", @"BraveSoftware\Brave-Browser\User Data"),
        new("Vivaldi", "LOCALAPPDATA", @"Vivaldi\User Data"),
        new("Opera", "APPDATA", @"Opera Software\Opera Stable"),
        new("Opera GX", "APPDATA", @"Opera Software\Opera GX Stable"),
        new("Yandex", "LOCALAPPDATA", @"Yandex\YandexBrowser\User Data"),
        new("Arc", "LOCALAPPDATA", @"Arc\User Data"),
        new("Chromium", "LOCALAPPDATA", @"Chromium\User Data"),
        new("Thorium", "LOCALAPPDATA", @"Thorium\User Data"),
    ];

    public static IReadOnlyList<BrowserInstall> Detect()
    {
        List<BrowserInstall> found = [];
        foreach (Candidate candidate in Catalog)
        {
            string? baseDir = Environment.GetEnvironmentVariable(candidate.BaseEnvVar);
            if (string.IsNullOrEmpty(baseDir))
            {
                continue;
            }

            string userData = Path.Combine(baseDir, candidate.RelativePath);
            if (File.Exists(Path.Combine(userData, "Local State")))
            {
                found.Add(new BrowserInstall(candidate.Name, userData, FindProfiles(userData)));
            }
        }

        return found;
    }

    private static IReadOnlyList<string> FindProfiles(string userDataDir)
    {
        List<string> profiles = [];
        foreach (string dir in Directory.EnumerateDirectories(userDataDir))
        {
            string name = Path.GetFileName(dir);
            bool looksLikeProfile = name == "Default" || name.StartsWith("Profile ", StringComparison.Ordinal);
            if (looksLikeProfile && File.Exists(Path.Combine(dir, "Preferences")))
            {
                profiles.Add(name);
            }
        }

        // Flat layout (Opera): the User Data dir *is* the profile.
        if (profiles.Count == 0 && File.Exists(Path.Combine(userDataDir, "Preferences")))
        {
            profiles.Add(string.Empty);
        }

        return profiles;
    }

    private sealed record Candidate(string Name, string BaseEnvVar, string RelativePath);
}
