using System.Net;
using System.Runtime.CompilerServices;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;

namespace Cichlids.Infrastructure.Storage;

/// <summary>
/// Object store backed by an S3-compatible service, reached only through standard S3 API calls
/// so the concrete provider stays a swappable configuration detail.
/// </summary>
public sealed class S3ObjectStore : IObjectStore
{
    private readonly IAmazonS3 _client;
    private readonly S3ObjectStoreOptions _options;

    public S3ObjectStore(IAmazonS3 client, IOptions<S3ObjectStoreOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var request = new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
        };

        await _client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Stream?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
        };

        try
        {
            using var response = await _client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
            var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            buffer.Position = 0;
            return buffer;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectMetadataRequest
        {
            BucketName = _options.Bucket,
            Key = key,
        };

        try
        {
            await _client.GetObjectMetadataAsync(request, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectMetadataRequest
        {
            BucketName = _options.Bucket,
            Key = key,
        };

        try
        {
            var response = await _client.GetObjectMetadataAsync(request, cancellationToken).ConfigureAwait(false);
            return response.ContentLength;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = _options.Bucket,
            Key = key,
        };

        await _client.DeleteObjectAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> ListKeysAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? continuationToken = null;

        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _options.Bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken,
                MaxKeys = _options.ListPageSize,
            };

            var response = await _client.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

            // S3Objects is null, not an empty list, when the provider's response has no
            // Contents element at all: real AWS S3 always includes it, but some S3-compatible
            // providers omit it entirely for a prefix with zero matching objects.
            foreach (var entry in response.S3Objects ?? [])
            {
                yield return entry.Key;
            }

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (continuationToken is not null);
    }

    public string GetPublicUrl(string key)
    {
        var encodedKey = string.Join('/', key.Split('/').Select(Uri.EscapeDataString));
        return $"{_options.PublicBaseUrl.TrimEnd('/')}/{encodedKey}";
    }

    /// <summary>
    /// Creates the configured bucket if it does not already exist. Safe to call on every
    /// startup: it is a no-op once the bucket is present.
    /// </summary>
    public async Task EnsureBucketAsync(CancellationToken cancellationToken = default)
    {
        var bucketExists = await AmazonS3Util.DoesS3BucketExistV2Async(_client, _options.Bucket).ConfigureAwait(false);
        if (bucketExists)
        {
            return;
        }

        await _client.PutBucketAsync(new PutBucketRequest { BucketName = _options.Bucket }, cancellationToken).ConfigureAwait(false);
    }
}
