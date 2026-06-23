using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Migration;
using SessionMigrate.Core.Profile;
using SessionMigrate.Core.Storage;

namespace SessionMigrate.Core.Bundle;

public sealed record BundleMeta(
    int Version, string Browser, string Profile, string Created, string KeyFingerprint, ProfileReport Report);

// Exports a profile to a portable, passphrase-protected bundle and imports it on another machine.
// A bundle is a directory: profile/ (the copied stores, still under the source os_crypt key),
// keys.enc.json (the 32-byte os_crypt key wrapped with PBKDF2-SHA256 + AES-256-GCM), and meta.json.
// Only the key is passphrase-protected; import re-keys the stores under a fresh destination key.
public static class ProfileBundle
{
    private const int Pbkdf2Iterations = 200_000;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static BundleMeta Export(
        string sourceUserDataDir,
        string sourceProfile,
        string browser,
        string outBundleDir,
        string passphrase,
        string createdUtc)
    {
        string sourceLocalState = Path.Combine(sourceUserDataDir, "Local State");
        string sourceProfileDir = Path.Combine(sourceUserDataDir, sourceProfile);
        byte[] sourceKey = LocalState.ReadOsCryptKey(sourceLocalState);

        string bundleProfile = Path.Combine(outBundleDir, "profile");
        Directory.CreateDirectory(bundleProfile);
        ProfileCopier.CopyFile(sourceLocalState, Path.Combine(bundleProfile, "Local State"));
        ProfileCopier.CopyTree(sourceProfileDir, bundleProfile);

        WriteWrappedKey(Path.Combine(outBundleDir, "keys.enc.json"), sourceKey, passphrase);

        string cookies = CookiePaths.Locate(bundleProfile)
            ?? Path.Combine(bundleProfile, "Network", "Cookies");
        ProfileReport report = MigrationReport.Analyze(
            cookies, bundleProfile, Path.Combine(bundleProfile, "Local State"));

        var meta = new BundleMeta(
            Version: 1, browser, sourceProfile, createdUtc, KeyFingerprint.Of(sourceKey), report);
        File.WriteAllText(Path.Combine(outBundleDir, "meta.json"), JsonSerializer.Serialize(meta, Json));
        return meta;
    }

    public static MigrationSummary Import(string bundleDir, string destUserDataDir, string passphrase)
    {
        string keysPath = Path.Combine(bundleDir, "keys.enc.json");
        string bundleProfile = Path.Combine(bundleDir, "profile");
        if (!File.Exists(keysPath) || !Directory.Exists(bundleProfile))
        {
            throw new InvalidOperationException("not a session-migrate bundle");
        }

        byte[] sourceKey = ReadWrappedKey(keysPath, passphrase);

        // The bundle's profile/ is a flat layout (Local State + stores at its root); treat it as a
        // User Data dir with an empty profile name, and re-key with the unwrapped source key.
        return ProfileMigrator.Migrate(bundleProfile, string.Empty, destUserDataDir, "Default", sourceKey);
    }

    private static void WriteWrappedKey(string path, byte[] key, string passphrase)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] derived = DeriveKey(passphrase, salt);
        byte[] cipher = new byte[key.Length];
        byte[] tag = new byte[TagSize];

        using (var gcm = new AesGcm(derived, TagSize))
        {
            gcm.Encrypt(nonce, key, cipher, tag);
        }

        var wrapped = new WrappedKey(
            "PBKDF2-SHA256",
            Pbkdf2Iterations,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(nonce),
            Convert.ToBase64String(cipher),
            Convert.ToBase64String(tag));
        File.WriteAllText(path, JsonSerializer.Serialize(wrapped, Json));
    }

    private static byte[] ReadWrappedKey(string path, string passphrase)
    {
        WrappedKey wrapped = JsonSerializer.Deserialize<WrappedKey>(File.ReadAllText(path), Json)
            ?? throw new InvalidOperationException("corrupt keys.enc.json");

        byte[] salt = Convert.FromBase64String(wrapped.Salt);
        byte[] nonce = Convert.FromBase64String(wrapped.Nonce);
        byte[] cipher = Convert.FromBase64String(wrapped.Cipher);
        byte[] tag = Convert.FromBase64String(wrapped.Tag);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, wrapped.Iter, HashAlgorithmName.SHA256, KeySize);

        byte[] key = new byte[cipher.Length];
        using var gcm = new AesGcm(derived, TagSize);
        gcm.Decrypt(nonce, cipher, tag, key);
        return key;
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, KeySize);

    private sealed record WrappedKey(string Kdf, int Iter, string Salt, string Nonce, string Cipher, string Tag);
}
