using SessionMigrate.Core.Tpm;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class TpmTests
{
    [Fact]
    public void ExtractKeyBlob_FindsField5PcpmBlob()
    {
        byte[] pcpm = new byte[480];
        "PCPM"u8.CopyTo(pcpm);
        for (int i = 4; i < pcpm.Length; i++)
        {
            pcpm[i] = (byte)(i % 251);
        }

        // proto: a junk field-1 varint, then field-5 (tag 0x2A) + varint 480 (0xE0 0x03) + the blob.
        var proto = new List<byte> { 0x08, 0x01, 0x2A, 0xE0, 0x03 };
        proto.AddRange(pcpm);

        byte[]? extracted = DeviceBoundSessions.ExtractKeyBlob(proto.ToArray());

        Assert.NotNull(extracted);
        Assert.Equal(pcpm, extracted);
    }

    [Fact]
    public void ExtractKeyBlob_ReturnsNull_WhenNoPcpmField()
    {
        Assert.Null(DeviceBoundSessions.ExtractKeyBlob([0x08, 0x01, 0x2A, 0x05, 1, 2, 3, 4, 5]));
    }

    [Fact]
    public void Check_RejectsNonPcpmBlob_WithoutTouchingNCrypt()
    {
        // All-zero blob: no "PCPM" magic, so the guard fails it without calling the provider (no hang).
        TpmKeyCheckResult result = TpmKeyCheck.Check(new byte[480]);

        Assert.False(result.Passed);
        Assert.Equal(-1, result.ImportStatus);
    }
}
