using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SessionMigrate.Core.Tpm;

// Outcome of a TPM key health check: the NCrypt status codes and whether a signature verified.
public sealed record TpmKeyCheckResult(int ImportStatus, int SignStatus, bool Verified)
{
    public bool Passed => ImportStatus == 0 && SignStatus == 0 && Verified;
}

// Proves a Chromium DBSC device-bound key is usable on this machine: re-imports the PCPM blob into the
// TPM via the Microsoft Platform Crypto Provider, signs a hash, and verifies the signature with the
// exported public key. Same check that confirms the key survives an OS reinstall — uses only the
// documented NCrypt API, never extracts the private key (the TPM can't).
public static class TpmKeyCheck
{
    private const uint NcryptSilent = 0x40;
    private const string Provider = "Microsoft Platform Crypto Provider";
    private const int PcpmLength = 480;

    // Imports the blob, signs hash (default a fixed test digest), and verifies. The blob must be the
    // exact 480-byte PCPM slice.
    public static TpmKeyCheckResult Check(byte[] pcpmKeyBlob, byte[]? hash = null)
    {
        ArgumentNullException.ThrowIfNull(pcpmKeyBlob);

        // Hard guard: never hand NCrypt an oversized/garbage blob — it hangs the provider.
        if (pcpmKeyBlob.Length != PcpmLength ||
            !pcpmKeyBlob.AsSpan(0, 4).SequenceEqual("PCPM"u8))
        {
            return new TpmKeyCheckResult(-1, -1, false);
        }

        hash ??= SHA256.HashData(Encoding.UTF8.GetBytes("livetest-keycheck"));

        IntPtr provider = IntPtr.Zero;
        IntPtr key = IntPtr.Zero;
        try
        {
            if (NCryptOpenStorageProvider(out provider, Provider, 0) != 0)
            {
                return new TpmKeyCheckResult(-1, -1, false);
            }

            int importStatus = NCryptImportKey(
                provider, IntPtr.Zero, "OpaqueKeyBlob", IntPtr.Zero, out key,
                pcpmKeyBlob, pcpmKeyBlob.Length, NcryptSilent);
            if (importStatus != 0)
            {
                return new TpmKeyCheckResult(importStatus, -1, false);
            }

            byte[] publicBlob = ExportPublicKey(key);

            int signStatus = NCryptSignHash(key, IntPtr.Zero, hash, hash.Length, null, 0, out int sigLen, NcryptSilent);
            if (signStatus != 0)
            {
                return new TpmKeyCheckResult(importStatus, signStatus, false);
            }

            byte[] signature = new byte[sigLen];
            signStatus = NCryptSignHash(key, IntPtr.Zero, hash, hash.Length, signature, sigLen, out sigLen, NcryptSilent);
            if (signStatus != 0)
            {
                return new TpmKeyCheckResult(importStatus, signStatus, false);
            }

            using var cng = CngKey.Import(publicBlob, CngKeyBlobFormat.EccPublicBlob);
            using var ecdsa = new ECDsaCng(cng);
            bool verified = ecdsa.VerifyHash(hash, signature);
            return new TpmKeyCheckResult(importStatus, signStatus, verified);
        }
        finally
        {
            if (key != IntPtr.Zero)
            {
                NCryptFreeObject(key);
            }

            if (provider != IntPtr.Zero)
            {
                NCryptFreeObject(provider);
            }
        }
    }

    private static byte[] ExportPublicKey(IntPtr key)
    {
        NCryptExportKey(key, IntPtr.Zero, "ECCPUBLICBLOB", IntPtr.Zero, null, 0, out int length, 0);
        byte[] blob = new byte[length];
        NCryptExportKey(key, IntPtr.Zero, "ECCPUBLICBLOB", IntPtr.Zero, blob, length, out length, 0);
        return blob;
    }

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int NCryptOpenStorageProvider(out IntPtr phProvider, string pszProviderName, uint dwFlags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int NCryptImportKey(
        IntPtr hProvider, IntPtr hImportKey, string pszBlobType, IntPtr pParameterList,
        out IntPtr phKey, byte[] pbData, int cbData, uint dwFlags);

    [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
    private static extern int NCryptExportKey(
        IntPtr hKey, IntPtr hExportKey, string pszBlobType, IntPtr pParameterList,
        byte[]? pbOutput, int cbOutput, out int pcbResult, uint dwFlags);

    [DllImport("ncrypt.dll")]
    private static extern int NCryptSignHash(
        IntPtr hKey, IntPtr pPaddingInfo, byte[] pbHashValue, int cbHashValue,
        byte[]? pbSignature, int cbSignature, out int pcbResult, uint dwFlags);

    [DllImport("ncrypt.dll")]
    private static extern int NCryptFreeObject(IntPtr hObject);
}
