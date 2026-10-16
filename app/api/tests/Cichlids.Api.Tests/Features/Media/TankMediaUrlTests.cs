using Cichlids.Api.Features.Media;
using Cichlids.Domain.Entities;
using Microsoft.AspNetCore.DataProtection;

namespace Cichlids.Api.Tests.Features.Media;

public sealed class TankMediaUrlTests
{
    private const string BaseUrl = "https://media.test";

    private static readonly TankMediaUrlSigner Signer = new(DataProtectionProvider.Create("tank-media-tests"));

    private static MediaVariant Variant(long id, string label, string key) => new() { Id = id, Label = label, StorageKey = key };

    [Fact]
    public void PublicKeysResolveToPublicUrlsAndPrivateKeysToSignedPaths()
    {
        var builder = new MediaUrlBuilder(new UrlOnlyObjectStore(BaseUrl), Signer);
        var urls = builder.Build(
        [
            Variant(1, "thumb", "variants/thumb/originals/a.jpg"),
            Variant(2, "small", "private/variants/small/private/tank/7/a.jpg"),
        ], allowSigned: true);

        Assert.Equal($"{BaseUrl}/variants/thumb/originals/a.jpg", urls.Thumb);
        Assert.StartsWith("/api/tank-media/", urls.Small);
        Assert.Equal(2, Signer.TryReadPhoto(urls.Small![TankMediaUrlSigner.PhotoRoute.Length..]));
        Assert.Null(urls.Medium);
    }

    [Fact]
    public void PrivateKeysAreLeftOutWhenNoSignedUrlIsAllowed()
    {
        var builder = new MediaUrlBuilder(new UrlOnlyObjectStore(BaseUrl), Signer);
        var urls = builder.Build(
        [
            Variant(1, "thumb", "private/variants/thumb/private/tank/7/a.jpg"),
            Variant(2, "small", "variants/small/originals/a.jpg"),
        ], allowSigned: false);

        Assert.Null(urls.Thumb);
        Assert.Equal($"{BaseUrl}/variants/small/originals/a.jpg", urls.Small);
    }

    [Fact]
    public void ABuilderWithoutASignerNeverSignsAndNeverThrowsForAPrivateKey()
    {
        var urls = new MediaUrlBuilder(new UrlOnlyObjectStore(BaseUrl)).Build(
            [Variant(1, "thumb", "private/variants/thumb/private/tank/7/a.jpg")], allowSigned: true);

        Assert.Null(urls.Thumb);
    }

    [Fact]
    public void AnUndecodablePhotoPublicCopyAnswersEverySize()
    {
        var builder = new MediaUrlBuilder(new UrlOnlyObjectStore(BaseUrl), Signer);
        var urls = builder.Build([Variant(9, "copy", "private/variants/copy/private/tank/7/a.bin")], allowSigned: true);

        Assert.StartsWith("/api/tank-media/", urls.Large);
        Assert.Equal(urls.Large, urls.Thumb);
    }

    [Fact]
    public void ATokenNamesOneVariantOrOneVideoAndNothingElse()
    {
        var photo = Signer.PhotoPathFor(41);
        var video = Signer.VideoPathFor(42, "master.m3u8");

        Assert.Equal(41, Signer.TryReadPhoto(photo[TankMediaUrlSigner.PhotoRoute.Length..]));
        var videoToken = video[TankMediaUrlSigner.VideoRoute.Length..video.LastIndexOf('/')];
        Assert.Equal(42, Signer.TryReadVideo(videoToken));
        Assert.EndsWith("/master.m3u8", video);
        Assert.Null(Signer.TryReadVideo(photo[TankMediaUrlSigner.PhotoRoute.Length..]));
        Assert.Null(Signer.TryReadPhoto(videoToken));
        Assert.Null(Signer.TryReadPhoto("garbage"));
    }
}
