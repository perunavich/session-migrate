using System.Text.Json;
using SessionMigrate.Core.Bundle;
using SessionMigrate.Core.Cdp;
using SessionMigrate.Core.Harvest;
using SessionMigrate.Core.Migration;
using SessionMigrate.Core.Profile;
using SessionMigrate.Core.Storage;
using SessionMigrate.Core.Tpm;

namespace SessionMigrate.Cli;

// Command-line front end exposing the migration toolkit.
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "detect" => Detect(),
                "report" => Report(args),
                "migrate" => Migrate(args),
                "export" => Export(args),
                "import" => Import(args),
                "tpm-check" => TpmCheck(args),
                "dbsc-build" => DbscBuildCommand(args),
                "snapshot" => Snapshot(args),
                "harvest" => await HarvestAsync(args),
                "cdp-harvest" => await CdpHarvestAsync(args),
                "cdp-verify" => await CdpVerifyAsync(args),
                _ => Fail($"unknown command '{args[0]}'"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Detect()
    {
        Print(BrowserDetector.Detect());
        return 0;
    }

    private static int Report(string[] args)
    {
        Need(args, 3, "report <userDataDir> <profile>");
        string profileDir = Path.Combine(args[1], args[2]);
        string? cookies = CookiePaths.Locate(profileDir);
        if (cookies is null)
        {
            return Fail("no Cookies file in that profile");
        }

        // Copy first so a running browser (which locks Cookies) doesn't block the read.
        string copy = Path.Combine(Path.GetTempPath(), "cm-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            CookieFile.CopyConsolidated(cookies, copy);
        }
        catch (IOException)
        {
            return Fail("the browser is holding its Cookies open — close it (or run elevated to use a VSS snapshot) and retry");
        }

        try
        {
            Print(MigrationReport.Analyze(copy, profileDir, Path.Combine(args[1], "Local State")));
        }
        finally
        {
            File.Delete(copy);
        }

        return 0;
    }

    private static int Migrate(string[] args)
    {
        Need(args, 4, "migrate <sourceUserDataDir> <profile> <destUserDataDir>");
        Print(ProfileMigrator.Migrate(args[1], args[2], args[3]));
        return 0;
    }

    private static int Export(string[] args)
    {
        Need(args, 6, "export <sourceUserDataDir> <profile> <browser> <bundleDir> <passphrase>");
        BundleMeta meta = ProfileBundle.Export(
            args[1], args[2], args[3], args[4], args[5], DateTimeOffset.UtcNow.ToString("o"));
        Print(meta);
        return 0;
    }

    private static int Import(string[] args)
    {
        Need(args, 4, "import <bundleDir> <destUserDataDir> <passphrase>");
        Print(ProfileBundle.Import(args[1], args[2], args[3]));
        return 0;
    }

    private static int TpmCheck(string[] args)
    {
        Need(args, 2, "tpm-check <DeviceBoundSessionsDb>");
        IReadOnlyList<byte[]> blobs = DeviceBoundSessions.ReadKeyBlobs(args[1]);
        if (blobs.Count == 0)
        {
            return Fail("no PCPM key found in that Device Bound Sessions store");
        }

        Print(blobs.Select(blob => TpmKeyCheck.Check(blob)).ToList());
        return 0;
    }

    private static int DbscBuildCommand(string[] args)
    {
        Need(args, 5, "dbsc-build <sourceUserDataDir> <profile> <destUserDataDir> <harvest.json> [--force-rotate]");
        bool forceRotate = args.Contains("--force-rotate");
        HarvestResult harvest = JsonSerializer.Deserialize<HarvestResult>(File.ReadAllText(args[4]))
            ?? throw new InvalidOperationException("could not read the harvest JSON");
        Print(DbscBuild.Build(args[1], args[2], args[3], harvest.Cookies, forceRotate));
        return 0;
    }

    private static int Snapshot(string[] args)
    {
        Need(args, 5, "snapshot <sourceUserDataBase> <profile> <destDir> <item> [<item>...]");
        Print(VssSnapshot.Capture(args[1], args[4..], args[2], args[3]));
        return 0;
    }

    private static async Task<int> HarvestAsync(string[] args)
    {
        int port = args.Length > 1 && int.TryParse(args[1], out int p) ? p : 8765;
        string outFile = args.Length > 2 ? args[2] : "cookies_export.json";

        using var receiver = new CookieReceiver(port);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        Console.WriteLine($"[harvest] listening on http://127.0.0.1:{receiver.Port} — load the extension; Ctrl+C to stop");

        Task run = receiver.RunAsync(cts.Token);
        HarvestResult? written = null;
        while (!cts.IsCancellationRequested && !run.IsCompleted)
        {
            await Task.Delay(300, CancellationToken.None);
            HarvestResult? latest = receiver.Latest;
            if (latest is not null && !ReferenceEquals(latest, written))
            {
                File.WriteAllText(outFile, JsonSerializer.Serialize(latest, Json));
                written = latest;
                Console.WriteLine($"[harvest] {latest.Count} cookie(s) [{latest.Trigger}] -> {outFile}");
            }
        }

        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
        }

        return written is null ? 1 : 0;
    }

    private static async Task<int> CdpHarvestAsync(string[] args)
    {
        Need(args, 3, "cdp-harvest <devtoolsPort> <out.json>");
        int port = int.Parse(args[1]);
        await using CdpClient client = await CdpClient.ConnectAsync(port);
        List<HarvestedCookie> cookies = await CdpCookies.HarvestAsync(client);
        File.WriteAllText(args[2], JsonSerializer.Serialize(
            new HarvestResult("cdp", cookies.Count, cookies), Json));
        Console.WriteLine($"[cdp-harvest] {cookies.Count} cookie(s) -> {args[2]}");
        return 0;
    }

    private static async Task<int> CdpVerifyAsync(string[] args)
    {
        Need(args, 3, "cdp-verify <devtoolsPort> <targets.json>");
        int port = int.Parse(args[1]);
        List<VerifyTarget> targets = JsonSerializer.Deserialize<List<VerifyTarget>>(
            File.ReadAllText(args[2]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("could not read targets");
        await using CdpClient client = await CdpClient.ConnectAsync(port);
        Print(await CdpVerifier.VerifyAsync(client, targets));
        return 0;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            session-migrate — Chromium profile migration toolkit

            detect
            report       <userDataDir> <profile>
            migrate      <sourceUserDataDir> <profile> <destUserDataDir>
            export       <sourceUserDataDir> <profile> <browser> <bundleDir> <passphrase>
            import       <bundleDir> <destUserDataDir> <passphrase>
            harvest      [port] [out.json]                       (start the extension receiver)
            cdp-harvest  <devtoolsPort> <out.json>               (harvest from a running browser)
            cdp-verify   <devtoolsPort> <targets.json>
            dbsc-build   <sourceUserDataDir> <profile> <destUserDataDir> <harvest.json> [--force-rotate]
            tpm-check    <DeviceBoundSessionsDb>
            snapshot     <sourceUserDataBase> <profile> <destDir> <item> [<item>...]   (admin)
            """);
    }

    private static void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));

    private static void Need(string[] args, int count, string usage)
    {
        if (args.Length < count)
        {
            throw new ArgumentException($"usage: {usage}");
        }
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"error: {message}");
        return 1;
    }
}
