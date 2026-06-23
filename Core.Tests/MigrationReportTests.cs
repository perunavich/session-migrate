using System.Text;
using SessionMigrate.Core.Crypto;
using SessionMigrate.Core.Profile;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class MigrationReportTests : IDisposable
{
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    private readonly string _dir = Directory.CreateTempSubdirectory("cm-report-").FullName;

    [Fact]
    public void Analyze_CountsSchemes_RanksHosts_AndDetectsDbsc()
    {
        string profileDir = Path.Combine(_dir, "Default");
        Directory.CreateDirectory(Path.Combine(profileDir, "Network"));
        File.WriteAllText(Path.Combine(profileDir, "Network", "Device Bound Sessions"), "blob");

        string cookies = Path.Combine(profileDir, "Network", "Cookies");
        Fixtures.CreateCookiesDb(
            cookies,
            [
            (".github.com", "a", "/", ChromiumCrypto.Encrypt(Key, ".github.com", "x")),
            (".github.com", "b", "/", ChromiumCrypto.Encrypt(Key, ".github.com", "y")),
            (".google.com", "c", "/", Encoding.ASCII.GetBytes("v20-blob")),
        ]);

        ProfileReport report = MigrationReport.Analyze(cookies, profileDir);

        Assert.Equal(3, report.Cookies);
        Assert.Equal(2, report.DistinctHosts);
        Assert.Equal(2, report.V10);
        Assert.Equal(1, report.V20);
        Assert.True(report.DeviceBound);
        Assert.False(report.HasTrustedVault);
        Assert.Equal("github.com", report.Sites[0].Host);   // ".github.com" host-merged to "github.com"
        Assert.Equal(2, report.Sites[0].Cookies);
        Assert.Equal("migrates", report.Sites[0].Status);
        Assert.Contains(report.Warnings, w => w.Contains("App-Bound"));
    }

    [Fact]
    public void Analyze_EmptyProfile_ReturnsZerosWithoutThrowing()
    {
        string cookies = Path.Combine(_dir, "Cookies");
        Fixtures.CreateCookiesDb(cookies, []);

        ProfileReport report = MigrationReport.Analyze(cookies);

        Assert.Equal(0, report.Cookies);
        Assert.Equal(0, report.DistinctHosts);
        Assert.Empty(report.Sites);
        Assert.False(report.DeviceBound);
        Assert.Equal("unknown", report.Scheme);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
