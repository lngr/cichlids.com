using Cichlids.Etl.Media;

namespace Cichlids.Etl.Tests;

public sealed class MediaStorageKeysTests
{
    [Fact]
    public void BuildsAVariantKeyUnderAnOriginalsPrefix()
    {
        var key = MediaStorageKeys.BuildVariantKey("originals/user_pics/1550/foo.JPG", "thumb");

        Assert.Equal("variants/thumb/originals/user_pics/1550/foo.JPG.jpg", key);
    }

    [Fact]
    public void BuildsAVariantKeyUnderAForumAttachmentsPrefixAppendingTheJpgExtension()
    {
        var key = MediaStorageKeys.BuildVariantKey("forum_attachments/9001_attach.png", "small");

        Assert.Equal("variants/small/forum_attachments/9001_attach.png.jpg", key);
    }

    [Fact]
    public void BuildsAVariantKeyForAnOriginalWithNoExtensionAtAll()
    {
        var key = MediaStorageKeys.BuildVariantKey("forum_attachments/9002_attach", "medium");

        Assert.Equal("variants/medium/forum_attachments/9002_attach.jpg", key);
    }

    [Fact]
    public void ThrowsForAStorageKeyUnderAnUnrecognizedPrefix()
    {
        Assert.Throws<ArgumentException>(() => MediaStorageKeys.BuildVariantKey("videos/clip.mp4", "thumb"));
    }

    [Fact]
    public void KeepsVariantKeysDistinctForOriginalsThatDifferOnlyByExtension()
    {
        var pngKey = MediaStorageKeys.BuildVariantKey("originals/foo.png", "thumb");
        var jpgKey = MediaStorageKeys.BuildVariantKey("originals/foo.jpg", "thumb");

        Assert.NotEqual(pngKey, jpgKey);
    }

    [Fact]
    public void KeepsVariantKeysDistinctForOriginalsThatDifferOnlyByPrefix()
    {
        var originalsKey = MediaStorageKeys.BuildVariantKey("originals/x.jpg", "thumb");
        var forumAttachmentsKey = MediaStorageKeys.BuildVariantKey("forum_attachments/x.jpg", "thumb");

        Assert.NotEqual(originalsKey, forumAttachmentsKey);
    }

    [Theory]
    [InlineData("originals/user_pics/1/a.jpg", "image/jpeg")]
    [InlineData("originals/user_pics/1/a.jpeg", "image/jpeg")]
    [InlineData("originals/user_pics/1/a.png", "image/png")]
    [InlineData("originals/user_pics/1/a.gif", "image/gif")]
    [InlineData("originals/user_pics/1/a.mp4", "video/mp4")]
    [InlineData("originals/user_pics/1/a.webm", "video/webm")]
    [InlineData("originals/user_pics/1/a.xyz", "application/octet-stream")]
    [InlineData("originals/user_pics/1/no-extension", "application/octet-stream")]
    public void ResolvesTheContentTypeFromTheExtension(string storageKey, string expectedContentType)
    {
        Assert.Equal(expectedContentType, MediaStorageKeys.ResolveContentType(storageKey));
    }
}
