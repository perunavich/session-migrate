using SessionMigrate.Core.Profile;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class BrowserDetectorTests
{
    [Fact]
    public void Detect_ReturnsOnlyInstallsThatExistOnDisk()
    {
        IReadOnlyList<BrowserInstall> installs = BrowserDetector.Detect();

        Assert.NotNull(installs);
        foreach (BrowserInstall install in installs)
        {
            Assert.True(Directory.Exists(install.UserDataDir), $"{install.Name} dir should exist");
            Assert.NotEqual(string.Empty, install.Name);
        }
    }
}
