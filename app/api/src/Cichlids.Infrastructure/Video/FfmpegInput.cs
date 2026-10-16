namespace Cichlids.Infrastructure.Video;

/// <summary>
/// The restrictions every ffprobe and ffmpeg run puts on an uploaded file. Media files can name
/// other resources (playlists, concat lists, external data references), so a tool reads only the
/// one local file it is given: the file is checked for an MP4 or QuickTime file type box before a
/// tool starts, the protocol whitelist admits the file protocol alone and the MP4 demuxer is
/// forced so no other container parser looks at the bytes.
/// </summary>
public static class FfmpegInput
{
    private const int HeadBytes = 4096;

    /// <summary>
    /// The input arguments for the file at the given path.
    /// </summary>
    public static IReadOnlyList<string> Arguments(string path) => ["-protocol_whitelist", "file", "-f", "mov", "-i", path];

    /// <summary>
    /// Throws VideoProbeException when the file does not start with an MP4 or QuickTime file type
    /// box. Nothing else of the file is read.
    /// </summary>
    public static void EnsureMp4Family(string path)
    {
        var head = new byte[HeadBytes];
        int read;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1))
        {
            read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }

        if (VideoContainerSniffer.Sniff(head.AsSpan(0, read)) is null)
        {
            throw new VideoProbeException(VideoProbeException.Unreadable, "The file is not an MP4 or QuickTime video.");
        }
    }
}
