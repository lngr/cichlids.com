namespace Cichlids.Infrastructure.Storage;

/// <summary>
/// Provider-agnostic access to object storage. Implementations talk to the store only through
/// a standard, S3-compatible API surface, so the concrete provider stays a configuration detail.
/// </summary>
public interface IObjectStore
{
    /// <summary>
    /// Uploads content under the given key, overwriting any existing object with the same key.
    /// </summary>
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the object stored under the given key. Returns null when no object exists for
    /// that key instead of throwing, so callers can treat a missing object as a normal outcome.
    /// </summary>
    Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether an object exists under the given key.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the object stored under the given key. Deleting a key that does not exist is not
    /// an error.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams every key stored under the given prefix, transparently paging through the
    /// underlying provider's result pages.
    /// </summary>
    IAsyncEnumerable<string> ListKeysAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the public URL for the given key from the configured public base URL. The key is
    /// not presigned; the object must already be reachable at that URL through the delivery path
    /// configured for the store (for example a CDN in front of the bucket).
    /// </summary>
    string GetPublicUrl(string key);
}
