using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Storage;
using MySqlConnector;
using Npgsql;

namespace Cichlids.Etl.Media;

/// <summary>
/// The "media" command: seeds object storage with every migrated picture's and forum attachment's
/// original file plus its photo variants, and records the byte size, checksum, width and height
/// of each media_item and the variant rows from what the object store holds. Not part of "all"
/// (EtlStepRegistry): it reads large binary files and runs its own worker pool outside the
/// single-transaction-per-step model of the other steps, so it is invoked on its own.
///
/// The object store is the source of truth for what needs work. Each original and each variant is
/// looked up in the store and compared with the SHA-256 of its legacy source file and the
/// pipeline version stored as object metadata (MediaObjectMetadata). Only a missing object, or one
/// whose source checksum or pipeline version differs, is uploaded; a present object is recorded
/// in the database from its metadata without being read or decoded. A run against a freshly
/// reset database therefore uploads nothing, and an object lost from the store is restored.
/// </summary>
public static class MediaSeedCommand
{
    private const int ProgressInterval = 1000;

    public static async Task<int> RunAsync(
        string legacyConnectionString,
        string targetConnectionString,
        IObjectStore objectStore,
        string legacyImagesRoot,
        int? limit,
        int workers,
        string? missingOutPath,
        CancellationToken cancellationToken)
    {
        var summary = await SeedAsync(
            legacyConnectionString, targetConnectionString, objectStore, legacyImagesRoot, limit, workers,
            missingOutPath, cancellationToken);
        PrintSummary(summary);
        return 0;
    }

    /// <summary>
    /// Seeds object storage and the media metadata and returns the counts of the run.
    /// </summary>
    public static async Task<MediaSeedSummary> SeedAsync(
        string legacyConnectionString,
        string targetConnectionString,
        IObjectStore objectStore,
        string legacyImagesRoot,
        int? limit,
        int workers,
        string? missingOutPath,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"Running media (limit={(limit is null ? "none" : limit.Value.ToString())}, workers={workers})...");

        var legacyImageByLegacyId = await LoadLegacyImagePathsAsync(legacyConnectionString, cancellationToken);
        var candidates = await LoadCandidatesAsync(targetConnectionString, limit, cancellationToken);

        Console.WriteLine($"Loaded {candidates.Count} media_item candidate(s).");

        var stats = new Statistics();
        var missingEntries = new ConcurrentBag<MissingEntry>();
        var brokenKeys = new ConcurrentBag<string>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await Parallel.ForEachAsync(
                candidates,
                new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellationToken },
                async (candidate, itemCancellationToken) =>
                {
                    await ProcessOneAsync(
                        candidate, targetConnectionString, objectStore, legacyImagesRoot, legacyImageByLegacyId,
                        stats, missingEntries, brokenKeys, itemCancellationToken);

                    var processed = stats.IncrementProcessed();
                    if (processed % ProgressInterval == 0)
                    {
                        LogProgress(processed, candidates.Count, stats, stopwatch);
                    }
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine();
            Console.WriteLine($"Interrupted after {stats.Processed} item(s); safe to re-run (idempotent).");
        }

        var summary = stats.ToSummary(stopwatch.Elapsed);

        if (missingOutPath is not null)
        {
            await WriteMissingReportAsync(missingOutPath, missingEntries, brokenKeys, cancellationToken);
        }

        return summary;
    }

    // === per-item processing ========================================================================

    private static async Task ProcessOneAsync(
        MediaCandidate candidate,
        string targetConnectionString,
        IObjectStore objectStore,
        string legacyImagesRoot,
        IReadOnlyDictionary<int, string> legacyImageByLegacyId,
        Statistics stats,
        ConcurrentBag<MissingEntry> missingEntries,
        ConcurrentBag<string> brokenKeys,
        CancellationToken cancellationToken)
    {
        var isPhoto = candidate.Kind == "photo";
        var isForumAttachment = candidate.StorageKey.StartsWith("forum_attachments/", StringComparison.Ordinal);

        SourceFile source;
        if (isForumAttachment)
        {
            var attachmentHead = await objectStore.HeadAsync(candidate.StorageKey, cancellationToken);
            if (attachmentHead is null)
            {
                stats.IncrementMissing();
                missingEntries.Add(new MissingEntry(candidate.Id, candidate.StorageKey, "object missing from store"));
                return;
            }

            source = SourceFile.FromStoredObject(objectStore, candidate.StorageKey, attachmentHead);
        }
        else
        {
            if (candidate.LegacyId is not { } legacyId || !legacyImageByLegacyId.TryGetValue(legacyId, out var legacyPath))
            {
                stats.IncrementMissing();
                missingEntries.Add(new MissingEntry(candidate.Id, candidate.StorageKey, "no legacy image path on record"));
                return;
            }

            var resolvedPath = LegacyImageLocator.Resolve(legacyImagesRoot, legacyPath);
            if (resolvedPath is null)
            {
                stats.IncrementMissing();
                missingEntries.Add(new MissingEntry(candidate.Id, candidate.StorageKey, $"source file not found: {legacyPath}"));
                return;
            }

            source = SourceFile.FromLegacyFile(resolvedPath);
        }

        StoredOriginal original;
        try
        {
            original = await EnsureOriginalAsync(candidate, isPhoto, source, objectStore, stats, cancellationToken);
        }
        catch (StoredObjectVanishedException)
        {
            stats.IncrementMissing();
            missingEntries.Add(new MissingEntry(candidate.Id, candidate.StorageKey, "object missing from store"));
            return;
        }
        catch (ObjectTooLargeException ex)
        {
            stats.IncrementMissing();
            missingEntries.Add(new MissingEntry(
                candidate.Id, candidate.StorageKey, $"object exceeds the download limit of {ex.MaxBytes} bytes"));
            return;
        }

        var variantsToRecord = new List<(string Label, int Width, string StorageKey, long ByteSize)>();

        if (isPhoto)
        {
            if (original.Photo is { Decodable: false } or null)
            {
                stats.IncrementBroken();
                brokenKeys.Add(candidate.StorageKey);
            }
            else
            {
                try
                {
                    foreach (var (label, targetWidth) in MediaVariantSpec.Resolve(original.Photo.Width!.Value))
                    {
                        var variantKey = MediaStorageKeys.BuildVariantKey(candidate.StorageKey, label);
                        var variantHead = await objectStore.HeadAsync(variantKey, cancellationToken);
                        if (MediaObjectMetadata.Decide(variantHead, original.SourceSha256, MediaObjectMetadata.VariantPipelineVersion)
                            == ObjectState.Current)
                        {
                            stats.IncrementVariantPresent();
                            variantsToRecord.Add((label, targetWidth, variantKey, variantHead!.Size));
                            continue;
                        }

                        var variantBytes = ImageProcessor.GenerateVariant(await source.ReadAllBytesAsync(cancellationToken), targetWidth);
                        var variantChecksum = Convert.ToHexStringLower(SHA256.HashData(variantBytes));
                        await objectStore.PutAsync(
                            variantKey,
                            new MemoryStream(variantBytes),
                            "image/jpeg",
                            MediaObjectMetadata.ForVariant(variantChecksum, original.SourceSha256),
                            cancellationToken);
                        stats.AddBytesTransferred(variantBytes.LongLength);
                        stats.IncrementVariantUploaded();
                        variantsToRecord.Add((label, targetWidth, variantKey, variantBytes.LongLength));
                    }
                }
                catch (ImageProcessor.BrokenImageException)
                {
                    // The header measured fine but the pixel data does not decode. The original
                    // is recorded as undecodable, so later runs do not retry it.
                    variantsToRecord.Clear();
                    original = original with { Photo = PhotoInfo.Undecodable };
                    await PutOriginalAsync(
                        objectStore, candidate.StorageKey, await source.ReadAllBytesAsync(cancellationToken),
                        original.Sha256, original.SourceSha256, original.Photo, cancellationToken);
                    stats.IncrementBroken();
                    brokenKeys.Add(candidate.StorageKey);
                }
            }
        }

        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);

        var decodable = original.Photo is { Decodable: true };
        await UpdateMediaItemMetadataAsync(
            connection, candidate.Id, original.Size, original.Sha256,
            decodable ? original.Photo!.Width : null, decodable ? original.Photo!.Height : null, cancellationToken);
        stats.IncrementMetadataRecorded();

        foreach (var (label, variantWidth, variantKey, variantByteSize) in variantsToRecord)
        {
            await UpsertMediaVariantAsync(connection, candidate.Id, label, variantWidth, variantKey, variantByteSize, cancellationToken);
        }
    }

    /// <summary>
    /// Brings the original in the object store in line with its source and returns what the store
    /// holds afterwards.
    /// Legacy originals are uploaded when they are missing or differ; a stored forum attachment is
    /// the source itself, so only its metadata is completed when it lacks the photo information.
    /// </summary>
    private static async Task<StoredOriginal> EnsureOriginalAsync(
        MediaCandidate candidate,
        bool isPhoto,
        SourceFile source,
        IObjectStore objectStore,
        Statistics stats,
        CancellationToken cancellationToken)
    {
        var sourceSha256 = await source.ComputeSha256Async(cancellationToken);
        var head = await objectStore.HeadAsync(candidate.StorageKey, cancellationToken);
        var state = MediaObjectMetadata.Decide(head, sourceSha256, MediaObjectMetadata.OriginalPipelineVersion);

        PhotoInfo? photo = null;
        string? storedSha256 = null;
        if (state == ObjectState.Current)
        {
            if (!head!.Metadata.TryGetValue(MediaObjectMetadata.Sha256Key, out storedSha256) || storedSha256.Length == 0)
            {
                state = ObjectState.Differs;
            }
            else if (isPhoto)
            {
                photo = MediaObjectMetadata.ReadPhotoInfo(head);
                if (photo is null)
                {
                    state = ObjectState.Differs;
                }
            }
        }

        if (state == ObjectState.Current)
        {
            stats.IncrementOriginalPresent();
            return new StoredOriginal(head!.Size, storedSha256!, sourceSha256, photo);
        }

        var bytes = await source.ReadAllBytesAsync(cancellationToken);
        if (isPhoto)
        {
            try
            {
                var measured = ImageProcessor.Measure(bytes);
                photo = new PhotoInfo(measured.Width, measured.Height);
            }
            catch (ImageProcessor.BrokenImageException)
            {
                photo = PhotoInfo.Undecodable;
            }
        }

        var uploadedSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await PutOriginalAsync(objectStore, candidate.StorageKey, bytes, uploadedSha256, sourceSha256, photo, cancellationToken);

        // A forum attachment is already in the store: completing its metadata rewrites the same
        // bytes and is not an upload of new content.
        if (source.IsStoredObject)
        {
            stats.IncrementOriginalPresent();
        }
        else
        {
            stats.AddBytesTransferred(bytes.LongLength);
            stats.IncrementOriginalUploaded();
        }

        return new StoredOriginal(bytes.LongLength, uploadedSha256, sourceSha256, photo);
    }

    private static Task PutOriginalAsync(
        IObjectStore objectStore, string key, byte[] bytes, string sha256, string sourceSha256, PhotoInfo? photo,
        CancellationToken cancellationToken) =>
        objectStore.PutAsync(
            key,
            new MemoryStream(bytes),
            MediaStorageKeys.ResolveContentType(key),
            MediaObjectMetadata.ForOriginal(sha256, sourceSha256, photo),
            cancellationToken);

    // === persistence =================================================================================

    private static async Task UpdateMediaItemMetadataAsync(
        NpgsqlConnection connection, long id, long byteSize, string checksum, int? width, int? height, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "UPDATE media_item SET byte_size = @byteSize, checksum_sha256 = @checksum, width = @width, height = @height WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("byteSize", byteSize);
        command.Parameters.AddWithValue("checksum", checksum);
        command.Parameters.AddWithValue("width", (object?)width ?? DBNull.Value);
        command.Parameters.AddWithValue("height", (object?)height ?? DBNull.Value);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertMediaVariantAsync(
        NpgsqlConnection connection, long mediaItemId, string label, int width, string storageKey, long byteSize, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO media_variant (media_item_id, label, width, storage_key, byte_size)
            VALUES (@mediaItemId, @label, @width, @storageKey, @byteSize)
            ON CONFLICT (media_item_id, label) DO UPDATE SET
                width = EXCLUDED.width, storage_key = EXCLUDED.storage_key, byte_size = EXCLUDED.byte_size
            """,
            connection);
        command.Parameters.AddWithValue("mediaItemId", mediaItemId);
        command.Parameters.AddWithValue("label", label);
        command.Parameters.AddWithValue("width", width);
        command.Parameters.AddWithValue("storageKey", storageKey);
        command.Parameters.AddWithValue("byteSize", byteSize);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // === loading ======================================================================================

    private static async Task<Dictionary<int, string>> LoadLegacyImagePathsAsync(string legacyConnectionString, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(legacyConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new MySqlCommand("SELECT uid, image FROM user_cichlids_pictures", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<int, string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var image = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
            if (image.Length > 0)
            {
                result[reader.GetInt32(0)] = image;
            }
        }

        return result;
    }

    private static async Task<List<MediaCandidate>> LoadCandidatesAsync(string targetConnectionString, int? limit, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = """
            SELECT id, kind, storage_key, legacy_id
            FROM media_item
            WHERE storage_key LIKE 'originals/%' OR storage_key LIKE 'forum_attachments/%'
            ORDER BY id
            """;
        if (limit is not null)
        {
            sql += " LIMIT @limit";
        }

        await using var command = new NpgsqlCommand(sql, connection);
        if (limit is not null)
        {
            command.Parameters.AddWithValue("limit", limit.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<MediaCandidate>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MediaCandidate(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3)));
        }

        return result;
    }

    // === reporting ====================================================================================

    private static void LogProgress(int processed, int total, Statistics stats, Stopwatch stopwatch)
    {
        var elapsedSeconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
        var itemsPerSecond = processed / elapsedSeconds;
        var megabytesPerSecond = stats.BytesTransferred / 1024.0 / 1024.0 / elapsedSeconds;
        Console.WriteLine(
            $"[media] processed {processed}/{total} ({itemsPerSecond:F1} items/s, {megabytesPerSecond:F2} MB/s, "
            + $"missing={stats.Missing}, broken={stats.Broken})...");
    }

    private static void PrintSummary(MediaSeedSummary summary)
    {
        var elapsedSeconds = Math.Max(summary.Elapsed.TotalSeconds, 0.001);
        Console.WriteLine();
        Console.WriteLine("=== media summary ===");
        Console.WriteLine($"  processed:           {summary.Processed}");
        Console.WriteLine($"  originals uploaded:  {summary.OriginalsUploaded}");
        Console.WriteLine($"  originals present:   {summary.OriginalsPresent}");
        Console.WriteLine($"  variants uploaded:   {summary.VariantsUploaded}");
        Console.WriteLine($"  variants present:    {summary.VariantsPresent}");
        Console.WriteLine($"  metadata recorded:   {summary.MetadataRecorded}");
        Console.WriteLine($"  missing:             {summary.Missing}");
        Console.WriteLine($"  broken:              {summary.Broken}");
        Console.WriteLine($"  bytes transferred:   {summary.BytesTransferred:N0}");
        Console.WriteLine($"  elapsed:             {summary.Elapsed:c}");
        Console.WriteLine($"  throughput:          {summary.Processed / elapsedSeconds:F1} items/s, {summary.BytesTransferred / 1024.0 / 1024.0 / elapsedSeconds:F2} MB/s");
        Console.WriteLine();
    }

    private static async Task WriteMissingReportAsync(
        string path, ConcurrentBag<MissingEntry> missingEntries, ConcurrentBag<string> brokenKeys, CancellationToken cancellationToken)
    {
        var document = new MissingReportDto(
            DateTimeOffset.UtcNow,
            missingEntries.Count,
            brokenKeys.Count,
            missingEntries
                .OrderBy(e => e.MediaItemId)
                .Select(e => new MissingEntryDto(e.MediaItemId, e.StorageKey, e.Reason))
                .ToList(),
            brokenKeys.OrderBy(k => k, StringComparer.Ordinal).ToList());

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(document, options);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, json, cancellationToken);
        Console.WriteLine($"Wrote missing/broken report to {path}.");
    }

    // === types ========================================================================================

    private sealed record MediaCandidate(long Id, string Kind, string StorageKey, int? LegacyId);

    private sealed class StoredObjectVanishedException : Exception;

    private sealed record StoredOriginal(long Size, string Sha256, string SourceSha256, PhotoInfo? Photo);

    /// <summary>
    /// The bytes an original and its variants are derived from: a legacy file on disk, or an
    /// object already in the store (forum attachments). Hashing streams the content; the bytes are
    /// loaded once, only when an upload or a decode needs them.
    /// </summary>
    private sealed class SourceFile
    {
        private readonly Func<CancellationToken, Task<byte[]>> _load;
        private readonly Func<CancellationToken, Task<string>> _hash;
        private byte[]? _bytes;

        private SourceFile(bool isStoredObject, Func<CancellationToken, Task<byte[]>> load, Func<CancellationToken, Task<string>> hash)
        {
            IsStoredObject = isStoredObject;
            _load = load;
            _hash = hash;
        }

        public bool IsStoredObject { get; }

        public static SourceFile FromLegacyFile(string path) =>
            new(
                false,
                token => File.ReadAllBytesAsync(path, token),
                async token =>
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                    return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
                });

        public static SourceFile FromStoredObject(IObjectStore store, string key, ObjectHead head)
        {
            SourceFile? self = null;
            self = new SourceFile(
                true,
                async token =>
                {
                    var downloaded = await store.GetAsync(key, token) ?? throw new StoredObjectVanishedException();
                    await using var stream = downloaded;
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, token);
                    return buffer.ToArray();
                },
                async token =>
                {
                    if (head.Metadata.TryGetValue(MediaObjectMetadata.Sha256Key, out var stored) && stored.Length > 0)
                    {
                        return stored;
                    }

                    return Convert.ToHexStringLower(SHA256.HashData(await self!.ReadAllBytesAsync(token)));
                });
            return self;
        }

        public Task<string> ComputeSha256Async(CancellationToken cancellationToken) => _hash(cancellationToken);

        public async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken) =>
            _bytes ??= await _load(cancellationToken);
    }

    private sealed record MissingEntry(long MediaItemId, string StorageKey, string Reason);

    private sealed record MissingEntryDto(
        [property: JsonPropertyName("mediaItemId")] long MediaItemId,
        [property: JsonPropertyName("storageKey")] string StorageKey,
        [property: JsonPropertyName("reason")] string Reason);

    private sealed record MissingReportDto(
        [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
        [property: JsonPropertyName("missingCount")] int MissingCount,
        [property: JsonPropertyName("brokenCount")] int BrokenCount,
        [property: JsonPropertyName("missing")] IReadOnlyList<MissingEntryDto> Missing,
        [property: JsonPropertyName("broken")] IReadOnlyList<string> Broken);

    /// <summary>
    /// Thread-safe run counters: every worker in the pool increments the same instance
    /// concurrently, so every mutation goes through Interlocked.
    /// </summary>
    private sealed class Statistics
    {
        private int _processed;
        private int _originalsUploaded;
        private int _originalsPresent;
        private int _variantsUploaded;
        private int _variantsPresent;
        private int _metadataRecorded;
        private int _missing;
        private int _broken;
        private long _bytesTransferred;

        public int Processed => _processed;
        public int Missing => _missing;
        public int Broken => _broken;
        public long BytesTransferred => _bytesTransferred;

        public int IncrementProcessed() => Interlocked.Increment(ref _processed);

        public void IncrementOriginalUploaded() => Interlocked.Increment(ref _originalsUploaded);

        public void IncrementOriginalPresent() => Interlocked.Increment(ref _originalsPresent);

        public void IncrementVariantUploaded() => Interlocked.Increment(ref _variantsUploaded);

        public void IncrementVariantPresent() => Interlocked.Increment(ref _variantsPresent);

        public void IncrementMetadataRecorded() => Interlocked.Increment(ref _metadataRecorded);

        public void IncrementMissing() => Interlocked.Increment(ref _missing);

        public void IncrementBroken() => Interlocked.Increment(ref _broken);

        public void AddBytesTransferred(long bytes) => Interlocked.Add(ref _bytesTransferred, bytes);

        public MediaSeedSummary ToSummary(TimeSpan elapsed) =>
            new(_processed, _originalsUploaded, _originalsPresent, _variantsUploaded, _variantsPresent, _metadataRecorded,
                _missing, _broken, _bytesTransferred, elapsed);
    }
}

/// <summary>
/// The counts of one media seed run: processed candidates, originals and variants uploaded or
/// already present in the object store, media rows whose metadata was recorded, candidates whose
/// source is missing, undecodable photos, the bytes uploaded and the elapsed time.
/// </summary>
public sealed record MediaSeedSummary(
    int Processed,
    int OriginalsUploaded,
    int OriginalsPresent,
    int VariantsUploaded,
    int VariantsPresent,
    int MetadataRecorded,
    int Missing,
    int Broken,
    long BytesTransferred,
    TimeSpan Elapsed);
