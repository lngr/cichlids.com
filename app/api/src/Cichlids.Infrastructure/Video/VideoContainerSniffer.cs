using System.Buffers.Binary;
using System.Text;

namespace Cichlids.Infrastructure.Video;

/// <summary>
/// The container family of an uploaded video file.
/// </summary>
public enum VideoContainer
{
    Mp4,
    QuickTime,
}

/// <summary>
/// Recognizes the MP4 and QuickTime container families from the file type box at the start of a
/// file, regardless of the declared content type or file name. Still image formats and audio-only
/// files that share the box layout are not recognized.
/// </summary>
public static class VideoContainerSniffer
{
    private static readonly HashSet<string> Mp4Brands =
    [
        "isom", "iso2", "iso3", "iso4", "iso5", "iso6", "iso7", "iso8", "iso9", "mp41", "mp42", "avc1", "dash", "M4V ", "f4v ", "MSNV",
    ];

    private static readonly HashSet<string> ExcludedBrands = ["heic", "heix", "hevc", "mif1", "msf1", "avif", "avis", "M4A ", "M4B "];

    private const string QuickTimeBrand = "qt  ";

    /// <summary>
    /// The container named by the file type box in the given leading bytes, or null when the bytes
    /// start with no such box or it names a family outside MP4 and QuickTime video.
    /// </summary>
    public static VideoContainer? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length < 16 || !head.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return null;
        }

        var major = Encoding.ASCII.GetString(head.Slice(8, 4));
        var boxSize = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(head), (uint)head.Length);
        var brands = new List<string> { major };
        for (var offset = 16; offset + 4 <= boxSize; offset += 4)
        {
            brands.Add(Encoding.ASCII.GetString(head.Slice(offset, 4)));
        }

        if (ExcludedBrands.Contains(major))
        {
            return null;
        }

        if (major == QuickTimeBrand)
        {
            return VideoContainer.QuickTime;
        }

        return brands.Any(Mp4Brands.Contains) ? VideoContainer.Mp4 : null;
    }
}
