using System.Collections.Concurrent;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Wraps an object store and records the keys that were uploaded, downloaded and looked up through
/// it, so a test can assert that a run left the store untouched.
/// </summary>
internal sealed class RecordingObjectStore(IObjectStore inner) : IObjectStore
{
    private readonly ConcurrentQueue<string> _puts = new();
    private readonly ConcurrentQueue<string> _gets = new();
    private readonly ConcurrentQueue<string> _heads = new();

    /// <summary>
    /// Keys whose download returns null although a head call still finds them, as when an object
    /// is deleted between the two calls.
    /// </summary>
    public HashSet<string> VanishOnGet { get; } = [];

    /// <summary>
    /// Keys whose download fails with ObjectTooLargeException, as for an object above the
    /// configured download cap.
    /// </summary>
    public HashSet<string> TooLargeOnGet { get; } = [];

    public IReadOnlyCollection<string> PutKeys => _puts;

    public IReadOnlyCollection<string> GetKeys => _gets;

    public IReadOnlyCollection<string> HeadKeys => _heads;

    public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        _puts.Enqueue(key);
        return inner.PutAsync(key, content, contentType, cancellationToken);
    }

    public Task PutAsync(
        string key,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        _puts.Enqueue(key);
        return inner.PutAsync(key, content, contentType, metadata, cancellationToken);
    }

    public Task<ObjectHead?> HeadAsync(string key, CancellationToken cancellationToken = default)
    {
        _heads.Enqueue(key);
        return inner.HeadAsync(key, cancellationToken);
    }

    public Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        _gets.Enqueue(key);
        if (TooLargeOnGet.Contains(key))
        {
            throw new ObjectTooLargeException(key, 1);
        }

        return VanishOnGet.Contains(key) ? Task.FromResult<Stream?>(null) : inner.GetAsync(key, cancellationToken);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        inner.ExistsAsync(key, cancellationToken);

    public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default) =>
        inner.GetSizeAsync(key, cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        inner.DeleteAsync(key, cancellationToken);

    public IAsyncEnumerable<string> ListKeysAsync(string prefix, CancellationToken cancellationToken = default) =>
        inner.ListKeysAsync(prefix, cancellationToken);

    public string GetPublicUrl(string key) => inner.GetPublicUrl(key);
}
