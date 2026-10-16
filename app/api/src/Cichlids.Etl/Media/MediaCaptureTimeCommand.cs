using System.Collections.Concurrent;
using System.Diagnostics;
using Cichlids.Infrastructure.Media;
using Npgsql;

namespace Cichlids.Etl.Media;

/// <summary>
/// The "media-dates" command: reads the capture time (EXIF DateTimeOriginal with
/// OffsetTimeOriginal) of every migrated archive photo from its legacy original and stores it on
/// the media_item row. The legacy files are only opened for reading, and only the leading
/// CaptureTimeReader.HeaderBytes bytes are read unless the header does not parse within them.
///
/// A row is a candidate while it is an archive photo (legacy id set, storage key under
/// originals/) with neither a capture time nor a check timestamp. Every row whose original was
/// read gets the check timestamp, whether or not a plausible time was found, so a rerun skips it.
/// A row whose original is missing or cannot be opened stays a candidate. The command runs on its
/// own against an existing database and writes nothing else of the media_item row.
/// </summary>
public static class MediaCaptureTimeCommand
{
    private const int ChunkSize = 500;

    /// <summary>
    /// Backfills the capture time of the candidate rows, at most limit of them when a limit is
    /// given, with the given number of parallel readers, and prints the counts.
    /// </summary>
    public static async Task<int> RunAsync(
        string legacyConnectionString,
        string targetConnectionString,
        string legacyImagesRoot,
        int? limit,
        int workers,
        long maxSourceBytes,
        CancellationToken cancellationToken)
    {
        var summary = await BackfillAsync(
            legacyConnectionString, targetConnectionString, legacyImagesRoot, limit, workers, maxSourceBytes, cancellationToken);
        Print(summary);
        return 0;
    }

    /// <summary>
    /// Backfills the capture time of the candidate rows and returns the counts of the run.
    /// </summary>
    public static async Task<MediaCaptureTimeSummary> BackfillAsync(
        string legacyConnectionString,
        string targetConnectionString,
        string legacyImagesRoot,
        int? limit,
        int workers,
        long maxSourceBytes,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var legacyPaths = await MediaSeedCommand.LoadLegacyImagePathsAsync(legacyConnectionString, cancellationToken);
        var candidates = await LoadCandidatesAsync(targetConnectionString, limit, cancellationToken);
        Console.WriteLine($"Running media-dates: {candidates.Count} candidate(s).");

        var counts = new Counts();
        foreach (var chunk in candidates.Chunk(ChunkSize))
        {
            var results = new ConcurrentBag<Examination>();
            await Parallel.ForEachAsync(
                chunk,
                new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellationToken },
                async (candidate, itemCancellationToken) =>
                    results.Add(await ExamineAsync(candidate, legacyPaths, legacyImagesRoot, maxSourceBytes, itemCancellationToken)));

            await StoreAsync(targetConnectionString, results, cancellationToken);
            foreach (var result in results)
            {
                counts.Add(result);
            }

            Console.WriteLine($"  {counts.Examined + counts.Missing} of {candidates.Count} candidate(s) handled.");
        }

        return new MediaCaptureTimeSummary(
            candidates.Count, counts.Examined, counts.Found, counts.NoDate, counts.Implausible, counts.Unreadable,
            counts.Missing, counts.BytesRead, stopwatch.Elapsed);
    }

    private static async Task<Examination> ExamineAsync(
        Candidate candidate,
        IReadOnlyDictionary<int, string> legacyPaths,
        string legacyImagesRoot,
        long maxSourceBytes,
        CancellationToken cancellationToken)
    {
        if (!legacyPaths.TryGetValue(candidate.LegacyId, out var legacyPath)
            || LegacyImageLocator.Resolve(legacyImagesRoot, legacyPath) is not { } path)
        {
            return new Examination(candidate.Id, null, 0, Mark: false);
        }

        try
        {
            var length = new FileInfo(path).Length;
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);

            var bytesRead = 0L;
            var buffer = new byte[(int)Math.Min(length, CaptureTimeReader.HeaderBytes)];
            var filled = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
            bytesRead += filled;
            var reading = CaptureTimeReader.Read(buffer.AsSpan(0, filled), DateTimeOffset.UtcNow);

            // A header that does not parse within the leading bytes is read again from the whole
            // file, so a photo with a long run of leading metadata is not reported unreadable.
            if (reading.Outcome == CaptureTimeOutcome.Unreadable && length > filled && length <= maxSourceBytes)
            {
                var whole = new byte[length];
                buffer.AsSpan(0, filled).CopyTo(whole);
                var rest = await stream.ReadAtLeastAsync(whole.AsMemory(filled), whole.Length - filled, throwOnEndOfStream: false, cancellationToken);
                bytesRead += rest;
                reading = CaptureTimeReader.Read(whole.AsSpan(0, filled + rest), DateTimeOffset.UtcNow);
            }

            return new Examination(candidate.Id, reading, bytesRead, Mark: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Examination(candidate.Id, new CaptureTimeReading(CaptureTimeOutcome.Unreadable, null), 0, Mark: false);
        }
    }

    private static async Task StoreAsync(
        string targetConnectionString, IEnumerable<Examination> results, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The row filter keeps a value written by the API or an earlier run.
        await using var command = new NpgsqlCommand(
            """
            UPDATE media_item
            SET taken_at_local = @local, taken_at_offset_minutes = @offset, capture_checked_at = @checkedAt
            WHERE id = @id AND taken_at_local IS NULL AND capture_checked_at IS NULL
            """,
            connection,
            transaction);
        var id = command.Parameters.Add(new NpgsqlParameter("id", NpgsqlTypes.NpgsqlDbType.Bigint));
        var local = command.Parameters.Add(new NpgsqlParameter("local", NpgsqlTypes.NpgsqlDbType.Timestamp));
        var offset = command.Parameters.Add(new NpgsqlParameter("offset", NpgsqlTypes.NpgsqlDbType.Smallint));
        var checkedAt = command.Parameters.Add(new NpgsqlParameter("checkedAt", NpgsqlTypes.NpgsqlDbType.TimestampTz));
        var now = DateTimeOffset.UtcNow;

        foreach (var result in results.Where(r => r.Mark))
        {
            var time = result.Reading?.Time;
            id.Value = result.Id;
            local.Value = time is null ? DBNull.Value : time.LocalTime;
            offset.Value = time?.Offset is { } span ? (short)span.TotalMinutes : DBNull.Value;
            checkedAt.Value = now;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<List<Candidate>> LoadCandidatesAsync(
        string targetConnectionString, int? limit, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(targetConnectionString);
        await connection.OpenAsync(cancellationToken);

        var sql = """
            SELECT id, legacy_id
            FROM media_item
            WHERE kind = 'photo' AND legacy_id IS NOT NULL AND storage_key LIKE 'originals/%'
                AND taken_at_local IS NULL AND capture_checked_at IS NULL
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
        var result = new List<Candidate>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new Candidate(reader.GetInt64(0), reader.GetInt32(1)));
        }

        return result;
    }

    private static void Print(MediaCaptureTimeSummary summary)
    {
        Console.WriteLine();
        Console.WriteLine("media-dates summary");
        Console.WriteLine($"  candidates:   {summary.Candidates}");
        Console.WriteLine($"  examined:     {summary.Examined}");
        Console.WriteLine($"  found:        {summary.Found}");
        Console.WriteLine($"  no date:      {summary.NoDate}");
        Console.WriteLine($"  implausible:  {summary.Implausible}");
        Console.WriteLine($"  unreadable:   {summary.Unreadable}");
        Console.WriteLine($"  source missing: {summary.Missing}");
        Console.WriteLine($"  bytes read:   {summary.BytesRead}");
        Console.WriteLine($"  elapsed:      {summary.Elapsed}");
    }

    private sealed record Candidate(long Id, int LegacyId);

    // A null Reading means the legacy original was not found. Mark says whether the row gets the
    // check timestamp.
    private sealed record Examination(long Id, CaptureTimeReading? Reading, long BytesRead, bool Mark);

    private sealed class Counts
    {
        public int Examined { get; private set; }
        public int Found { get; private set; }
        public int NoDate { get; private set; }
        public int Implausible { get; private set; }
        public int Unreadable { get; private set; }
        public int Missing { get; private set; }
        public long BytesRead { get; private set; }

        public void Add(Examination result)
        {
            BytesRead += result.BytesRead;
            if (result.Reading is null)
            {
                Missing++;
                return;
            }

            Examined++;
            switch (result.Reading.Outcome)
            {
                case CaptureTimeOutcome.Found: Found++; break;
                case CaptureTimeOutcome.NoDate: NoDate++; break;
                case CaptureTimeOutcome.Implausible: Implausible++; break;
                default: Unreadable++; break;
            }
        }
    }
}

/// <summary>
/// The counts of one media-dates run: the candidate rows, the rows whose original was read
/// (Examined, split into Found, NoDate, Implausible and Unreadable), the rows whose original could
/// not be found (Missing), and the bytes read from the originals.
/// </summary>
public sealed record MediaCaptureTimeSummary(
    int Candidates, int Examined, int Found, int NoDate, int Implausible, int Unreadable, int Missing, long BytesRead, TimeSpan Elapsed);
