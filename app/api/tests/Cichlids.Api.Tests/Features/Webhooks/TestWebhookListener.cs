using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Cichlids.Api.Tests.Features.Webhooks;

/// <summary>
/// One request the listener received, with the status it answered.
/// </summary>
internal sealed record CapturedWebhookRequest(string Body, string? Signature, int StatusCode)
{
    public bool Succeeded => StatusCode is >= 200 and < 300;

    /// <summary>
    /// The "id" of the delivery envelope, which is the outbox event id.
    /// </summary>
    public Guid EventId
    {
        get
        {
            using var envelope = JsonDocument.Parse(Body);
            return envelope.RootElement.GetProperty("id").GetGuid();
        }
    }
}

/// <summary>
/// A minimal loopback HTTP server standing in for a webhook subscriber. It captures every request
/// body and signature header it receives together with the status it answered. It answers 200 by
/// default and can be told to fail a fixed number of deliveries, to be down (503), to answer with
/// any status and Location header, or to hold requests until they are released.
/// </summary>
internal sealed class TestWebhookListener : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly List<CapturedWebhookRequest> _requests = [];
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private Task _acceptLoop = Task.CompletedTask;
    private int _failFirstNDeliveries;
    private bool _down;
    private int _statusCode = (int)HttpStatusCode.OK;
    private string? _location;
    private TaskCompletionSource _release = CompletedRelease();
    private TaskCompletionSource _requestArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
    /// The requests answered with a success status.
    /// </summary>
    public IReadOnlyList<CapturedWebhookRequest> AcceptedRequests => Requests.Where(r => r.Succeeded).ToList();

    /// <summary>
    /// The next count deliveries respond with 500; every delivery after that responds normally.
    /// </summary>
    public void FailNextDeliveries(int count) => _failFirstNDeliveries = count;

    /// <summary>
    /// While down, every delivery responds with 503.
    /// </summary>
    public void SetDown(bool down) => _down = down;

    /// <summary>
    /// Every delivery that is neither failed nor refused while down responds with this status and,
    /// when given, this Location header.
    /// </summary>
    public void RespondWith(HttpStatusCode statusCode, Uri? location = null)
    {
        _statusCode = (int)statusCode;
        _location = location?.ToString();
    }

    /// <summary>
    /// Holds every following request open until Release is called.
    /// </summary>
    public void HoldRequests()
    {
        lock (_gate)
        {
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _requestArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Release()
    {
        lock (_gate)
        {
            _release.TrySetResult();
        }
    }

    /// <summary>
    /// Completes once a request arrived after the last call to HoldRequests.
    /// </summary>
    public Task RequestArrived
    {
        get
        {
            lock (_gate)
            {
                return _requestArrived.Task;
            }
        }
    }

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

            _ = Task.Run(() => RespondAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task RespondAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var signature = context.Request.Headers["X-Cichlids-Signature"];

            Task release;
            lock (_gate)
            {
                release = _release.Task;
                _requestArrived.TrySetResult();
            }

            await release.WaitAsync(cancellationToken);

            int statusCode;
            string? location = null;
            lock (_gate)
            {
                if (_failFirstNDeliveries > 0)
                {
                    _failFirstNDeliveries--;
                    statusCode = (int)HttpStatusCode.InternalServerError;
                }
                else if (_down)
                {
                    statusCode = (int)HttpStatusCode.ServiceUnavailable;
                }
                else
                {
                    statusCode = _statusCode;
                    location = _location;
                }

                _requests.Add(new CapturedWebhookRequest(body, signature, statusCode));
            }

            context.Response.StatusCode = statusCode;
            if (location is not null)
            {
                context.Response.RedirectLocation = location;
            }

            context.Response.Close();
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // The listener is shutting down; the request is abandoned.
        }
        catch (HttpListenerException)
        {
            // The client gave up on the request, for example after its timeout.
        }
        catch (ObjectDisposedException)
        {
            // The client gave up on the request, for example after its timeout.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        Release();
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

    private static TaskCompletionSource CompletedRelease()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        release.SetResult();
        return release;
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
