using Cichlids.Api.Features.Media;
using Cichlids.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;

namespace Cichlids.Api.Tests.Features.Media;

public sealed class MediaUrlTests
{
    private const string BaseUrl = "https://media.test";

    private static readonly IDataProtectionProvider Provider = DataProtectionProvider.Create("media-url-tests");
    private static readonly MediaUrlSigner Signer = new(Provider);

    private static MediaVariant Variant(long id, string label, string key) => new() { Id = id, Label = label, StorageKey = key };

    [Fact]
    public void AnItemThatAnonymousViewersSeeGetsPublicUrlsAndSignedPathsForPrivateKeys()
    {
        var builder = new MediaUrlBuilder(new UrlOnlyObjectStore(BaseUrl), Signer);
        var urls = builder.Build(
        [
            Variant(1, "thumb", "variants/thumb/originals/a.jpg"),
            Variant(2, "small", "private/variants/small/private/tank/7/a.jpg"),
        ], allowSigned: true, anonymousVisible: true, issuableGeneration: 7);

        Assert.Equal($"{BaseUrl}/variants/thumb/originals/a.jpg", urls.Thumb);
        Assert.StartsWith("/api/media/photos/", urls.Small);
        Assert.Equal((2L, 7L), Signer.TryReadPhoto(urls.Small![MediaUrlSigner.PhotoRoute.Length..]));
        Assert.Null(urls.Medium);
    }

    [Fact]
    public void ARepresentationThatAllowsNoSignedPathLeavesPrivateKeysOut()
    {
        var builder = new MediaUrlBuilder(new UrlOnlyObjectStore(BaseUrl), Signer);
        var urls = builder.Build(
        [
            Variant(1, "thumb", "private/variants/thumb/private/tank/7/a.jpg"),
            Variant(2, "small", "variants/small/originals/a.jpg"),
        ], allowSigned: false, anonymousVisible: true, issuableGeneration: 7);

        Assert.Null(urls.Thumb);
        Assert.Equal($"{BaseUrl}/variants/small/originals/a.jpg", urls.Small);
    }

    [Fact]
    public void ABuilderWithoutASignerNeverSignsAndNeverThrowsForAPrivateKey()
    {
        var urls = new MediaUrlBuilder(new UrlOnlyObjectStore(BaseUrl)).Build(
            [Variant(1, "thumb", "private/variants/thumb/private/tank/7/a.jpg")], allowSigned: true, anonymousVisible: true, issuableGeneration: 7);

        Assert.Null(urls.Thumb);
    }

    [Fact]
    public void ForAnItemThatAnonymousViewersDoNotSeeAPublicKeyStillResolvesToASignedPathOrToNothing()
    {
        var builder = new MediaUrlBuilder(new UrlOnlyObjectStore(BaseUrl), Signer);
        MediaVariant[] variants = [Variant(1, "thumb", "variants/thumb/originals/a.jpg"), Variant(2, "small", "private/variants/small/x.jpg")];

        var signed = builder.Build(variants, allowSigned: true, anonymousVisible: false, issuableGeneration: 7);
        var cached = builder.Build(variants, allowSigned: false, anonymousVisible: false, issuableGeneration: 7);

        Assert.Equal((1L, 7L), Signer.TryReadPhoto(signed.Thumb![MediaUrlSigner.PhotoRoute.Length..]));
        Assert.StartsWith("/api/media/photos/", signed.Small);
        Assert.Null(cached.Thumb);
        Assert.Null(cached.Small);
    }

    [Fact]
    public void WithoutAGenerationReadWithTheVisibilityCheckNoSignedPathIsIssued()
    {
        var builder = new MediaUrlBuilder(new UrlOnlyObjectStore(BaseUrl), Signer);
        var urls = builder.Build([Variant(1, "thumb", "private/variants/thumb/x.jpg"), Variant(2, "small", "variants/small/y.jpg")],
            allowSigned: true, anonymousVisible: false, issuableGeneration: null);

        Assert.Null(urls.Thumb);
        Assert.Null(urls.Small);
    }

    [Fact]
    public void ThePublicOnlyBuilderLeavesAPrivateKeyOutInsteadOfThrowing()
    {
        var urls = new MediaUrlBuilder(new UrlOnlyObjectStore(BaseUrl)).Build(
            new Dictionary<string, string> { ["thumb"] = "private/variants/thumb/private/gallery/7/a.jpg", ["small"] = "variants/small/originals/a.jpg" });

        Assert.Null(urls.Thumb);
        Assert.Equal($"{BaseUrl}/variants/small/originals/a.jpg", urls.Small);
    }

    [Fact]
    public void AnUndecodablePhotoPublicCopyAnswersEverySize()
    {
        var builder = new MediaUrlBuilder(new UrlOnlyObjectStore(BaseUrl), Signer);
        var urls = builder.Build([Variant(9, "copy", "private/variants/copy/private/tank/7/a.bin")], allowSigned: true, anonymousVisible: false, issuableGeneration: 7);

        Assert.StartsWith("/api/media/photos/", urls.Large);
        Assert.Equal(urls.Large, urls.Thumb);
    }

    [Fact]
    public void ATokenNamesOneVariantOrOneVideoAndNothingElse()
    {
        var photo = Signer.PhotoPathFor(41, 3);
        var video = Signer.VideoPathFor(42, 5, "master.m3u8");

        Assert.Equal((41L, 3L), Signer.TryReadPhoto(photo[MediaUrlSigner.PhotoRoute.Length..]));
        var videoToken = video[MediaUrlSigner.VideoRoute.Length..video.LastIndexOf('/')];
        Assert.Equal((42L, 5L), Signer.TryReadVideo(videoToken));
        Assert.EndsWith("/master.m3u8", video);
        Assert.Null(Signer.TryReadVideo(photo[MediaUrlSigner.PhotoRoute.Length..]));
        Assert.Null(Signer.TryReadPhoto(videoToken));
        Assert.Null(Signer.TryReadPhoto("garbage"));
    }

    [Fact]
    public void AnExpiredTokenAndATokenOfAnotherApplicationAreNotValid()
    {
        var expired = Provider.CreateProtector(MediaUrlSigner.PhotoPurpose).ToTimeLimitedDataProtector()
            .Protect("41:0", TimeSpan.FromSeconds(-1));
        var foreign = DataProtectionProvider.Create("another-application").CreateProtector(MediaUrlSigner.PhotoPurpose)
            .ToTimeLimitedDataProtector().Protect("41:0", TimeSpan.FromMinutes(5));
        var withoutGeneration = Provider.CreateProtector(MediaUrlSigner.PhotoPurpose).ToTimeLimitedDataProtector()
            .Protect("41", TimeSpan.FromMinutes(5));

        Assert.Null(Signer.TryReadPhoto(expired));
        Assert.Null(Signer.TryReadPhoto(foreign));
        Assert.Null(Signer.TryReadPhoto(withoutGeneration));
    }

    [Fact]
    public void ThePhotoPathLivesThirtyMinutesAndTheVideoPathTwoHours()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), MediaUrlSigner.PhotoLifetime);
        Assert.Equal(TimeSpan.FromHours(2), MediaUrlSigner.VideoLifetime);
    }
}
