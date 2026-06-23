using Microsoft.Data.Sqlite;
using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Harvest;
using SessionMigrate.Core.Storage;

namespace SessionMigrate.Core.Migration;

// How an overlay went: rows rewritten, harvested cookies with no matching row, and skipped.
public sealed record OverlayResult(int Updated, int Unmatched, int Skipped);

// Overlays harvested plaintext cookies onto a destination Cookies DB as v10 domain-bound values
// (SHA-256(host_key) ‖ value) under the destination key — the way App-Bound (v20) cookies, which
// can't be resealed from a file, are made readable on the copy. Rows are matched by
// (host_key, name, path); partitioned (CHIPS) cookies are skipped.
public static class CookieOverlay
{
    // The short-lived bound-cookie name suffixes — the single source for both the forceRotate skip
    // and DeleteShortBound (Google's __Secure-*PSIDTS / *PSIDRTS and __Host-GAPSTS).
    private static readonly string[] ShortBoundSuffixes = ["PSIDTS", "PSIDRTS", "GAPSTS"];

    // With forceRotate, short-lived bound cookies (*PSIDTS/*PSIDRTS/*GAPSTS) are left out so the
    // browser cold-starts a rotation.
    public static OverlayResult Apply(
        string cookiesDbPath, byte[] destKey, IEnumerable<HarvestedCookie> cookies, bool forceRotate = false)
    {
        using SqliteConnection conn = CookieDb.Open(cookiesDbPath, SqliteOpenMode.ReadWrite);
        using SqliteTransaction tx = conn.BeginTransaction();

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "UPDATE cookies SET encrypted_value = $v, value = '' " +
            "WHERE host_key = $h AND name = $n AND path = $p";
        SqliteParameter pValue = cmd.Parameters.Add(new SqliteParameter { ParameterName = "$v" });
        SqliteParameter pHost = cmd.Parameters.Add(new SqliteParameter { ParameterName = "$h" });
        SqliteParameter pName = cmd.Parameters.Add(new SqliteParameter { ParameterName = "$n" });
        SqliteParameter pPath = cmd.Parameters.Add(new SqliteParameter { ParameterName = "$p" });

        int updated = 0, unmatched = 0, skipped = 0;
        foreach (HarvestedCookie cookie in cookies)
        {
            if (cookie.IsPartitioned || (forceRotate && IsShortBound(cookie.Name)))
            {
                skipped++;
                continue;
            }

            pValue.Value = ChromiumCrypto.Encrypt(destKey, cookie.Domain, cookie.Value, bindToHost: true);
            pHost.Value = cookie.Domain;
            pName.Value = cookie.Name;
            pPath.Value = cookie.Path;

            int changed = cmd.ExecuteNonQuery();
            if (changed > 0)
            {
                updated += changed;
            }
            else
            {
                unmatched++;
            }
        }

        tx.Commit();
        return new OverlayResult(updated, unmatched, skipped);
    }

    // Deletes the short-lived bound cookies so the browser must re-mint them via DBSC.
    public static int DeleteShortBound(string cookiesDbPath)
    {
        using SqliteConnection conn = CookieDb.Open(cookiesDbPath, SqliteOpenMode.ReadWrite);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM cookies WHERE " +
            string.Join(" OR ", ShortBoundSuffixes.Select(s => $"name LIKE '%{s}'"));
        return cmd.ExecuteNonQuery();
    }

    private static bool IsShortBound(string name) =>
        ShortBoundSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal));
}
