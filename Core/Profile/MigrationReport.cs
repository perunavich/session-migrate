using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SessionMigrate.Core.Storage;

namespace SessionMigrate.Core.Profile;

// One host and how its cookies fare in a migration.
public sealed record SiteReport(string Host, int Cookies, string Status);

// What a profile holds, for the pre-migration summary the UI shows.
public sealed record ProfileReport(
    string Scheme,
    bool AppBound,
    bool DeviceBound,
    bool HasTrustedVault,
    int Cookies,
    int DistinctHosts,
    int V10,
    int V20,
    int Other,
    int SessionOnly,
    IReadOnlyList<SiteReport> Sites,
    IReadOnlyList<string> Warnings);

// Inspects a profile's Cookies DB (and its Local State / directory) and reports what would migrate:
// the os_crypt scheme, cookie counts by scheme (v10/v11 are bearer; v20 is App-Bound), session-only
// counts, the busiest hosts, and whether the profile carries a DBSC device-bound session.
public static class MigrationReport
{
    private const int TopSiteCount = 20;

    // Pass profileDir and localStatePath to detect DBSC artifacts and the os_crypt scheme.
    public static ProfileReport Analyze(
        string cookiesDbPath, string? profileDir = null, string? localStatePath = null)
    {
        (string scheme, bool appBound) = ReadScheme(localStatePath);
        List<CookieRecord> rows = CookieDb.Read(cookiesDbPath);

        int v10 = 0, v20 = 0, other = 0, sessionOnly = 0;
        var perHost = new Dictionary<string, (int Total, int V10, int V20)>(StringComparer.Ordinal);
        foreach (CookieRecord row in rows)
        {
            if (!row.IsPersistent)
            {
                sessionOnly++;
            }

            string kind = CookieScheme.Of(row.EncryptedValue) switch
            {
                "v10" or "v11" => "v10",
                "v20" => "v20",
                _ => "other",
            };
            if (kind == "v10")
            {
                v10++;
            }
            else if (kind == "v20")
            {
                v20++;
            }
            else
            {
                other++;
            }

            // Merge ".host" (domain) and "host" (host-only) into one entry, like Chromium reporting.
            string host = row.HostKey.TrimStart('.');
            (int total, int hv10, int hv20) = perHost.GetValueOrDefault(host);
            perHost[host] = (total + 1, hv10 + (kind == "v10" ? 1 : 0), hv20 + (kind == "v20" ? 1 : 0));
        }

        List<SiteReport> sites = perHost
            .Select(kv => new SiteReport(kv.Key, kv.Value.Total, StatusOf(kv.Value.V10, kv.Value.V20)))
            .OrderByDescending(s => s.Cookies)
            .ThenBy(s => s.Host, StringComparer.Ordinal)
            .Take(TopSiteCount)
            .ToList();

        bool deviceBound = profileDir is not null &&
            File.Exists(Path.Combine(profileDir, "Network", "Device Bound Sessions"));
        bool trustedVault = profileDir is not null &&
            File.Exists(Path.Combine(profileDir, "trusted_vault.pb"));

        var warnings = new List<string>();
        if (v20 > 0)
        {
            warnings.Add($"{v20} App-Bound (v20) cookie(s) — need a live-browser harvest; not migratable by the offline path.");
        }

        if (appBound)
        {
            warnings.Add("profile has an app_bound_encrypted_key (App-Bound cookies present).");
        }

        if (deviceBound || trustedVault)
        {
            warnings.Add("device-bound sessions detected (DBSC / trusted_vault) — those accounts (e.g. Google/Microsoft) need ONE re-login on a different machine; the TPM key cannot move.");
        }

        return new ProfileReport(
            scheme, appBound, deviceBound, trustedVault,
            rows.Count, perHost.Count, v10, v20, other, sessionOnly, sites, warnings);
    }

    private static string StatusOf(int v10, int v20) =>
        v10 > 0 ? "migrates" : v20 > 0 ? "app-bound (re-login)" : "unknown";

    private static (string Scheme, bool AppBound) ReadScheme(string? localStatePath)
    {
        if (localStatePath is null || !File.Exists(localStatePath))
        {
            return ("unknown", false);
        }

        try
        {
            JsonNode? osCrypt = JsonNode.Parse(File.ReadAllText(localStatePath))?["os_crypt"];
            bool appBound = osCrypt?["app_bound_encrypted_key"] is not null;
            string? encoded = osCrypt?["encrypted_key"]?.GetValue<string>();

            string scheme = "unknown";
            if (!string.IsNullOrEmpty(encoded))
            {
                byte[] blob = Convert.FromBase64String(encoded);
                if (blob.Length >= 5)
                {
                    scheme = Encoding.ASCII.GetString(blob, 0, 5);
                }
            }

            return (scheme, appBound);
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException)
        {
            return ("unknown", false);
        }
    }
}
