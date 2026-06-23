using System.Security.Cryptography;
using System.Text;
using SessionMigrate.Core.Crypto;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class DpapiTests
{
    [Fact]
    public void ProtectThenUnprotect_RoundTrips()
    {
        byte[] secret = Encoding.UTF8.GetBytes("os_crypt key bytes");

        byte[] back = Dpapi.Unprotect(Dpapi.Protect(secret));

        Assert.Equal(secret, back);
    }

    [Fact]
    public void Unprotect_WrongEntropy_Throws()
    {
        byte[] secret = Encoding.UTF8.GetBytes("domain-A secret");
        byte[] blob = Dpapi.Protect(secret, "domain-A"u8.ToArray());

        // Entropy isolates "domains" on one machine — B cannot read A's blob.
        Assert.ThrowsAny<CryptographicException>(() => Dpapi.Unprotect(blob, "domain-B"u8.ToArray()));
    }
}
