using System.Text.Json;
using SessionMigrate.Core.Bundle;
using SessionMigrate.Core.Migration;
using SessionMigrate.Core.Profile;
using SessionMigrate.Core.Storage;

namespace SessionMigrate.Ui;

// Handles JSON commands from the web UI by calling the Core, and returns a JSON response. The web
// layer never touches crypto or the filesystem directly — it asks the bridge.
public sealed class Bridge
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IFolderPicker _folderPicker;

    public Bridge(IFolderPicker folderPicker) => _folderPicker = folderPicker;

    public async Task<string> HandleAsync(string requestJson)
    {
        Request request;
        try
        {
            request = JsonSerializer.Deserialize<Request>(requestJson, Json) ?? new Request();
        }
        catch (JsonException)
        {
            return Error(0, "malformed request");
        }

        try
        {
            object data = request.Cmd switch
            {
                "detect" => Detect(),
                "analyze" => Analyze(request),
                "migrate" => await MigrateAsync(request),
                "export" => await ExportAsync(request),
                "import" => await ImportAsync(request),
                _ => throw new InvalidOperationException($"unknown command '{request.Cmd}'"),
            };
            return JsonSerializer.Serialize(new Response { Id = request.Id, Ok = true, Data = data }, Json);
        }
        catch (Exception ex)
        {
            return Error(request.Id, ex.Message);
        }
    }

    private static string Error(int id, string message) =>
        JsonSerializer.Serialize(new Response { Id = id, Ok = false, Error = message }, Json);

    private static object Detect()
    {
        var browsers = BrowserDetector.Detect()
            .Select(b => new { name = b.Name, userDataDir = b.UserDataDir, profiles = b.Profiles })
            .ToList();
        return new { browsers };
    }

    private static object Analyze(Request request)
    {
        if (string.IsNullOrEmpty(request.UserDataDir) || string.IsNullOrEmpty(request.Profile))
        {
            throw new InvalidOperationException("pick a browser and profile first");
        }

        string profileDir = Path.Combine(request.UserDataDir, request.Profile);
        string? cookies = CookiePaths.Locate(profileDir);
        if (cookies is null)
        {
            throw new FileNotFoundException("no Cookies database in this profile");
        }

        string localState = Path.Combine(request.UserDataDir, "Local State");
        string copy = Path.Combine(Path.GetTempPath(), "cm-cookies-" + Guid.NewGuid().ToString("N"));
        CookieFile.CopyConsolidated(cookies, copy);
        try
        {
            return ToDto(MigrationReport.Analyze(copy, profileDir, localState), request.Profile, sample: false);
        }
        finally
        {
            TryDelete(copy);
            TryDelete(copy + "-wal");
        }
    }

    private async Task<object> MigrateAsync(Request request)
    {
        if (string.IsNullOrEmpty(request.UserDataDir) || string.IsNullOrEmpty(request.Profile))
        {
            throw new InvalidOperationException("pick a browser and profile first");
        }

        string? dest = _folderPicker.Pick("Choose an empty destination folder for the migrated profile");
        if (string.IsNullOrEmpty(dest))
        {
            return new { cancelled = true };
        }

        string userDataDir = request.UserDataDir;
        string profile = request.Profile;
        MigrationSummary summary = await Task.Run(() => ProfileMigrator.Migrate(userDataDir, profile, dest));

        return new
        {
            cancelled = false,
            dest,
            filesCopied = summary.FilesCopied,
            totalResealed = summary.TotalResealed,
            stores = summary.Stores
                .Select(s => new { store = s.Store, present = s.Present, resealed = s.Resealed })
                .ToList(),
        };
    }

    private async Task<object> ExportAsync(Request request)
    {
        if (string.IsNullOrEmpty(request.UserDataDir) || string.IsNullOrEmpty(request.Profile))
        {
            throw new InvalidOperationException("pick a browser and profile first");
        }

        if (string.IsNullOrEmpty(request.Passphrase))
        {
            throw new InvalidOperationException("set a passphrase to protect the bundle");
        }

        string? dest = _folderPicker.Pick("Choose an empty folder for the exported bundle");
        if (string.IsNullOrEmpty(dest))
        {
            return new { cancelled = true };
        }

        string userDataDir = request.UserDataDir;
        string profile = request.Profile;
        string browser = string.IsNullOrEmpty(request.Browser) ? "Chromium" : request.Browser;
        string passphrase = request.Passphrase;
        BundleMeta meta = await Task.Run(() => ProfileBundle.Export(
            userDataDir, profile, browser, dest, passphrase, DateTimeOffset.UtcNow.ToString("o")));

        return new
        {
            cancelled = false,
            dest,
            browser = meta.Browser,
            fingerprint = meta.KeyFingerprint,
            cookies = meta.Report.Cookies,
            bearer = meta.Report.V10,
            appBound = meta.Report.V20,
        };
    }

    private async Task<object> ImportAsync(Request request)
    {
        if (string.IsNullOrEmpty(request.Passphrase))
        {
            throw new InvalidOperationException("enter the bundle's passphrase");
        }

        string? bundle = _folderPicker.Pick("Choose the bundle folder to import");
        if (string.IsNullOrEmpty(bundle))
        {
            return new { cancelled = true };
        }

        string? dest = _folderPicker.Pick("Choose an empty destination folder for the imported profile");
        if (string.IsNullOrEmpty(dest))
        {
            return new { cancelled = true };
        }

        string passphrase = request.Passphrase;
        MigrationSummary summary = await Task.Run(() => ProfileBundle.Import(bundle, dest, passphrase));

        return new
        {
            cancelled = false,
            dest,
            filesCopied = summary.FilesCopied,
            totalResealed = summary.TotalResealed,
            stores = summary.Stores
                .Select(s => new { store = s.Store, present = s.Present, resealed = s.Resealed })
                .ToList(),
        };
    }

    private static object ToDto(ProfileReport report, string label, bool sample) => new
    {
        label,
        sample,
        scheme = report.Scheme,
        appBound = report.AppBound,
        total = report.Cookies,
        hosts = report.DistinctHosts,
        v10 = report.V10,
        v20 = report.V20,
        other = report.Other,
        sessionOnly = report.SessionOnly,
        dbsc = report.DeviceBound,
        trustedVault = report.HasTrustedVault,
        sites = report.Sites.Select(s => new { host = s.Host, cookies = s.Cookies, status = s.Status }).ToList(),
        warnings = report.Warnings,
    };

    private static void TryDelete(string path) =>
        Try(() =>
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        });

    // Best-effort cleanup of a temp file; a leftover is harmless.
    private static void Try(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed class Request
    {
        public int Id { get; set; }

        public string Cmd { get; set; } = string.Empty;

        public string? UserDataDir { get; set; }

        public string? Profile { get; set; }

        public string? Passphrase { get; set; }

        public string? Browser { get; set; }
    }

    private sealed class Response
    {
        public int Id { get; set; }

        public bool Ok { get; set; }

        public object? Data { get; set; }

        public string? Error { get; set; }
    }
}
