using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SessionMigrate.Core.Harvest;

// A tiny loopback receiver for the cookie-export extension. Raw TcpListener on 127.0.0.1 (no
// HttpListener URL ACL, no admin), parses the extension's POST /cookies, and keeps the LATEST harvest
// (last-write-wins) — the extension fires once at startup and re-fires after the bound-cookie rotation
// settles, and we want the newest snapshot.
public sealed class CookieReceiver : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly TcpListener _listener;

    // Starts listening immediately. Pass port 0 for an ephemeral port (tests).
    public CookieReceiver(int port = 8765)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
    }

    // The port actually bound (useful when constructed with port 0).
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    // The most recent harvest received, or null until the first POST.
    public HarvestResult? Latest { get; private set; }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using (client)
            {
                await HandleAsync(client, cancellationToken);
            }
        }
    }

    public void Dispose() => _listener.Stop();

    private async Task HandleAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using NetworkStream stream = client.GetStream();
        byte[]? body = await ReadRequestBodyAsync(stream, cancellationToken);
        if (body is not null)
        {
            try
            {
                HarvestResult? result = JsonSerializer.Deserialize<HarvestResult>(body, Json);
                if (result is not null)
                {
                    Latest = result;
                }
            }
            catch (JsonException)
            {
                // Ignore a malformed POST; the next harvest overwrites it.
            }
        }

        // Always ack the POST we just consumed, even if shutdown was requested mid-request.
        byte[] response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\nAccess-Control-Allow-Origin: *\r\n\r\nok"u8.ToArray();
        await stream.WriteAsync(response, CancellationToken.None);
    }

    private static async Task<byte[]?> ReadRequestBodyAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[65536];
        using var accumulated = new MemoryStream();
        int headerEnd = -1;
        int contentLength = -1;

        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
            {
                break;
            }

            accumulated.Write(buffer, 0, read);
            byte[] data = accumulated.GetBuffer();
            int length = (int)accumulated.Length;

            if (headerEnd < 0)
            {
                headerEnd = FindHeaderEnd(data, length);
                if (headerEnd >= 0)
                {
                    contentLength = ParseContentLength(Encoding.ASCII.GetString(data, 0, headerEnd));
                }
            }

            if (headerEnd >= 0)
            {
                int bodyStart = headerEnd + 4;
                if (contentLength < 0 || length - bodyStart >= contentLength)
                {
                    if (contentLength <= 0)
                    {
                        return null;
                    }

                    byte[] body = new byte[contentLength];
                    Array.Copy(data, bodyStart, body, 0, contentLength);
                    return body;
                }
            }
        }

        return null;
    }

    private static int FindHeaderEnd(byte[] data, int length)
    {
        for (int i = 0; i + 3 < length; i++)
        {
            if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
            {
                return i;
            }
        }

        return -1;
    }

    private static int ParseContentLength(string headers)
    {
        foreach (string line in headers.Split("\r\n"))
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line["Content-Length:".Length..].Trim(), out int length))
            {
                return length;
            }
        }

        return -1;
    }
}
