using Cichlids.Infrastructure.Storage;

namespace Cichlids.Api.Tests.Features.Tanks;

/// <summary>
/// An object store that forwards to another one and can misbehave on demand: run a hook before a put,
/// refuse puts whose key matches, or silently ignore deletes.
/// </summary>
internal sealed class FlakyObjectStore(IObjectStore inner) : IObjectStore
{
    /// <summary>Runs before every put with the key; the put goes ahead afterwards.</summary>
    public Func<string, Task>? BeforePut { get; set; }

    /// <summary>Refuses (throws on) every put whose key starts with this prefix while set.</summary>
    public string? RefusePutsUnder { get; set; }

    /// <summary>How many puts under the refused prefix succeed before the refusal starts.</summary>
    public int AllowedPutsBeforeRefusal { get; set; }

    /// <summary>Makes every delete a silent no-op while set.</summary>
    public bool IgnoreDeletes { get; set; }

    private int _putsUnderRefusedPrefix;

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
        await PutAsync(key, content, contentType, new Dictionary<string, string>(), cancellationToken);

    public async Task PutAsync(
        string key, Stream content, string contentType, IReadOnlyDictionary<string, string> metadata, CancellationToken cancellationToken = default)
    {
        if (BeforePut is { } hook) await hook(key);
        if (RefusePutsUnder is { } prefix && key.StartsWith(prefix, StringComparison.Ordinal)
            && Interlocked.Increment(ref _putsUnderRefusedPrefix) > AllowedPutsBeforeRefusal)
        {
            throw new IOException($"The store refused the put of '{key}'.");
        }

        await inner.PutAsync(key, content, contentType, metadata, cancellationToken);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        IgnoreDeletes ? Task.CompletedTask : inner.DeleteAsync(key, cancellationToken);

    public Task<ObjectHead?> HeadAsync(string key, CancellationToken cancellationToken = default) => inner.HeadAsync(key, cancellationToken);
    public Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default) => inner.GetAsync(key, cancellationToken);
    public Task<bool> DownloadAsync(string key, Stream destination, long maxBytes, CancellationToken cancellationToken = default) =>
        inner.DownloadAsync(key, destination, maxBytes, cancellationToken);
    public Task<string> BeginMultipartUploadAsync(string key, string contentType, CancellationToken cancellationToken = default) =>
        inner.BeginMultipartUploadAsync(key, contentType, cancellationToken);
    public Task<string> UploadPartAsync(string key, string uploadId, int partNumber, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default) =>
        inner.UploadPartAsync(key, uploadId, partNumber, content, cancellationToken);
    public Task CompleteMultipartUploadAsync(string key, string uploadId, IReadOnlyList<MultipartPart> parts, CancellationToken cancellationToken = default) =>
        inner.CompleteMultipartUploadAsync(key, uploadId, parts, cancellationToken);
    public Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken cancellationToken = default) =>
        inner.AbortMultipartUploadAsync(key, uploadId, cancellationToken);
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) => inner.ExistsAsync(key, cancellationToken);
    public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default) => inner.GetSizeAsync(key, cancellationToken);
    public IAsyncEnumerable<string> ListKeysAsync(string prefix, CancellationToken cancellationToken = default) => inner.ListKeysAsync(prefix, cancellationToken);
    public string GetPublicUrl(string key) => inner.GetPublicUrl(key);
}
