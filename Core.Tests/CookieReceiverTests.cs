using System.Net.Http;
using System.Text;
using SessionMigrate.Core.Harvest;
using Xunit;

namespace SessionMigrate.Core.Tests;

public class CookieReceiverTests
{
    [Fact]
    public async Task ReceivesAndParsesAHarvestPost()
    {
        using var receiver = new CookieReceiver(port: 0);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task run = receiver.RunAsync(cts.Token);

        const string Json = """
            {"trigger":"settled","count":1,"cookies":[
              {"name":"SID","value":"abc","domain":".google.com","path":"/","secure":true,"httpOnly":true,
               "expirationDate":1893456000.5}]}
            """;
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) })
        {
            using var content = new StringContent(Json, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.PostAsync(
                $"http://127.0.0.1:{receiver.Port}/cookies", content);
            response.EnsureSuccessStatusCode();
        }

        for (int i = 0; i < 100 && receiver.Latest is null; i++)
        {
            await Task.Delay(20);
        }

        cts.Cancel();
        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.NotNull(receiver.Latest);
        Assert.Equal("settled", receiver.Latest!.Trigger);
        HarvestedCookie cookie = Assert.Single(receiver.Latest.Cookies);
        Assert.Equal("SID", cookie.Name);
        Assert.Equal(".google.com", cookie.Domain);
        Assert.True(cookie.HttpOnly);
        Assert.Equal(1893456000.5, cookie.ExpirationDate);
        Assert.False(cookie.IsPartitioned);
    }
}
