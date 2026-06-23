using SessionMigrate.Core.Profile;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class LocalStateTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("cm-localstate-").FullName;

    [Fact]
    public void SeedWithFreshKey_RoundTripsThroughReadOsCryptKey()
    {
        string src = Path.Combine(_dir, "source-LocalState");
        File.WriteAllText(src, """{"os_crypt":{"app_bound_encrypted_key":"QkVHaU4="},"other":1}""");
        string dest = Path.Combine(_dir, "dest-LocalState");

        byte[] minted = LocalState.SeedWithFreshKey(src, dest);

        Assert.Equal(32, minted.Length);
        Assert.Equal(minted, LocalState.ReadOsCryptKey(dest));
    }

    [Fact]
    public void SeedWithFreshKey_StripsAppBoundKey_AndKeepsOtherFields()
    {
        string src = Path.Combine(_dir, "source-LocalState");
        File.WriteAllText(src, """{"os_crypt":{"app_bound_encrypted_key":"QUJF"},"profile":{"name":"x"}}""");
        string dest = Path.Combine(_dir, "dest-LocalState");

        LocalState.SeedWithFreshKey(src, dest);

        string written = File.ReadAllText(dest);
        Assert.DoesNotContain("app_bound_encrypted_key", written);
        Assert.Contains("\"profile\"", written);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
