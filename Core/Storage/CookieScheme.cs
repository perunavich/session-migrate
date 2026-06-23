namespace SessionMigrate.Core.Storage;

// Classifies a cookie's encrypted_value by its leading marker: v10 (DPAPI/AES on Windows), v11 (Linux
// keyring), v20 (App-Bound), or plain (unencrypted).
public static class CookieScheme
{
    public static string Of(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 3 || blob[0] != (byte)'v')
        {
            return "plain";
        }

        return (blob[1], blob[2]) switch
        {
            ((byte)'1', (byte)'0') => "v10",
            ((byte)'1', (byte)'1') => "v11",
            ((byte)'2', (byte)'0') => "v20",
            _ => "plain",
        };
    }

    // True if a row can be resealed from a file: v10 (Windows DPAPI) or the byte-identical v11. v20 is
    // App-Bound and must go through the live harvest path.
    public static bool IsResealable(ReadOnlySpan<byte> blob)
    {
        string scheme = Of(blob);
        return scheme is "v10" or "v11";
    }
}
