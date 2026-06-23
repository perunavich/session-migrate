using Microsoft.Data.Sqlite;
using SessionMigrate.Core.Storage;

namespace SessionMigrate.Core.Tpm;

// Reads the TPM-protected key blob out of a Chromium "Device Bound Sessions" store. The session
// protobuf carries the key as field 5 (length-delimited, exactly 480 bytes) beginning with the PCPM
// magic (BCRYPT_PCP_KEY_MAGIC) of a Platform Crypto Provider key.
public static class DeviceBoundSessions
{
    private const int PcpmLength = 480;
    private const byte Field5Tag = 0x2A; // field 5, wire type 2 (length-delimited)
    private static readonly byte[] PcpmMagic = "PCPM"u8.ToArray();

    // Reads every PCPM key blob from a Device Bound Sessions SQLite file.
    public static IReadOnlyList<byte[]> ReadKeyBlobs(string dbPath)
    {
        using SqliteConnection conn = CookieDb.Open(dbPath, SqliteOpenMode.ReadOnly);
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT proto FROM dbsc_session_tbl";
        using SqliteDataReader reader = cmd.ExecuteReader();

        List<byte[]> blobs = [];
        while (reader.Read())
        {
            if (reader.IsDBNull(0))
            {
                continue;
            }

            byte[]? key = ExtractKeyBlob(reader.GetFieldValue<byte[]>(0));
            if (key is not null)
            {
                blobs.Add(key);
            }
        }

        return blobs;
    }

    // Extracts the 480-byte PCPM key blob from a session protobuf, or null if not present. Validating
    // tag + length + magic guarantees the exact slice the TPM import requires (an over-long slice makes
    // NCrypt hang).
    public static byte[]? ExtractKeyBlob(byte[] proto)
    {
        ArgumentNullException.ThrowIfNull(proto);
        for (int i = 0; i < proto.Length; i++)
        {
            if (proto[i] != Field5Tag)
            {
                continue;
            }

            (int length, int varintBytes) = ReadVarint(proto, i + 1);
            if (length != PcpmLength)
            {
                continue;
            }

            int start = i + 1 + varintBytes;
            if (start + PcpmLength > proto.Length)
            {
                continue;
            }

            if (proto.AsSpan(start, PcpmMagic.Length).SequenceEqual(PcpmMagic))
            {
                return proto[start..(start + PcpmLength)];
            }
        }

        return null;
    }

    private static (int Value, int Bytes) ReadVarint(byte[] data, int offset)
    {
        int value = 0, shift = 0, index = offset;
        while (index < data.Length && shift < 32)
        {
            byte b = data[index++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                break;
            }

            shift += 7;
        }

        return (value, index - offset);
    }
}
