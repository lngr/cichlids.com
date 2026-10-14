using System.Text.Json;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Infrastructure.Tests;

public sealed class S3ObjectStorePolicyTests
{
    [Fact]
    public void Generated_media_policy_grants_only_get_on_variants_and_forum_attachments()
    {
        const string bucket = "cichlids-media-master";
        var policy = S3ObjectStore.BuildPublicReadPolicy(bucket, MediaStorageKeys.PublicPrefixes);
        using var document = JsonDocument.Parse(policy);
        var statement = Assert.Single(document.RootElement.GetProperty("Statement").EnumerateArray());

        Assert.Equal("Allow", statement.GetProperty("Effect").GetString());
        Assert.Equal("*", statement.GetProperty("Principal").GetString());
        Assert.Equal(["s3:GetObject"], statement.GetProperty("Action").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal(
            [$"arn:aws:s3:::{bucket}/variants/*", $"arn:aws:s3:::{bucket}/forum_attachments/*"],
            statement.GetProperty("Resource").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.False(statement.TryGetProperty("NotAction", out _));
        Assert.False(statement.TryGetProperty("NotResource", out _));
        Assert.False(statement.TryGetProperty("NotPrincipal", out _));
    }
}
