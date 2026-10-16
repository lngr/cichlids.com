using System.Collections.Concurrent;
using System.Net;

namespace Cichlids.Api.Tests.Features.WebPush;

/// <summary>A request a push service received: where it went, its headers and its encrypted body.</summary>
internal sealed record PushRequest(Uri Url, IReadOnlyDictionary<string, string> Headers, byte[] Body);

/// <summary>
/// Stands in for the push services of the browsers: records every request and answers with a status
/// chosen per endpoint (201 Created by default). Delivery through a real push service is not part
/// of the tests.
/// </summary>
internal sealed class FakePushService : HttpMessageHandler
{
    private readonly ConcurrentQueue<PushRequest> _requests = new();
    private readonly ConcurrentDictionary<string, (HttpStatusCode Status, int? RetryAfterSeconds)> _answers = new();

    public IReadOnlyCollection<PushRequest> Requests => _requests;

    public IReadOnlyList<PushRequest> RequestsFor(string endpoint) => _requests.Where(r => r.Url.AbsoluteUri == endpoint).ToList();

    public void Answer(string endpoint, HttpStatusCode status, int? retryAfterSeconds = null) => _answers[endpoint] = (status, retryAfterSeconds);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        _requests.Enqueue(new PushRequest(request.RequestUri!, headers, body));
        var (status, retryAfter) = _answers.GetValueOrDefault(request.RequestUri!.AbsoluteUri, (HttpStatusCode.Created, null));
        var response = new HttpResponseMessage(status);
        if (retryAfter is { } seconds) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        return response;
    }
}
