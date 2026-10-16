using System.Diagnostics;
using System.Globalization;

namespace Cichlids.Tests.Shared;

/// <summary>
/// Generates small test videos with ffmpeg, so no binary video lives in the repository. A video
/// is a moving test pattern with an optional sine tone, encoded as H.264 and AAC in an MP4
/// container with the index at the front.
/// </summary>
internal static class TestVideos
{
    public static async Task<byte[]> CreateAsync(
        int width = 320, int height = 180, double seconds = 2, bool audio = true, int rotationDegrees = 0, string codec = "libx264",
        long padToBytes = 0)
    {
        var directory = Directory.CreateTempSubdirectory("test-video-").FullName;
        try
        {
            var path = Path.Combine(directory, "video.mp4");
            var args = new List<string> { "-nostdin", "-hide_banner", "-loglevel", "error", "-y" };

            var duration = seconds.ToString(CultureInfo.InvariantCulture);
            args.AddRange(["-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate=25:duration={duration}"]);
            if (audio)
            {
                args.AddRange(["-f", "lavfi", "-i", $"sine=frequency=440:duration={duration}"]);
            }

            args.AddRange(["-c:v", codec, "-pix_fmt", "yuv420p"]);
            if (audio)
            {
                args.AddRange(["-c:a", "aac", "-shortest"]);
            }

            args.AddRange(["-movflags", "+faststart", path]);
            await RunAsync("ffmpeg", args);
            if (rotationDegrees != 0)
            {
                // A display matrix on the stream is how phones record a video held upright.
                var rotated = Path.Combine(directory, "rotated.mp4");
                await RunAsync("ffmpeg",
                [
                    "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-display_rotation:v:0",
                    rotationDegrees.ToString(CultureInfo.InvariantCulture), "-i", path, "-c", "copy", rotated,
                ]);
                return Pad(await File.ReadAllBytesAsync(rotated), padToBytes);
            }

            return Pad(await File.ReadAllBytesAsync(path), padToBytes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // A free box at the end is valid MP4 and lets a tiny video reach a size that needs several upload chunks.
    private static byte[] Pad(byte[] video, long toBytes)
    {
        if (toBytes <= video.Length + 8)
        {
            return video;
        }

        var padded = new byte[toBytes];
        video.CopyTo(padded, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(padded.AsSpan(video.Length), (uint)(toBytes - video.Length));
        "free"u8.CopyTo(padded.AsSpan(video.Length + 4));
        return padded;
    }

    /// <summary>
    /// Runs a tool with the given arguments and returns its standard output. Throws with the error
    /// output when the tool exits with a non-zero code.
    /// </summary>
    public static async Task<string> RunAsync(string tool, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{tool} failed: {await error}");
        }

        return await output;
    }
}
