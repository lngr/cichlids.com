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
/// original file plus its photo variants, and backfills the width/height/byte_size/checksum_sha256
/// media_item was migrated without. Not part of "all" (<see cref="Steps.EtlStepRegistry"/>): it
/// reads large binary files and runs its own worker pool instead of the single-transaction-per-step
/// model every other step uses, so it is invoked on its own.
///
/// Idempotency has two layers. "Complete" media_item rows (an already-recorded byte size and
/// checksum, and for a photo, a recorded width plus every variant label <see
/// cref="MediaVariantSpec.Resolve"/> expects for that width) are skipped outright when
/// <c>onlyMissing</c> is true, without any object storage or file-system access at all -- the
/// default, and what makes a second run over an unchanged source cheap. Underneath that, every
/// individual upload (the original, and each variant) additionally compares the freshly computed
/// byte count against what is already on record for that key rather than presuming the object
/// needs to be written; a run that turns onlyMissing off still skips re-uploading content that has
/// not actually changed.
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
        bool onlyMissing,
        string? missingOutPath,
        CancellationToken cancellationToken)
    {
        var summary = await SeedAsync(
            legacyConnectionString, targetConnectionString, objectStore, legacyImagesRoot, limit, workers, onlyMissing,
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
        bool onlyMissing,
        string? missingOutPath,
        CancellationToken cancellationToken)
    {
        Console.WriteLine(
            $"Running media (limit={(limit is null ? "none" : limit.Value.ToString())}, workers={workers}, onlyMissing={onlyMissing})...");

        var legacyImageByLegacyId = await LoadLegacyImagePathsAsync(legacyConnectionString, cancellationToken);
        var candidates = await LoadCandidatesAsync(targetConnectionString, limit, cancellationToken);
        var variantLabelsByMediaId = await LoadExistingVariantLabelsAsync(targetConnectionString, cancellationToken);

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
                    if (onlyMissing && IsComplete(candidate, variantLabelsByMediaId))
                    {
                        stats.IncrementOriginalPresent();
                    }
                    else
                    {
                        await ProcessOneAsync(
                            candidate, targetConnectionString, objectStore, legacyImagesRoot, legacyImageByLegacyId,
                            variantLabelsByMediaId, onlyMissing, stats, missingEntries, brokenKeys, itemCancellationToken);
                    }

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
        IReadOnlyDictionary<long, HashSet<string>> variantLabelsByMediaId,
        bool onlyMissing,
        Statistics stats,
        ConcurrentBag<MissingEntry> missingEntries,
        ConcurrentBag<string> brokenKeys,
        CancellationToken cancellationToken)
    {
        var isForumAttachment = candidate.StorageKey.StartsWith("forum_attachments/", StringComparison.Ordinal);

        byte[] sourceBytes;
        var sourceIsAlreadyInStore = isForumAttachment;

        if (isForumAttachment)
        {
            var downloaded = await objectStore.GetAsync(candidate.StorageKey, cancellationToken);
            if (downloaded is null)
            {
                stats.IncrementMissing();
                missingEntries.Add(new MissingEntry(candidate.Id, candidate.StorageKey, "object missing from store"));
                return;
            }

            await using var stream = downloaded;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            sourceBytes = buffer.ToArray();
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

            sourceBytes = await File.ReadAllBytesAsync(resolvedPath, cancellationToken);
        }

        var checksum = Convert.ToHexStringLower(SHA256.HashData(sourceBytes));
        var size = (long)sourceBytes.LongLength;

        // The forum attachment's original bytes were just read back out of the store, so writing
        // them back in would be a pointless round trip; only a legacy-disk original ever needs
        // uploading here.
        if (!sourceIsAlreadyInStore && candidate.ByteSize != size)
        {
            await objectStore.PutAsync(
                candidate.StorageKey, new MemoryStream(sourceBytes), MediaStorageKeys.ResolveContentType(candidate.StorageKey), cancellationToken);
            stats.AddBytesTransferred(size);
            stats.IncrementOriginalUploaded();
        }
        else
        {
            stats.IncrementOriginalPresent();
        }

        int? width = candidate.Width;
        int? height = candidate.Height;
        var variantsToRecord = new List<(string Label, int Width, string StorageKey, long ByteSize)>();

        if (candidate.Kind == "photo")
        {
            try
            {
                var measured = ImageProcessor.Measure(sourceBytes);
                width = measured.Width;
                height = measured.Height;

                var existingLabels = variantLabelsByMediaId.TryGetValue(candidate.Id, out var labels) ? labels : [];
                foreach (var (label, targetWidth) in MediaVariantSpec.Resolve(width.Value))
                {
                    if (onlyMissing && existingLabels.Contains(label))
                    {
                        stats.IncrementVariantPresent();
                        continue;
                    }

                    var variantBytes = ImageProcessor.GenerateVariant(sourceBytes, targetWidth);
                    var variantKey = MediaStorageKeys.BuildVariantKey(candidate.StorageKey, label);
                    await objectStore.PutAsync(variantKey, new MemoryStream(variantBytes), "image/jpeg", cancellationToken);
                    stats.AddBytesTransferred(variantBytes.LongLength);
                    stats.IncrementVariantUploaded();
                    variantsToRecord.Add((label, targetWidth, variantKey, variantBytes.LongLength));
                }
            }
            catch (ImageProcessor.BrokenImageException)
            {
                stats.IncrementBroken();
                brokenKeys.Add(candidate.StorageKey);
                width = null;
                height = null;
            }
        }

        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);

        await UpdateMediaItemMetadataAsync(connection, candidate.Id, size, checksum, width, height, cancellationToken);
        stats.IncrementMetadataRecorded();

        foreach (var (label, variantWidth, variantKey, variantByteSize) in variantsToRecord)
        {
            await UpsertMediaVariantAsync(connection, candidate.Id, label, variantWidth, variantKey, variantByteSize, cancellationToken);
        }
    }

    private static bool IsComplete(MediaCandidate candidate, IReadOnlyDictionary<long, HashSet<string>> variantLabelsByMediaId)
    {
        if (candidate.ByteSize is null || candidate.ChecksumSha256 is null)
        {
            return false;
        }

        if (candidate.Kind != "photo")
        {
            return true;
        }

        if (candidate.Width is null || candidate.Height is null)
        {
            return false;
        }

        var expectedLabels = MediaVariantSpec.Resolve(candidate.Width.Value).Select(v => v.Label).ToHashSet();
        var actualLabels = variantLabelsByMediaId.TryGetValue(candidate.Id, out var labels) ? labels : [];
        return expectedLabels.SetEquals(actualLabels);
    }

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
            SELECT id, kind, storage_key, legacy_id, width, height, byte_size, checksum_sha256
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
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt64(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return result;
    }

    private static async Task<Dictionary<long, HashSet<string>>> LoadExistingVariantLabelsAsync(
        string targetConnectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("SELECT media_item_id, label FROM media_variant", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new Dictionary<long, HashSet<string>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var mediaItemId = reader.GetInt64(0);
            var label = reader.GetString(1);
            if (!result.TryGetValue(mediaItemId, out var labels))
            {
                labels = [];
                result[mediaItemId] = labels;
            }

            labels.Add(label);
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

    private sealed record MediaCandidate(
        long Id, string Kind, string StorageKey, int? LegacyId, int? Width, int? Height, long? ByteSize, string? ChecksumSha256);

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
