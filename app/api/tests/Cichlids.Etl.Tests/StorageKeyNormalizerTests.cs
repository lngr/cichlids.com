using Cichlids.Etl.Steps;

namespace Cichlids.Etl.Tests;

// Pure unit tests: no legacy MySQL or target Postgres container involved, unlike the step tests
// in this project that share EtlFixture.
public sealed class StorageKeyNormalizerTests
{
    [Fact]
    public void KeepsAnOrdinaryPathAsIs()
    {
        Assert.Equal(
            "originals/user_pics/5229/01_HPIM3766.JPG",
            StorageKeyNormalizer.ToStorageKey("user_pics/5229/01_HPIM3766.JPG"));
        Assert.Equal("01_HPIM3766.JPG", StorageKeyNormalizer.GetOriginalFilename("user_pics/5229/01_HPIM3766.JPG"));
    }

    [Fact]
    public void DecodesAPercentEncodedSpaceAndThenReplacesIt()
    {
        // %20 decodes to a literal space, which is itself not an allowed storage-key character.
        Assert.Equal(
            "originals/user_pics/anonymous/01_Frontosa_Web.jpg",
            StorageKeyNormalizer.ToStorageKey("user_pics/anonymous/01_Frontosa%20Web.jpg"));
    }

    [Fact]
    public void DecodesADoublyEncodedSequenceOneExtraLevel()
    {
        // %2523 decodes once to %23 (a literal "%" followed by the still-encoded "23"), which
        // still looks percent-encoded, so it decodes once more to "#", which is then replaced.
        Assert.Equal(
            "originals/user_pics/anonymous/01_01_Photo__.jpg",
            StorageKeyNormalizer.ToStorageKey("user_pics/anonymous/01_01_Photo_%2523.jpg"));
    }

    [Fact]
    public void ReassemblesAPercentEncodedMultiByteUtf8Character()
    {
        // %C3%BC is the two-byte UTF-8 encoding of "ü"; decoding byte-by-byte instead of as one
        // UTF-8 sequence would produce mojibake that folding could not recover "u" from.
        Assert.Equal(
            "originals/user_pics/1/grue.jpg",
            StorageKeyNormalizer.ToStorageKey("user_pics/1/gr%C3%BCe.jpg"));
    }

    [Fact]
    public void FoldsAccentedLatinLettersAndUnderscoresWhatIsLeftOver()
    {
        // o-umlaut decomposes to a plain "o" plus a combining mark that gets dropped; sharp s has
        // no ASCII decomposition at all and falls back to an underscore.
        Assert.Equal(
            "originals/user_pics/1/gro_e.jpg",
            StorageKeyNormalizer.ToStorageKey("user_pics/1/größe.jpg"));
    }

    [Fact]
    public void ReplacesDisallowedPunctuationWithUnderscores()
    {
        Assert.Equal(
            "originals/user_pics/anonymous/01_3475_7_98_fp344_nu_326__7_8__77_WSNRCG_323362949663_nu0mrj_1_.jpg",
            StorageKeyNormalizer.ToStorageKey(
                "user_pics/anonymous/01_3475%3B7%3B98%7Ffp344%3Enu%3D326%3A%3E7%3B8%3E%3B77%3EWSNRCG%3D323362949663%3Anu0mrj[1].jpg"));
    }

    [Fact]
    public void NormalizeFilenameAppliesTheSameFoldingWithoutAnOriginalsPrefix()
    {
        Assert.Equal("gro_e.jpg", StorageKeyNormalizer.NormalizeFilename("größe.jpg"));
        Assert.Equal("01_Frontosa_Web.jpg", StorageKeyNormalizer.NormalizeFilename("01_Frontosa%20Web.jpg"));
    }

    [Fact]
    public void RoutesAPathWithNoDirectoryUnderTheUnresolvedBucket()
    {
        Assert.Equal(
            "originals/user_pics/unresolved/01_3475.jpg",
            StorageKeyNormalizer.ToStorageKey("01_3475.jpg"));
        Assert.Equal("01_3475.jpg", StorageKeyNormalizer.GetOriginalFilename("01_3475.jpg"));
    }
}
