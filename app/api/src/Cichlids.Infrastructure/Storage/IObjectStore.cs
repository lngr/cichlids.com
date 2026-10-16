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
    /// Uploads content under the given key together with user metadata, overwriting any existing
    /// object with the same key. Metadata keys are lower-case names without any provider prefix.
    /// </summary>
    Task PutAsync(
        string key,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the byte size and the user metadata of the object stored under the given key without
    /// downloading its content, or returns null when no object exists for that key. Metadata keys
    /// are lower-case and carry no provider prefix.
    /// </summary>
    Task<ObjectHead?> HeadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a forward-only stream over the object stored under the given key; disposing the
    /// stream releases the download. Returns null when no object exists for that key, so callers
    /// can treat a missing object as a normal outcome. Throws ObjectTooLargeException when the
    /// object is larger than the configured download cap.
    /// </summary>
    Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies the object stored under the given key into the destination stream, for objects such
    /// as videos that are processed from a local file. The caller names the largest size it
    /// accepts: an object that declares more fails before any byte is copied and a body that
    /// delivers more stops the copy, both with ObjectTooLargeException. Returns false when no
    /// object exists for that key.
    /// </summary>
    Task<bool> DownloadAsync(string key, Stream destination, long maxBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a multipart upload under the given key and returns its upload id. The object appears
    /// under the key only when the upload is completed.
    /// </summary>
    Task<string> BeginMultipartUploadAsync(string key, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads one part of a multipart upload and returns its entity tag. Every part except the
    /// last must be at least 5 MiB; a part is at most 64 MiB and its number between 1 and 10000. Uploading a part number again replaces that part.
    /// </summary>
    Task<string> UploadPartAsync(
        string key, string uploadId, int partNumber, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a multipart upload from its parts, in ascending part number order.
    /// </summary>
    Task CompleteMultipartUploadAsync(
        string key, string uploadId, IReadOnlyList<MultipartPart> parts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards a multipart upload and its uploaded parts. Aborting an upload that does not exist
    /// is not an error.
    /// </summary>
    Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether an object exists under the given key.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the byte size of the object stored under the given key without downloading its
    /// content, or null when no object exists for that key.
    /// </summary>
    Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default);

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

/// <summary>
/// Size and user metadata of a stored object, as returned without downloading its content.
/// </summary>
public sealed record ObjectHead(long Size, IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// One uploaded part of a multipart upload: its number, starting at 1, and its entity tag.
/// </summary>
public sealed record MultipartPart(int PartNumber, string ETag);
