using System.Net;
using System.Net.Sockets;

namespace Cichlids.Api.Tests.Features.Webhooks;

internal sealed record CapturedWebhookRequest(string Body, string? Signature);

/// <summary>
/// A minimal loopback HTTP server standing in for a webhook subscriber: it captures every
/// request body and signature header it receives and can be told to fail a fixed number of
/// deliveries before it starts accepting them, so dispatcher retry behavior can be exercised
/// without a real external service.
/// </summary>
internal sealed class TestWebhookListener : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly List<CapturedWebhookRequest> _requests = [];
    private readonly Lock _gate = new();
    private CancellationTokenSource _cts = new();
    private Task _acceptLoop = Task.CompletedTask;
    private int _failFirstNDeliveries;

    public TestWebhookListener()
    {
        var port = ReserveFreeTcpPort();
        Uri = new Uri($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add(Uri.ToString());
    }

    public Uri Uri { get; }

    public IReadOnlyList<CapturedWebhookRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToList();
            }
        }
    }

    /// <summary>
    /// The next <paramref name="count"/> deliveries respond with 500; every delivery after that
    /// responds with 200.
    /// </summary>
    public void FailNextDeliveries(int count) => _failFirstNDeliveries = count;

    public void Start()
    {
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var signature = context.Request.Headers["X-Cichlids-Signature"];

            bool shouldFail;
            lock (_gate)
            {
                _requests.Add(new CapturedWebhookRequest(body, signature));
                shouldFail = _failFirstNDeliveries > 0;
                if (shouldFail)
                {
                    _failFirstNDeliveries--;
                }
            }

            context.Response.StatusCode = shouldFail ? (int)HttpStatusCode.InternalServerError : (int)HttpStatusCode.OK;
            context.Response.Close();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        _listener.Close();
        try
        {
            await _acceptLoop;
        }
        catch
        {
            // The accept loop's own exception handling already covers a clean shutdown; nothing
            // further to observe here once it has stopped.
        }
    }

    private static int ReserveFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
