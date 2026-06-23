using System.Text.Json;
using System.Text.Json.Serialization;

namespace SessionMigrate.Core.Harvest;

// A plaintext cookie as harvested from a live browser (the chrome.cookies shape the export extension
// POSTs). The browser has already decrypted it, including App-Bound (v20) and httpOnly.
public sealed record HarvestedCookie
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = "/";

    [JsonPropertyName("secure")]
    public bool Secure { get; init; }

    [JsonPropertyName("httpOnly")]
    public bool HttpOnly { get; init; }

    [JsonPropertyName("hostOnly")]
    public bool HostOnly { get; init; }

    [JsonPropertyName("session")]
    public bool Session { get; init; }

    [JsonPropertyName("sameSite")]
    public string? SameSite { get; init; }

    [JsonPropertyName("expirationDate")]
    public double? ExpirationDate { get; init; }

    [JsonPropertyName("partitionKey")]
    public JsonElement? PartitionKey { get; init; }

    // True if this is a CHIPS/partitioned cookie (skipped when injecting auth cookies).
    [JsonIgnore]
    public bool IsPartitioned =>
        PartitionKey is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };
}

// One harvest POST: the trigger that fired it and the cookies it carried.
public sealed record HarvestResult(
    [property: JsonPropertyName("trigger")] string Trigger,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("cookies")] IReadOnlyList<HarvestedCookie> Cookies);
