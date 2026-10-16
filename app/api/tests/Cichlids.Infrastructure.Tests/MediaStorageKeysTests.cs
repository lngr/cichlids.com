using Cichlids.Infrastructure.Media;

namespace Cichlids.Infrastructure.Tests;

public sealed class MediaStorageKeysTests
{
    [Fact]
    public void MapsAPublicVariantKeyToItsPrivateCounterpartAndBack()
    {
        var publicKey = "variants/thumb/originals/uploads/1/a.jpg.jpg";
        var privateKey = MediaStorageKeys.ToPrivateVariantKey(publicKey);

        Assert.Equal("private/variants/thumb/originals/uploads/1/a.jpg.jpg", privateKey);
        Assert.True(MediaStorageKeys.IsPrivateVariantKey(privateKey));
        Assert.False(MediaStorageKeys.IsPublicVariantKey(privateKey));
        Assert.Equal(publicKey, MediaStorageKeys.ToPublicVariantKey(privateKey));
    }

    [Fact]
    public void TheMappingOfAPrivateTankPhotoVariantEqualsItsPrivateVariantKey()
    {
        var original = MediaStorageKeys.BuildPrivateOriginalKey("tank", 7, "a.jpg");

        Assert.Equal(
            MediaStorageKeys.BuildPrivateVariantKey(original, "small"),
            MediaStorageKeys.ToPrivateVariantKey($"variants/small/{original}.jpg"));
    }

    [Theory]
    [InlineData("originals/a.jpg")]
    [InlineData("private/care/1/a.jpg")]
    [InlineData("private/variants/thumb/x.jpg")]
    public void RefusesToMapAKeyThatIsNotAPublicVariantKeyToItsPrivateCounterpart(string key)
    {
        if (MediaStorageKeys.IsPublicVariantKey(key)) return;
        Assert.Throws<ArgumentException>(() => MediaStorageKeys.ToPrivateVariantKey(key));
    }

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

    [Fact]
    public void ExposesVariantsAndForumAttachmentsAsThePublicPrefixes()
    {
        Assert.Equal(["variants/", "forum_attachments/"], MediaStorageKeys.PublicPrefixes);
    }

    [Theory]
    [InlineData("originals/user_pics/1550/foo.jpg", "variants/copy/originals/user_pics/1550/foo.jpg")]
    [InlineData("forum_attachments/9001_attach.gif", "variants/copy/forum_attachments/9001_attach.gif")]
    public void BuildsThePublicCopyKeyUnderTheVariantsPrefix(string originalKey, string expected)
    {
        Assert.Equal(expected, MediaStorageKeys.BuildPublicCopyKey(originalKey));
    }

    [Fact]
    public void RefusesAPublicCopyKeyForAKeyThatIsNeitherAnOriginalNorAForumAttachment()
    {
        Assert.Throws<ArgumentException>(() => MediaStorageKeys.BuildPublicCopyKey("variants/thumb/x.jpg"));
    }

    [Fact]
    public void BuildsAPrivateVariantKeyOutsideEveryPublicPrefix()
    {
        var original = MediaStorageKeys.BuildPrivateOriginalKey("care", 42, "0190a1b2.jpg");
        var variant = MediaStorageKeys.BuildPrivateVariantKey(original, "thumb");

        Assert.Equal("private/care/42/0190a1b2.jpg", original);
        Assert.Equal("private/variants/thumb/private/care/42/0190a1b2.jpg.jpg", variant);
        Assert.All(new[] { original, variant }, key =>
            Assert.DoesNotContain(MediaStorageKeys.PublicPrefixes, prefix => key.StartsWith(prefix, StringComparison.Ordinal)));
    }

    [Fact]
    public void ThrowsWhenAPrivateVariantKeyIsBuiltFromAPublicOriginal()
    {
        Assert.Throws<ArgumentException>(() => MediaStorageKeys.BuildPrivateVariantKey("originals/x.jpg", "thumb"));
    }

    [Fact]
    public void BuildsThePrivateOriginalKeyOfAVideoBelowThePrivatePrefix()
    {
        Assert.Equal("private/video/7/abc.mp4", MediaStorageKeys.BuildPrivateVideoKey(7, "abc.mp4"));
    }

    [Fact]
    public void BuildsPublicAndPrivateHlsDirectoriesFromTheOriginalKey()
    {
        const string original = "private/video/7/abc.mp4";

        Assert.Equal("variants/video/7/abc/", MediaStorageKeys.BuildVideoDirectory(original, isPrivate: false));
        Assert.Equal("private/variants/video/7/abc/", MediaStorageKeys.BuildVideoDirectory(original, isPrivate: true));
        Assert.Throws<ArgumentException>(() => MediaStorageKeys.BuildVideoDirectory("originals/uploads/7/a.jpg", isPrivate: false));
    }

    [Theory]
    [InlineData("master.m3u8", "application/vnd.apple.mpegurl")]
    [InlineData("480p/index.m3u8", "application/vnd.apple.mpegurl")]
    [InlineData("480p/seg_00001.m4s", "video/mp4")]
    [InlineData("480p/init_0.mp4", "video/mp4")]
    [InlineData("poster.jpg", "image/jpeg")]
    public void ResolvesTheContentTypeOfHlsFiles(string path, string expected) =>
        Assert.Equal(expected, MediaStorageKeys.ResolveHlsContentType(path));

    [Theory]
    [InlineData("master.m3u8", true)]
    [InlineData("poster.jpg", true)]
    [InlineData("480p/index.m3u8", true)]
    [InlineData("1080p/init_2.mp4", true)]
    [InlineData("720p/init.mp4", true)]
    [InlineData("720p/seg_00012.m4s", true)]
    [InlineData("../master.m3u8", false)]
    [InlineData("480p/../../x", false)]
    [InlineData("480p/seg_1.m4s", false)]
    [InlineData("/master.m3u8", false)]
    [InlineData("master.m3u8/", false)]
    [InlineData("480p\\index.m3u8", false)]
    [InlineData("a/b/c.m3u8", false)]
    [InlineData("", false)]
    public void RecognizesTheFilesOfAnHlsDirectory(string path, bool expected) =>
        Assert.Equal(expected, MediaStorageKeys.IsHlsFile(path));
}
