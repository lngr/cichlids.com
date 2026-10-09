using System.Text.Json;
using System.Text.Json.Serialization;
using Cichlids.Infrastructure.Storage;
using Npgsql;

namespace Cichlids.Etl.Media;

/// <summary>
/// The "media-verify" command: checks the target database's media references against what is
/// actually in object storage, independently in both directions. Unlike the "media" command's
/// idempotency fast path (which trusts a media_item's own recorded byte size and checksum once
/// set), every check here asks the object store directly, so it also catches an object that
/// disappeared or changed size after the media command last recorded it.
/// </summary>
public static class MediaVerifyCommand
{
    private const int LiveCheckConcurrency = 16;
    private static readonly string[] OrphanScanPrefixes = ["originals/", "variants/", "forum_attachments/"];

    public static async Task<int> RunAsync(
        string targetConnectionString, IObjectStore objectStore, string? outPath, CancellationToken cancellationToken)
    {
        Console.WriteLine("Running media-verify...");

        var mediaItems = await LoadMediaItemsAsync(targetConnectionString, cancellationToken);
        var mediaVariants = await LoadMediaVariantsAsync(targetConnectionString, cancellationToken);

        var referencedMismatches = new List<ReferencedMismatch>();
        await CheckReferencedObjectsAsync(
            objectStore, mediaItems, r => r.Id, r => r.StorageKey, r => r.ByteSize, "media_item", referencedMismatches, cancellationToken);
        await CheckReferencedObjectsAsync(
            objectStore, mediaVariants, r => r.Id, r => r.StorageKey, r => r.ByteSize, "media_variant", referencedMismatches, cancellationToken);

        var knownKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in mediaItems)
        {
            knownKeys.Add(item.StorageKey);
        }

        foreach (var variant in mediaVariants)
        {
            knownKeys.Add(variant.StorageKey);
        }

        var orphanKeys = await FindOrphanKeysAsync(objectStore, knownKeys, cancellationToken);

        PrintSummary(referencedMismatches, orphanKeys, mediaItems.Count, mediaVariants.Count);

        if (outPath is not null)
        {
            await WriteReportAsync(outPath, referencedMismatches, orphanKeys, cancellationToken);
        }

        return referencedMismatches.Count > 0 ? 1 : 0;
    }

    // === (a) DB rows whose referenced object is missing or the wrong size ===========================

    private static async Task CheckReferencedObjectsAsync<T>(
        IObjectStore objectStore,
        IReadOnlyList<T> rows,
        Func<T, long> id,
        Func<T, string> storageKey,
        Func<T, long?> recordedByteSize,
        string table,
        List<ReferencedMismatch> mismatches,
        CancellationToken cancellationToken)
    {
        var found = new List<ReferencedMismatch>();
        var sync = new object();

        await Parallel.ForEachAsync(
            rows,
            new ParallelOptions { MaxDegreeOfParallelism = LiveCheckConcurrency, CancellationToken = cancellationToken },
            async (row, itemCancellationToken) =>
            {
                var liveSize = await objectStore.GetSizeAsync(storageKey(row), itemCancellationToken);
                var expected = recordedByteSize(row);

                ReferencedMismatch? mismatch = liveSize switch
                {
                    null => new ReferencedMismatch(table, id(row), storageKey(row), expected, null, "object missing"),
                    _ when expected is null || expected != liveSize.Value =>
                        new ReferencedMismatch(table, id(row), storageKey(row), expected, liveSize, "size mismatch"),
                    _ => null,
                };

                if (mismatch is not null)
                {
                    lock (sync)
                    {
                        found.Add(mismatch);
                    }
                }
            });

        mismatches.AddRange(found.OrderBy(m => m.Table, StringComparer.Ordinal).ThenBy(m => m.Id));
    }

    // === (b) objects with no matching DB row =========================================================

    private static async Task<List<string>> FindOrphanKeysAsync(
        IObjectStore objectStore, HashSet<string> knownKeys, CancellationToken cancellationToken)
    {
        var orphans = new List<string>();

        foreach (var prefix in OrphanScanPrefixes)
        {
            await foreach (var key in objectStore.ListKeysAsync(prefix, cancellationToken))
            {
                if (!knownKeys.Contains(key))
                {
                    orphans.Add(key);
                }
            }
        }

        orphans.Sort(StringComparer.Ordinal);
        return orphans;
    }

    // === loading ======================================================================================

    private static async Task<List<MediaItemRow>> LoadMediaItemsAsync(string targetConnectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("SELECT id, storage_key, byte_size FROM media_item", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new List<MediaItemRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MediaItemRow(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2)));
        }

        return result;
    }

    private static async Task<List<MediaVariantRow>> LoadMediaVariantsAsync(string targetConnectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand("SELECT id, storage_key, byte_size FROM media_variant", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var result = new List<MediaVariantRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MediaVariantRow(reader.GetInt64(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt64(2)));
        }

        return result;
    }

    // === reporting ====================================================================================

    private static void PrintSummary(
        IReadOnlyList<ReferencedMismatch> referencedMismatches, IReadOnlyList<string> orphanKeys, int mediaItemCount, int mediaVariantCount)
    {
        Console.WriteLine();
        Console.WriteLine("=== media-verify report ===");
        Console.WriteLine();
        Console.WriteLine($"media_item rows checked:    {mediaItemCount}");
        Console.WriteLine($"media_variant rows checked: {mediaVariantCount}");
        Console.WriteLine($"referenced but missing/mismatched objects: {referencedMismatches.Count}");
        Console.WriteLine($"orphaned objects (no DB row): {orphanKeys.Count}");

        foreach (var mismatch in referencedMismatches.Take(50))
        {
            Console.WriteLine(
                $"  [{mismatch.Table}#{mismatch.Id}] {mismatch.StorageKey}: {mismatch.Reason}"
                + $" (expected={mismatch.ExpectedByteSize?.ToString() ?? "n/a"}, actual={mismatch.ActualByteSize?.ToString() ?? "missing"})");
        }

        if (referencedMismatches.Count > 50)
        {
            Console.WriteLine($"  ... and {referencedMismatches.Count - 50} more (see --out for the full list).");
        }

        Console.WriteLine();
        Console.WriteLine(referencedMismatches.Count == 0 ? "Result: ok (no referenced object missing or mismatched)" : $"Result: {referencedMismatches.Count} mismatch(es)");
        Console.WriteLine();
    }

    private static async Task WriteReportAsync(
        string path, IReadOnlyList<ReferencedMismatch> referencedMismatches, IReadOnlyList<string> orphanKeys, CancellationToken cancellationToken)
    {
        var document = new ReportDto(
            DateTimeOffset.UtcNow,
            referencedMismatches.Count == 0,
            referencedMismatches
                .Select(m => new MismatchDto(m.Table, m.Id, m.StorageKey, m.ExpectedByteSize, m.ActualByteSize, m.Reason))
                .ToList(),
            orphanKeys);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(document, options);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, json, cancellationToken);
        Console.WriteLine($"Wrote media-verify report to {path}.");
    }

    // === types ========================================================================================

    private sealed record MediaItemRow(long Id, string StorageKey, long? ByteSize);

    private sealed record MediaVariantRow(long Id, string StorageKey, long? ByteSize);

    private sealed record ReferencedMismatch(string Table, long Id, string StorageKey, long? ExpectedByteSize, long? ActualByteSize, string Reason);

    private sealed record MismatchDto(
        [property: JsonPropertyName("table")] string Table,
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("storageKey")] string StorageKey,
        [property: JsonPropertyName("expectedByteSize")] long? ExpectedByteSize,
        [property: JsonPropertyName("actualByteSize")] long? ActualByteSize,
        [property: JsonPropertyName("reason")] string Reason);

    private sealed record ReportDto(
        [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
        [property: JsonPropertyName("passed")] bool Passed,
        [property: JsonPropertyName("referencedMismatches")] IReadOnlyList<MismatchDto> ReferencedMismatches,
        [property: JsonPropertyName("orphanKeys")] IReadOnlyList<string> OrphanKeys);
}
