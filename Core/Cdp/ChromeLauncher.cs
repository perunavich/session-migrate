using System.Diagnostics;

namespace SessionMigrate.Core.Cdp;

// Launches (or attaches to) a Chromium browser with a DevTools port. Tracks whether it started the
// process, so on dispose it only kills a browser it launched — never the user's running browser.
public sealed class ChromeLauncher : IDisposable
{
    private readonly Process? _process;

    private ChromeLauncher(Process? process) => _process = process;

    public bool Launched => _process is not null;

    // Use an already-running browser at a known DevTools port; never killed on dispose.
    public static ChromeLauncher Attach() => new(null);

    public static ChromeLauncher Launch(string chromePath, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo { FileName = chromePath, UseShellExecute = false };
        foreach (string argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return new ChromeLauncher(Process.Start(info) ?? throw new InvalidOperationException("failed to launch the browser"));
    }

    // The standard DevTools-harvest arguments (headless, isolated profile, CDP origin allowed).
    public static IReadOnlyList<string> HarvestArguments(int port, string userDataDir) =>
    [
        $"--remote-debugging-port={port}",
        $"--user-data-dir={userDataDir}",
        "--headless=new",
        "--no-first-run",
        "--no-default-browser-check",
        "--remote-allow-origins=*",
    ];

    public static string? FindChrome()
    {
        string[] candidates =
        [
            Path.Combine(Env("ProgramFiles"), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Env("ProgramFiles(x86)"), @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(Env("LOCALAPPDATA"), @"Google\Chrome\Application\chrome.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        }

        _process?.Dispose();
    }

    private static string Env(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;
}
