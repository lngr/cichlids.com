using Amazon.Runtime;
using Amazon.S3;
using Cichlids.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace Cichlids.Infrastructure.Tests;

/// <summary>
/// GetPublicUrl is pure string composition and needs no running store, so these cases run
/// without the rustfs container.
/// </summary>
public sealed class S3ObjectStoreUrlTests
{
    [Theory]
    [InlineData("images/original.jpg", "https://cdn.example.com/media/images/original.jpg")]
    [InlineData("images/tank photo.jpg", "https://cdn.example.com/media/images/tank%20photo.jpg")]
    [InlineData("images/Größe.jpg", "https://cdn.example.com/media/images/Gr%C3%B6%C3%9Fe.jpg")]
    public void GetPublicUrl_appends_the_url_encoded_key_to_the_base_url(string key, string expectedUrl)
    {
        var store = CreateStore("https://cdn.example.com/media");

        var url = store.GetPublicUrl(key);

        Assert.Equal(expectedUrl, url);
    }

    [Fact]
    public void GetPublicUrl_trims_a_trailing_slash_on_the_base_url()
    {
        var store = CreateStore("https://cdn.example.com/media/");

        var url = store.GetPublicUrl("images/original.jpg");

        Assert.Equal("https://cdn.example.com/media/images/original.jpg", url);
    }

    private static S3ObjectStore CreateStore(string publicBaseUrl)
    {
        var options = new S3ObjectStoreOptions
        {
            ServiceUrl = "http://localhost:9000",
            Region = "us-east-1",
            Bucket = "unused",
            AccessKey = "unused",
            SecretKey = "unused",
            PublicBaseUrl = publicBaseUrl,
        };

        var client = new AmazonS3Client(
            new BasicAWSCredentials(options.AccessKey, options.SecretKey),
            new AmazonS3Config { ServiceURL = options.ServiceUrl, ForcePathStyle = true, AuthenticationRegion = options.Region });

        return new S3ObjectStore(client, Options.Create(options));
    }
}
