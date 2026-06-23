using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace SessionMigrate.Core.Storage;

// What a snapshot produced: where the frozen copies landed and which items were captured.
public sealed record SnapshotResult(string DestDir, IReadOnlyList<string> Items);

// Copies profile items from a Volume Shadow Copy — the consistent way to read files a running browser
// holds exclusively locked (notably Network\Cookies). Needs administrator rights; the shadow copy is
// always torn down afterwards. For an unlocked profile, the live ProfileCopier path is simpler and
// needs no elevation.
[SupportedOSPlatform("windows")]
public static class VssSnapshot
{
    // True if the current process is elevated (required to create a shadow copy).
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static SnapshotResult Capture(
        string sourceBase, IEnumerable<string> items, string profile, string destDir)
    {
        if (!IsElevated())
        {
            throw new InvalidOperationException("a VSS snapshot needs administrator rights");
        }

        string volume = Path.GetPathRoot(Path.GetFullPath(sourceBase))
            ?? throw new InvalidOperationException("cannot resolve the source volume");

        (string shadowId, string deviceObject) = CreateShadow(volume);
        string link = Path.Combine(Path.GetTempPath(), "vss_" + Guid.NewGuid().ToString("N"));
        try
        {
            if (RunCmd($"mklink /d \"{link}\" \"{deviceObject}\\\"") != 0)
            {
                throw new InvalidOperationException("failed to link the shadow copy (mklink)");
            }

            string relative = Path.GetFullPath(sourceBase)[volume.Length..];
            string snapBase = Path.Combine(link, relative, profile);

            List<string> captured = [];
            foreach (string item in items)
            {
                string source = Path.Combine(snapBase, item);
                string dest = Path.Combine(destDir, item);
                if (Directory.Exists(source))
                {
                    ProfileCopier.CopyTree(source, dest);
                    captured.Add(item);
                }
                else if (File.Exists(source))
                {
                    ProfileCopier.CopyFile(source, dest);
                    captured.Add(item);
                }
            }

            return new SnapshotResult(destDir, captured);
        }
        finally
        {
            RunCmd($"rmdir \"{link}\"");
            DeleteShadow(shadowId);
        }
    }

    private static (string ShadowId, string DeviceObject) CreateShadow(string volume)
    {
        using var shadowClass = new ManagementClass("Win32_ShadowCopy");
        using ManagementBaseObject input = shadowClass.GetMethodParameters("Create");
        input["Volume"] = volume;
        input["Context"] = "ClientAccessible";

        using ManagementBaseObject output = shadowClass.InvokeMethod("Create", input, null);
        if (Convert.ToUInt32(output["ReturnValue"]) != 0)
        {
            throw new InvalidOperationException($"Win32_ShadowCopy.Create failed: {output["ReturnValue"]}");
        }

        string shadowId = (string)output["ShadowID"];
        using var searcher = new ManagementObjectSearcher("SELECT ID, DeviceObject FROM Win32_ShadowCopy");
        foreach (ManagementBaseObject row in searcher.Get())
        {
            using (row)
            {
                if ((string)row["ID"] == shadowId)
                {
                    return (shadowId, (string)row["DeviceObject"]);
                }
            }
        }

        throw new InvalidOperationException("created shadow copy not found");
    }

    private static void DeleteShadow(string shadowId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT * FROM Win32_ShadowCopy WHERE ID='{shadowId}'");
            foreach (ManagementBaseObject row in searcher.Get())
            {
                using (row)
                {
                    ((ManagementObject)row).Delete();
                }
            }
        }
        catch (ManagementException)
        {
            // Best-effort teardown.
        }
    }

    private static int RunCmd(string command)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c {command}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        });
        process?.WaitForExit();
        return process?.ExitCode ?? -1;
    }
}
