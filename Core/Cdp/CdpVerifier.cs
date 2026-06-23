using System.Diagnostics;
using System.Text.Json;

namespace SessionMigrate.Core.Cdp;

// A page to open and a JS probe whose return value signals login state.
public sealed record VerifyTarget(string Name, string Url, string Expr);

public sealed record VerifyResult(string Name, string Url, string? Value);

// Verifies login state by opening each target page (via Target.createTarget), waiting for it to load,
// then evaluating a probe expression. The probe's return value (e.g. "LOGGED_IN:user" vs "ANON") is
// interpreted by the caller — the convention lives in the targets, not here.
public static class CdpVerifier
{
    public static async Task<List<VerifyResult>> VerifyAsync(
        CdpClient client,
        IEnumerable<VerifyTarget> targets,
        int loadTimeoutMs = 15000,
        int settleMs = 800,
        CancellationToken cancellationToken = default)
    {
        List<VerifyResult> results = [];
        foreach (VerifyTarget target in targets)
        {
            string targetId = (await client.SendAsync(
                "Target.createTarget", new { url = target.Url }, cancellationToken: cancellationToken))
                .GetProperty("targetId").GetString()!;
            try
            {
                string sessionId = (await client.SendAsync(
                    "Target.attachToTarget", new { targetId, flatten = true }, cancellationToken: cancellationToken))
                    .GetProperty("sessionId").GetString()!;

                await client.SendAsync("Page.enable", sessionId: sessionId, cancellationToken: cancellationToken);
                await client.SendAsync("Runtime.enable", sessionId: sessionId, cancellationToken: cancellationToken);

                await WaitForLoadAsync(client, sessionId, loadTimeoutMs, cancellationToken);
                await Task.Delay(settleMs, cancellationToken);

                results.Add(new VerifyResult(
                    target.Name, target.Url, await ProbeAsync(client, sessionId, target.Expr, cancellationToken)));
            }
            finally
            {
                // Always close the tab we opened, even if the probe threw.
                try
                {
                    await client.SendAsync("Target.closeTarget", new { targetId }, cancellationToken: cancellationToken);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        return results;
    }

    private static async Task WaitForLoadAsync(
        CdpClient client, string sessionId, int loadTimeoutMs, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < loadTimeoutMs)
        {
            JsonElement result = await client.SendAsync(
                "Runtime.evaluate",
                new { expression = "document.readyState", returnByValue = true },
                sessionId,
                cancellationToken);
            if (TryGetValue(result, out string? value) && value == "complete")
            {
                return;
            }

            await Task.Delay(300, cancellationToken);
        }
    }

    private static async Task<string?> ProbeAsync(
        CdpClient client, string sessionId, string expression, CancellationToken cancellationToken)
    {
        try
        {
            JsonElement result = await client.SendAsync(
                "Runtime.evaluate",
                new { expression, awaitPromise = true, returnByValue = true },
                sessionId,
                cancellationToken);
            return TryGetValue(result, out string? value) ? value : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryGetValue(JsonElement evaluateResult, out string? value)
    {
        value = null;
        if (evaluateResult.TryGetProperty("result", out JsonElement inner) &&
            inner.TryGetProperty("value", out JsonElement v))
        {
            value = v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString();
            return true;
        }

        return false;
    }
}
