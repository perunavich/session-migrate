using System.Text;
using SessionMigrate.Core.Crypto;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class ChromiumCryptoTests
{
    // Arbitrary 32-byte AES key for round-trip tests. Not a secret; only its length matters.
    private static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void HostKeyHash_GitHub_MatchesChromeObservedValue()
    {
        // External golden vector: a real Chrome-written ".github.com" cookie
        // decrypts to SHA-256(".github.com") followed by the value. This pins the most
        // expensive lesson of the project — the in-plaintext host-key bind.
        const string Expected = "4c39becb07a558b602458395836f23cf3c668a5c1c96d968b0803a9869b33074";

        string actual = Convert.ToHexString(ChromiumCrypto.HostKeyHash(".github.com")).ToLowerInvariant();

        Assert.Equal(Expected, actual);
    }

    [Theory]
    [InlineData(".github.com", "no")]
    [InlineData(".google.com", "g.a000_QhHKsomeDurableTokenValue")]
    [InlineData("accounts.google.com", "")]
    public void EncryptThenDecrypt_RoundTrips_WithHostBind(string hostKey, string value)
    {
        byte[] blob = ChromiumCrypto.Encrypt(TestKey, hostKey, value);

        Assert.Equal("v10"u8.ToArray(), blob[..3]);
        Assert.True(ChromiumCrypto.IsHostBound(TestKey, hostKey, blob));
        Assert.Equal(value, ChromiumCrypto.Decrypt(TestKey, hostKey, blob));
    }

    [Fact]
    public void IsHostBound_OnlyTrueForTheMatchingHost()
    {
        byte[] blob = ChromiumCrypto.Encrypt(TestKey, ".github.com", "session=abc");

        Assert.True(ChromiumCrypto.IsHostBound(TestKey, ".github.com", blob));

        // A modern cookie carried to a row with a different host_key fails the bind — exactly
        // the case Chrome treats as tampering and drops.
        Assert.False(ChromiumCrypto.IsHostBound(TestKey, ".evil.com", blob));
    }

    [Fact]
    public void LegacyValue_NoHostBind_RoundTrips()
    {
        byte[] blob = ChromiumCrypto.Encrypt(TestKey, ".vivaldi.example", "v", bindToHost: false);

        Assert.False(ChromiumCrypto.IsHostBound(TestKey, ".vivaldi.example", blob));
        Assert.Equal("v", ChromiumCrypto.Decrypt(TestKey, ".vivaldi.example", blob));
    }

    [Fact]
    public void EncryptRawDecryptRaw_PreservesExactBytes()
    {
        byte[] plaintext = [0x00, 0x01, 0xFF, 0x42, 0x00];

        byte[] back = ChromiumCrypto.DecryptRaw(TestKey, ChromiumCrypto.EncryptRaw(TestKey, plaintext));

        Assert.Equal(plaintext, back);
    }

    [Fact]
    public void Decrypt_NonV10Blob_Throws()
    {
        byte[] junk = Encoding.ASCII.GetBytes("v20 app-bound blob, not ours .................");

        Assert.Throws<FormatException>(() => ChromiumCrypto.Decrypt(TestKey, ".github.com", junk));
    }
}
