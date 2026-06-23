using SessionMigrate.Core.Harvest;
using SessionMigrate.Core.Profile;

namespace SessionMigrate.Core.Migration;

public sealed record DbscBuildResult(
    string DestUserDataDir, MigrationSummary Migration, OverlayResult Overlay, int BoundDeleted);

// Builds a same-machine DBSC restore profile (the recipe proven in the project notes): clone the
// source profile and re-key it (carrying the Device Bound Sessions TPM key and trusted_vault), overlay
// the freshly-harvested plaintext cookies as v10 domain-bound (so App-Bound rows become readable on
// the copy), and — for the cold-start path — drop the short-lived bound cookies so the browser
// re-mints them. Launch the result with --allow-browser-signin=false (or the BrowserSignin=0 policy)
// so the DICE reconcilor doesn't clear the cookie jar.
public static class DbscBuild
{
    public static DbscBuildResult Build(
        string sourceUserDataDir,
        string sourceProfile,
        string destUserDataDir,
        IReadOnlyList<HarvestedCookie> harvested,
        bool forceRotate = false)
    {
        // 1. Clone + reseal (a full tree copy carries Device Bound Sessions + trusted_vault verbatim).
        MigrationSummary migration = ProfileMigrator.Migrate(sourceUserDataDir, sourceProfile, destUserDataDir);

        byte[] destKey = LocalState.ReadOsCryptKey(Path.Combine(destUserDataDir, "Local State"));
        string destCookies = Path.Combine(destUserDataDir, "Default", "Network", "Cookies");

        // 2. Overlay the harvested plaintext (incl. App-Bound v20) as v10 domain-bound.
        OverlayResult overlay = CookieOverlay.Apply(destCookies, destKey, harvested, forceRotate);

        // 3. Cold-start: drop the short bound cookies so the browser re-mints them via DBSC.
        int boundDeleted = forceRotate ? CookieOverlay.DeleteShortBound(destCookies) : 0;

        return new DbscBuildResult(destUserDataDir, migration, overlay, boundDeleted);
    }
}
