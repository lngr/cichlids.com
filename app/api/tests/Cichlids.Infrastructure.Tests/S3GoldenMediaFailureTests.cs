using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Cichlids.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Cichlids.Infrastructure.Tests;

public class S3GoldenMediaFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Provider_errors_propagate(HttpStatusCode status)
    {
        using var client = new FailingClient(status);
        var store = new S3ObjectStore(client, Options.Create(new S3ObjectStoreOptions
        {
            WritePrefix = "environments/test/",
        }));
        await Assert.ThrowsAsync<AmazonS3Exception>(() => store.GetAsync("variants/a.jpg"));
        await Assert.ThrowsAsync<AmazonS3Exception>(() => store.HeadAsync("variants/a.jpg"));
        await Assert.ThrowsAsync<AmazonS3Exception>(() => store.ExistsAsync("variants/a.jpg"));
        await Assert.ThrowsAsync<AmazonS3Exception>(() => store.GetSizeAsync("variants/a.jpg"));
        Assert.EndsWith("/environments/test/variants/a.jpg", store.GetPublicUrl("variants/a.jpg"));
        Assert.Equal(4, client.Keys.Count);
        Assert.All(client.Keys, key => Assert.Equal("environments/test/variants/a.jpg", key));
    }

    [Theory]
    [InlineData("golden/v1/media/")]
    [InlineData("golden/")]
    [InlineData("golden/v2/media/")]
    public void Golden_write_prefix_is_rejected(string writePrefix)
    {
        using var client = new FailingClient(HttpStatusCode.Forbidden);
        Assert.Throws<InvalidOperationException>(() => new S3ObjectStore(client, Options.Create(new S3ObjectStoreOptions
        {
            WritePrefix = writePrefix,
        })));
        Assert.Empty(client.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("environments/staging/")]
    public async Task Given_golden_keys_when_writing_or_deleting_then_no_provider_call_is_allowed(string prefix)
    {
        using var client = new FailingClient(HttpStatusCode.Forbidden);
        var store = new S3ObjectStore(client, Options.Create(new S3ObjectStoreOptions { WritePrefix = prefix }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PutAsync("golden/v1/media/a", new MemoryStream([1]), "image/jpeg"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PutAsync("golden/v1/media/a", new MemoryStream([1]), "image/jpeg", new Dictionary<string, string> { ["source"] = "synthetic" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync("golden/v1/media/a"));
        Assert.Empty(client.Keys);
    }

    private sealed class FailingClient(HttpStatusCode status) : AmazonS3Client(
        new BasicAWSCredentials("unused", "unused"), new AmazonS3Config { ServiceURL = "http://unused" })
    {
        public List<string> Keys { get; } = [];

        public override Task<PutObjectResponse> PutObjectAsync(PutObjectRequest request, CancellationToken cancellationToken = default)
        {
            Keys.Add(request.Key);
            throw new InvalidOperationException("Unexpected write.");
        }

        public override Task<DeleteObjectResponse> DeleteObjectAsync(DeleteObjectRequest request, CancellationToken cancellationToken = default)
        {
            Keys.Add(request.Key);
            throw new InvalidOperationException("Unexpected delete.");
        }

        public override Task<GetObjectMetadataResponse> GetObjectMetadataAsync(GetObjectMetadataRequest request, CancellationToken cancellationToken = default)
        {
            Keys.Add(request.Key);
            return Task.FromException<GetObjectMetadataResponse>(new AmazonS3Exception("synthetic failure") { StatusCode = status });
        }

        public override Task<GetObjectResponse> GetObjectAsync(GetObjectRequest request, CancellationToken cancellationToken = default)
        {
            Keys.Add(request.Key);
            return Task.FromException<GetObjectResponse>(new AmazonS3Exception("synthetic failure") { StatusCode = status });
        }
    }
}
