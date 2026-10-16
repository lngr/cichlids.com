using Cichlids.Infrastructure.Storage;

namespace Cichlids.Api.Tests;

/// <summary>
/// An object store that only builds public URLs under the given base URL; every storage operation
/// fails, since URL building never reaches the store.
/// </summary>
internal sealed class UrlOnlyObjectStore(string baseUrl) : IObjectStore
{
    public string GetPublicUrl(string key) => $"{baseUrl}/{key}";

    public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task PutAsync(
        string key, Stream content, string contentType, IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<ObjectHead?> HeadAsync(string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> DownloadAsync(string key, Stream destination, long maxBytes, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<string> BeginMultipartUploadAsync(string key, string contentType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<string> UploadPartAsync(
        string key, string uploadId, int partNumber, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task CompleteMultipartUploadAsync(
        string key, string uploadId, IReadOnlyList<MultipartPart> parts, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task AbortMultipartUploadAsync(string key, string uploadId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public IAsyncEnumerable<string> ListKeysAsync(string prefix, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
