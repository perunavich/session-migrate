using System.Text.Json;
using SessionMigrate.Core.Harvest;

namespace SessionMigrate.Core.Cdp;

// Harvests cookies from a live browser via CDP (which returns them already decrypted, including
// App-Bound v20 and in-memory session cookies) and applies them to another browser. The browser
// flushes its cookie store on a graceful Browser.close.
public static class CdpCookies
{
    public static async Task<List<HarvestedCookie>> HarvestAsync(CdpClient client, CancellationToken cancellationToken = default)
    {
        JsonElement result = await client.SendAsync("Storage.getCookies", new { }, cancellationToken: cancellationToken);

        List<HarvestedCookie> cookies = [];
        foreach (JsonElement cookie in result.GetProperty("cookies").EnumerateArray())
        {
            cookies.Add(MapFromCdp(cookie));
        }

        return cookies;
    }

    public static async Task ApplyAsync(
        CdpClient client, IEnumerable<HarvestedCookie> cookies, CancellationToken cancellationToken = default)
    {
        var parameters = cookies.Select(ToCdpParam).ToList();
        await client.SendAsync("Storage.setCookies", new { cookies = parameters }, cancellationToken: cancellationToken);
    }

    private static HarvestedCookie MapFromCdp(JsonElement c) => new()
    {
        Name = GetString(c, "name"),
        Value = GetString(c, "value"),
        Domain = GetString(c, "domain"),
        Path = GetString(c, "path", "/"),
        Secure = GetBool(c, "secure"),
        HttpOnly = GetBool(c, "httpOnly"),
        Session = GetBool(c, "session"),
        SameSite = c.TryGetProperty("sameSite", out JsonElement ss) ? ss.GetString() : null,
        ExpirationDate = c.TryGetProperty("expires", out JsonElement e) && e.ValueKind == JsonValueKind.Number
            ? e.GetDouble()
            : null,
        PartitionKey = c.TryGetProperty("partitionKey", out JsonElement pk) ? pk.Clone() : null,
    };

    private static object ToCdpParam(HarvestedCookie c)
    {
        var p = new Dictionary<string, object?>
        {
            ["name"] = c.Name,
            ["value"] = c.Value,
            ["domain"] = c.Domain,
            ["path"] = c.Path,
            ["secure"] = c.Secure,
            ["httpOnly"] = c.HttpOnly,
        };

        if (!string.IsNullOrEmpty(c.SameSite))
        {
            p["sameSite"] = c.SameSite;
        }

        if (c.ExpirationDate is > 0)
        {
            p["expires"] = c.ExpirationDate;
        }

        return p;
    }

    private static string GetString(JsonElement element, string name, string fallback = "") =>
        element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.True;
}
