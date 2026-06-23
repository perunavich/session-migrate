using System.Security.Cryptography;
using System.Text.Json.Nodes;
using SessionMigrate.Core.Crypto;

namespace SessionMigrate.Core.Profile;

// Reads and writes a Chromium "Local State" file, which holds the DPAPI-wrapped os_crypt key (base64
// of "DPAPI" | dpapiBlob under os_crypt.encrypted_key).
public static class LocalState
{
    private const int KeySize = 32;
    private static readonly byte[] DpapiPrefix = "DPAPI"u8.ToArray();

    // Reads and DPAPI-unwraps the 32-byte AES os_crypt key from a Local State file.
    public static byte[] ReadOsCryptKey(string localStatePath)
    {
        JsonNode root = Parse(localStatePath);
        string? encoded = root["os_crypt"]?["encrypted_key"]?.GetValue<string>();
        if (string.IsNullOrEmpty(encoded))
        {
            throw new InvalidOperationException("Local State has no os_crypt.encrypted_key");
        }

        return UnwrapKey(Convert.FromBase64String(encoded));
    }

    // Writes a Local State at destPath seeded from sourcePath, with a freshly minted os_crypt key
    // (DPAPI-wrapped for the current user) and the App-Bound key stripped — what a fresh destination
    // profile needs. Returns the new 32-byte os_crypt key.
    public static byte[] SeedWithFreshKey(string sourcePath, string destPath)
    {
        JsonObject root = Parse(sourcePath).AsObject();

        byte[] key = RandomNumberGenerator.GetBytes(KeySize);
        if (root["os_crypt"] is not JsonObject osCrypt)
        {
            osCrypt = new JsonObject();
            root["os_crypt"] = osCrypt;
        }

        osCrypt["encrypted_key"] = Convert.ToBase64String(WrapKey(key));
        osCrypt.Remove("app_bound_encrypted_key");

        File.WriteAllText(destPath, root.ToJsonString());
        return key;
    }

    // os_crypt.encrypted_key is base64 of "DPAPI" | DPAPI-wrapped-key; these are the only two places
    // that frame and unframe it.
    private static byte[] WrapKey(byte[] key)
    {
        byte[] wrapped = Dpapi.Protect(key);
        byte[] stored = new byte[DpapiPrefix.Length + wrapped.Length];
        DpapiPrefix.CopyTo(stored, 0);
        wrapped.CopyTo(stored, DpapiPrefix.Length);
        return stored;
    }

    private static byte[] UnwrapKey(byte[] stored)
    {
        if (stored.Length <= DpapiPrefix.Length ||
            !stored.AsSpan(0, DpapiPrefix.Length).SequenceEqual(DpapiPrefix))
        {
            throw new InvalidOperationException("os_crypt.encrypted_key is not DPAPI-wrapped");
        }

        return Dpapi.Unprotect(stored[DpapiPrefix.Length..]);
    }

    // Registers a profile in Local State (profile.info_cache + last_used) so the destination browser
    // adopts the migrated profile instead of running a first-run wizard.
    public static void RegisterProfile(string localStatePath, string profileName)
    {
        JsonObject root = Parse(localStatePath).AsObject();

        if (root["profile"] is not JsonObject profile)
        {
            profile = new JsonObject();
            root["profile"] = profile;
        }

        if (profile["info_cache"] is not JsonObject infoCache)
        {
            infoCache = new JsonObject();
            profile["info_cache"] = infoCache;
        }

        infoCache[profileName] = new JsonObject
        {
            ["name"] = profileName,
            ["is_using_default_name"] = true,
        };
        profile["last_used"] = profileName;

        File.WriteAllText(localStatePath, root.ToJsonString());
    }

    private static JsonNode Parse(string path) =>
        JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidOperationException($"empty Local State: {path}");
}
