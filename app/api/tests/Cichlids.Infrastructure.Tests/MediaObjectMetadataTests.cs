using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Storage;

namespace Cichlids.Infrastructure.Tests;

public sealed class MediaObjectMetadataTests
{
    private const string Source = "aaaa";

    [Fact]
    public void ForOriginal_records_checksums_pipeline_and_the_measured_size_of_a_decodable_photo()
    {
        var metadata = MediaObjectMetadata.ForOriginal("bbbb", Source, new PhotoInfo(640, 480));

        Assert.Equal("bbbb", metadata[MediaObjectMetadata.Sha256Key]);
        Assert.Equal(Source, metadata[MediaObjectMetadata.SourceSha256Key]);
        Assert.Equal(MediaObjectMetadata.OriginalPipelineVersion.ToString(), metadata[MediaObjectMetadata.PipelineKey]);
        Assert.Equal("640", metadata[MediaObjectMetadata.WidthKey]);
        Assert.Equal("480", metadata[MediaObjectMetadata.HeightKey]);
        Assert.Equal("true", metadata[MediaObjectMetadata.DecodableKey]);
    }

    [Fact]
    public void ForOriginal_marks_an_undecodable_photo_without_dimensions()
    {
        var metadata = MediaObjectMetadata.ForOriginal("bbbb", Source, PhotoInfo.Undecodable);

        Assert.Equal("false", metadata[MediaObjectMetadata.DecodableKey]);
        Assert.False(metadata.ContainsKey(MediaObjectMetadata.WidthKey));
        Assert.False(metadata.ContainsKey(MediaObjectMetadata.HeightKey));
    }

    [Fact]
    public void ForOriginal_without_photo_information_has_no_photo_keys()
    {
        var metadata = MediaObjectMetadata.ForOriginal("bbbb", Source, photo: null);

        Assert.Equal(3, metadata.Count);
        Assert.False(metadata.ContainsKey(MediaObjectMetadata.DecodableKey));
    }

    [Fact]
    public void ForVariant_records_checksums_and_the_variant_pipeline_version()
    {
        var metadata = MediaObjectMetadata.ForVariant("cccc", Source);

        Assert.Equal("cccc", metadata[MediaObjectMetadata.Sha256Key]);
        Assert.Equal(Source, metadata[MediaObjectMetadata.SourceSha256Key]);
        Assert.Equal(MediaObjectMetadata.VariantPipelineVersion.ToString(), metadata[MediaObjectMetadata.PipelineKey]);
        Assert.Equal(3, metadata.Count);
    }

    [Fact]
    public void ReadPhotoInfo_round_trips_through_the_metadata()
    {
        var head = new ObjectHead(10, MediaObjectMetadata.ForOriginal("b", Source, new PhotoInfo(640, 480)));
        var broken = new ObjectHead(10, MediaObjectMetadata.ForOriginal("b", Source, PhotoInfo.Undecodable));

        Assert.Equal(new PhotoInfo(640, 480), MediaObjectMetadata.ReadPhotoInfo(head));
        Assert.Equal(PhotoInfo.Undecodable, MediaObjectMetadata.ReadPhotoInfo(broken));
        Assert.Null(MediaObjectMetadata.ReadPhotoInfo(new ObjectHead(10, new Dictionary<string, string>())));
    }

    [Fact]
    public void Decide_is_Missing_for_a_null_head()
    {
        Assert.Equal(
            ObjectState.Missing,
            MediaObjectMetadata.Decide(null, Source, MediaObjectMetadata.OriginalPipelineVersion));
    }

    [Fact]
    public void Decide_is_Current_when_source_checksum_and_pipeline_match()
    {
        var head = new ObjectHead(10, MediaObjectMetadata.ForOriginal("b", Source, null));

        Assert.Equal(
            ObjectState.Current,
            MediaObjectMetadata.Decide(head, Source, MediaObjectMetadata.OriginalPipelineVersion));
    }

    [Fact]
    public void Decide_differs_on_a_source_checksum_mismatch()
    {
        var head = new ObjectHead(10, MediaObjectMetadata.ForOriginal("b", Source, null));

        Assert.Equal(
            ObjectState.Differs,
            MediaObjectMetadata.Decide(head, "other", MediaObjectMetadata.OriginalPipelineVersion));
    }

    [Fact]
    public void Decide_differs_on_a_pipeline_mismatch()
    {
        var head = new ObjectHead(10, MediaObjectMetadata.ForOriginal("b", Source, null));

        Assert.Equal(
            ObjectState.Differs,
            MediaObjectMetadata.Decide(head, Source, MediaObjectMetadata.OriginalPipelineVersion + 1));
    }

    [Fact]
    public void Decide_differs_for_an_object_without_metadata()
    {
        var head = new ObjectHead(10, new Dictionary<string, string>());

        Assert.Equal(
            ObjectState.Differs,
            MediaObjectMetadata.Decide(head, Source, MediaObjectMetadata.OriginalPipelineVersion));
    }
}
