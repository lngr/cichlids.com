using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Media;
using MySqlConnector;
using NetVips;

namespace Cichlids.Etl.Tests;

/// <summary>
/// Helpers MediaSeedCommandTests and MediaVerifyCommandTests both need to build a legacy image
/// tree and its matching MySQL and Postgres rows: writing a synthetic JPEG at the exact
/// user_pics/uid/filename layout LegacyImageLocator resolves directly, and inserting the
/// user_cichlids_pictures and media_item rows MediaSeedCommand joins against.
/// </summary>
internal static class MediaTestSupport
{
    public static string CreateImagesRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-command-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// Writes a synthetic, valid JPEG of the exact given pixel width and height under
    /// root/user_pics/uid/filename and returns the legacy-style relative path
    /// (user_pics/uid/filename) a user_cichlids_pictures.image column would hold for it.
    /// </summary>
    public static string WritePhoto(string root, int uid, string filename, int width, int height)
    {
        var directory = Path.Combine(root, "user_pics", uid.ToString());
        Directory.CreateDirectory(directory);

        using var image = Image.Black(width, height) + new[] { 120, 40, 200 };
        image.WriteToFile(Path.Combine(directory, filename), new VOption { { "Q", 90 } });

        return $"user_pics/{uid}/{filename}";
    }

    /// <summary>
    /// Writes a synthetic JPEG tagged with EXIF orientation 6 (rotate 90 degrees) at the given
    /// raw pixel width and height, so a viewer that honors orientation sees width and height
    /// swapped relative to the raw pixel grid.
    /// </summary>
    public static string WriteOrientedPhoto(string root, int uid, string filename, int width, int height)
    {
        var directory = Path.Combine(root, "user_pics", uid.ToString());
        Directory.CreateDirectory(directory);

        using var image = Image.Black(width, height) + new[] { 120, 40, 200 };
        using var tagged = image.Mutate(m => m.Set(GValue.GIntType, "orientation", 6));
        tagged.WriteToFile(Path.Combine(directory, filename), new VOption { { "Q", 90 } });

        return $"user_pics/{uid}/{filename}";
    }

    /// <summary>
    /// Writes bytes that do not decode as an image at all, under the same layout a photo would
    /// use, so LegacyImageLocator still finds the file while ImageProcessor treats it as broken.
    /// </summary>
    public static string WriteCorruptFile(string root, int uid, string filename)
    {
        var directory = Path.Combine(root, "user_pics", uid.ToString());
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, filename), "not a real jpeg"u8.ToArray());

        return $"user_pics/{uid}/{filename}";
    }

    public static async Task<MySqlConnection> OpenLegacyConnectionAsync(MediaEtlFixture fixture)
    {
        var connection = new MySqlConnection(fixture.LegacyConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public static async Task InsertLegacyPictureAsync(MySqlConnection connection, int uid, string imagePath, int feUser = 1)
    {
        await using var command = new MySqlCommand(
            "INSERT INTO user_cichlids_pictures (uid, fe_user, image) VALUES (@uid, @feUser, @image)", connection);
        command.Parameters.AddWithValue("uid", uid);
        command.Parameters.AddWithValue("feUser", feUser);
        command.Parameters.AddWithValue("image", imagePath);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task<long> InsertMediaItemAsync(
        MediaEtlFixture fixture, int legacyId, string storageKey, MediaKind kind = MediaKind.Photo)
    {
        await using var db = fixture.CreateTargetContext();
        var item = new MediaItem
        {
            LegacyId = legacyId,
            Kind = kind,
            StorageKey = storageKey,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.MediaItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    public static Task<int> RunSeedAsync(
        MediaEtlFixture fixture, string imagesRoot, bool onlyMissing = true, int? limit = null, string? missingOutPath = null) =>
        MediaSeedCommand.RunAsync(
            fixture.LegacyConnectionString,
            fixture.TargetConnectionString,
            fixture.ObjectStore,
            imagesRoot,
            limit,
            workers: 2,
            onlyMissing,
            missingOutPath,
            CancellationToken.None);

    public static async Task<byte[]> DownloadAsync(MediaEtlFixture fixture, string storageKey)
    {
        await using var stream = await fixture.ObjectStore.GetAsync(storageKey);
        Assert.NotNull(stream);
        using var buffer = new MemoryStream();
        await stream!.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}
