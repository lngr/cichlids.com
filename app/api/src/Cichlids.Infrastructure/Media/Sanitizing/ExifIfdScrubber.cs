using System.Buffers.Binary;

namespace Cichlids.Infrastructure.Media.Sanitizing;

/// <summary>
/// Removes personal data from a TIFF-structured EXIF block in place. IFD0 and the Exif IFD keep
/// only allowlisted tags (camera make and model, orientation, resolution, software, dates, artist,
/// copyright, colour profile, exposure and lens model data, colour space and pixel size); the
/// Interop IFD keeps its interoperability tags with their specified types. Only the first pointer
/// to the Exif IFD and the first pointer to the Interop IFD are kept. Every other entry is removed
/// by compacting its directory, and the bytes of its value are zeroed, together with the GPS IFD,
/// IFD1 with its thumbnail and every other directory only a removed entry points to. Bytes that
/// belong to a kept directory or a kept value are never zeroed, even where a writer let a removed
/// value overlap them. The block keeps its length, so no offset changes. The work is bounded by the
/// block length: blocks above MaxBlockLength are dropped, every directory read and every pointer
/// followed is charged against one budget of twice the block length (a block that exhausts it is
/// dropped), each directory is queued once, and removed directories are walked with a work list.
/// </summary>
internal static class ExifIfdScrubber
{
    private const ushort ExifPointerTag = 0x8769;
    private const ushort GpsPointerTag = 0x8825;
    private const ushort InteropPointerTag = 0xA005;
    private const ushort SubIfdsTag = 0x014A;
    private const ushort ThumbnailOffsetTag = 0x0201;
    private const ushort ThumbnailLengthTag = 0x0202;

    /// <summary>
    /// Largest EXIF block in bytes the scrubber accepts, 64 KiB: the most a JPEG APP1 segment can
    /// hold. Larger blocks (possible in PNG and WebP) are dropped whole.
    /// </summary>
    public const int MaxBlockLength = 64 * 1024;

    private static readonly HashSet<ushort> InteropTags =
    [
        0x0001, // InteroperabilityIndex
        0x0002, // InteroperabilityVersion
        0x1000, // RelatedImageFileFormat
        0x1001, // RelatedImageWidth
        0x1002, // RelatedImageLength
    ];

    private static readonly HashSet<ushort> Ifd0Tags =
    [
        0x010F, // Make
        0x0110, // Model
        0x0112, // Orientation
        0x011A, // XResolution
        0x011B, // YResolution
        0x0128, // ResolutionUnit
        0x0131, // Software
        0x0132, // DateTime
        0x013B, // Artist
        0x0213, // YCbCrPositioning
        0x8298, // Copyright
        0x8773, // InterColorProfile
        ExifPointerTag,
    ];

    private static readonly HashSet<ushort> ExifTags =
    [
        0x829A, // ExposureTime
        0x829D, // FNumber
        0x8822, // ExposureProgram
        0x8827, // PhotographicSensitivity
        0x8830, // SensitivityType
        0x8831, // StandardOutputSensitivity
        0x8832, // RecommendedExposureIndex
        0x8833, // ISOSpeed
        0x9000, // ExifVersion
        0x9003, // DateTimeOriginal
        0x9004, // DateTimeDigitized
        0x9010, // OffsetTime
        0x9011, // OffsetTimeOriginal
        0x9012, // OffsetTimeDigitized
        0x9101, // ComponentsConfiguration
        0x9201, // ShutterSpeedValue
        0x9202, // ApertureValue
        0x9203, // BrightnessValue
        0x9204, // ExposureBiasValue
        0x9205, // MaxApertureValue
        0x9206, // SubjectDistance
        0x9207, // MeteringMode
        0x9208, // LightSource
        0x9209, // Flash
        0x920A, // FocalLength
        0x9290, // SubSecTime
        0x9291, // SubSecTimeOriginal
        0x9292, // SubSecTimeDigitized
        0xA000, // FlashpixVersion
        0xA001, // ColorSpace
        0xA002, // PixelXDimension
        0xA003, // PixelYDimension
        0xA402, // ExposureMode
        0xA403, // WhiteBalance
        0xA404, // DigitalZoomRatio
        0xA405, // FocalLengthIn35mmFilm
        0xA406, // SceneCaptureType
        0xA407, // GainControl
        0xA408, // Contrast
        0xA409, // Saturation
        0xA40A, // Sharpness
        0xA40C, // SubjectDistanceRange
        0xA432, // LensSpecification
        0xA433, // LensMake
        0xA434, // LensModel
        InteropPointerTag,
    ];

    /// <summary>
    /// Returns a scrubbed copy of the block, or null when the block is dropped: it is larger than
    /// MaxBlockLength, has an unknown byte order, a kept directory outside the block or reached
    /// twice, or more structure to walk than its work budget allows.
    /// </summary>
    public static byte[]? Scrub(ReadOnlySpan<byte> block)
    {
        if (block.Length > MaxBlockLength)
        {
            return null;
        }

        var data = block.ToArray();
        try
        {
            new Scrubber(data).Run();
            return data;
        }
        catch (MalformedExifException)
        {
            return null;
        }
    }

    private sealed class MalformedExifException : Exception;

    private readonly record struct Entry(ushort Tag, ushort Type, uint Count, uint ValueOrOffset, byte[] Raw);

    private sealed record KeptIfd(int Offset, int EntryCount, List<Entry> Kept);

    private sealed class Scrubber(byte[] data)
    {
        private const int HeaderLength = 8;
        private const int MaxRelatedImageFileFormatCount = 64;

        // One budget for the whole block, in bytes of structure read: a directory costs its
        // table, a queued pointer its four bytes. A well-formed block reads each byte of its
        // structure once; twice the block length leaves room for writers that let tables overlap.
        private readonly long _budget = (2L * data.Length) + 256;

        private readonly List<KeptIfd> _keptIfds = [];
        private readonly HashSet<long> _keptOffsets = [];
        private readonly List<Entry> _removedEntries = [];
        private readonly HashSet<ushort> _keptPointerTags = [];
        private (Entry Pointer, List<Entry> Directory)? _interopPointer;
        private readonly Stack<uint> _removedDirectories = [];
        private readonly HashSet<uint> _queuedDirectories = [];
        private readonly HashSet<long> _zeroedOffsets = [];
        private readonly List<(long Start, long End)> _zeroRanges = [];
        private readonly List<(long Start, long End)> _protectedRanges = [];
        private long _spent;
        private bool _bigEndian;

        public void Run()
        {
            if (data.Length < HeaderLength)
            {
                throw new MalformedExifException();
            }

            _bigEndian = (data[0], data[1]) switch
            {
                ((byte)'M', (byte)'M') => true,
                ((byte)'I', (byte)'I') => false,
                _ => throw new MalformedExifException(),
            };

            if (ReadU16(2) != 42)
            {
                throw new MalformedExifException();
            }

            Protect(0, HeaderLength);
            CollectKept(ReadU32(4), Ifd0Tags, isIfd0: true);
            CollectInterop();

            foreach (var entry in _removedEntries)
            {
                PlanEntryRemoval(entry);
            }

            while (_removedDirectories.TryPop(out var offset))
            {
                PlanIfdRemoval(offset);
            }

            ApplyZeroRanges();

            foreach (var ifd in _keptIfds)
            {
                Array.Clear(data, ifd.Offset, 2 + (ifd.EntryCount * 12) + 4);
                WriteU16(ifd.Offset, (ushort)ifd.Kept.Count);
                for (var i = 0; i < ifd.Kept.Count; i++)
                {
                    ifd.Kept[i].Raw.CopyTo(data, ifd.Offset + 2 + (i * 12));
                }
            }
        }

        // Walks the directories that stay (IFD0, the Exif IFD and the Interop IFD; at most three
        // levels deep), protects their tables and kept values, and records what the removal has
        // to zero.
        private void CollectKept(uint offset, HashSet<ushort> allowed, bool isIfd0 = false)
        {
            if (!_keptOffsets.Add(offset))
            {
                throw new MalformedExifException();
            }

            var (entries, next) = ReadIfd(offset) ?? throw new MalformedExifException();
            Protect(offset, 2 + (entries.Count * 12) + 4L);

            var kept = new List<Entry>(entries.Count);
            foreach (var entry in entries)
            {
                var allowedEntry = allowed == InteropTags ? IsInteropEntry(entry) : allowed.Contains(entry.Tag);

                // Readers follow every Exif or Interop pointer they find. Only the first pointer of
                // each kind is kept, so the one directory walked for it is the only one they reach.
                if (!allowedEntry || ((entry.Tag is ExifPointerTag or InteropPointerTag) && !_keptPointerTags.Add(entry.Tag)))
                {
                    _removedEntries.Add(entry);
                    continue;
                }

                kept.Add(entry);
                if (ValueLength(entry) is > 4 and var length)
                {
                    Protect(entry.ValueOrOffset, length);
                }

                if (isIfd0 && entry.Tag == ExifPointerTag)
                {
                    CollectKept(InlineValue(entry), ExifTags);
                }
                else if (allowed == ExifTags && entry.Tag == InteropPointerTag)
                {
                    _interopPointer = (entry, kept);
                }
            }

            if (next != 0)
            {
                QueueRemoval(next);
            }

            _keptIfds.Add(new KeptIfd((int)offset, entries.Count, kept));
        }

        // The Interop directory is collected once every removed pointer of IFD0 and the Exif IFD is
        // known. A writer can let the Interop pointer lead to the GPS directory, whose
        // GPSLatitudeRef and GPSLatitude share their tag numbers with the interoperability tags;
        // an Interop pointer to a removed or already kept directory is removed itself.
        private void CollectInterop()
        {
            if (_interopPointer is not var (pointer, directory))
            {
                return;
            }

            var target = InlineValue(pointer);
            var removedTargets = _removedEntries
                .Where(e => e.Tag is ExifPointerTag or GpsPointerTag or InteropPointerTag or SubIfdsTag)
                .Select(InlineValue);
            if (_keptOffsets.Contains(target) || _queuedDirectories.Contains(target) || removedTargets.Contains(target))
            {
                directory.Remove(pointer);
                _removedEntries.Add(pointer);
                return;
            }

            CollectKept(target, InteropTags);
        }

        // An interoperability tag is kept only with the type and count the Exif specification
        // gives it, so a misplaced entry of another directory never passes as one.
        // RelatedImageFileFormat, a short file format name, is kept up to 64 bytes.
        private static bool IsInteropEntry(Entry entry) => entry.Tag switch
        {
            0x0001 => entry.Type == 2 && entry.Count == 4,
            0x0002 => entry.Type == 7 && entry.Count == 4,
            0x1000 => entry.Type == 2 && entry.Count <= MaxRelatedImageFileFormatCount,
            0x1001 or 0x1002 => entry.Type is 3 or 4 && entry.Count == 1,
            _ => false,
        };

        // An entry of unknown type has no value a reader can locate or interpret; removing it from
        // its directory is all its removal takes.
        private void PlanEntryRemoval(Entry entry)
        {
            var length = ValueLength(entry) ?? 0;
            if (length > 4)
            {
                PlanZero(entry.ValueOrOffset, length);
            }

            if (entry.Tag is ExifPointerTag or GpsPointerTag or InteropPointerTag)
            {
                QueueRemoval(InlineValue(entry));
            }
            else if (entry.Tag == SubIfdsTag && length <= 4)
            {
                QueueRemoval(InlineValue(entry));
            }
            else if (entry.Tag == SubIfdsTag && entry.ValueOrOffset < data.Length)
            {
                // Only the pointers that lie inside the block can be read.
                var readable = Math.Min(entry.Count, (data.Length - entry.ValueOrOffset) / 4);
                for (var i = 0L; i < readable; i++)
                {
                    QueueRemoval(ReadU32((int)(entry.ValueOrOffset + (i * 4))));
                }
            }
        }

        // Queues a removed directory once and charges the pointer against the budget, so the
        // number of pointers declared by removed entries cannot multiply the work.
        private void QueueRemoval(uint offset)
        {
            Spend(4);
            if (_queuedDirectories.Add(offset))
            {
                _removedDirectories.Push(offset);
            }
        }

        private void Spend(long bytes)
        {
            _spent += bytes;
            if (_spent > _budget)
            {
                throw new MalformedExifException();
            }
        }

        // Plans zeroing a removed directory, its out-of-line values and its thumbnail, and queues
        // every directory it points to. A directory outside the block has nothing in it to zero,
        // and a kept directory or one already planned is left alone.
        private void PlanIfdRemoval(uint offset)
        {
            if (_keptOffsets.Contains(offset) || !_zeroedOffsets.Add(offset) || ReadIfd(offset) is not var (entries, next))
            {
                return;
            }

            var thumbnailOffset = entries.FirstOrDefault(e => e.Tag == ThumbnailOffsetTag);
            var thumbnailLength = entries.FirstOrDefault(e => e.Tag == ThumbnailLengthTag);
            if (thumbnailOffset.Raw is not null && thumbnailLength.Raw is not null)
            {
                PlanZero(InlineValue(thumbnailOffset), InlineValue(thumbnailLength));
            }

            foreach (var entry in entries)
            {
                PlanEntryRemoval(entry);
            }

            if (next != 0)
            {
                QueueRemoval(next);
            }

            PlanZero(offset, 2 + (entries.Count * 12) + 4L);
        }

        // Zero ranges are cut to the block: bytes a broken offset or length points past the end
        // are not part of the block and hold nothing to remove.
        private void PlanZero(long offset, long length)
        {
            var start = Math.Max(offset, HeaderLength);
            var end = Math.Min(offset + length, data.Length);
            if (start < end)
            {
                _zeroRanges.Add((start, end));
            }
        }

        private void Protect(long offset, long length)
        {
            var end = Math.Min(offset + length, data.Length);
            if (offset < end)
            {
                _protectedRanges.Add((offset, end));
            }
        }

        // Merges both range lists and clears every zero range minus the protected ranges, so the
        // work is linear in the block length however many entries overlap.
        private void ApplyZeroRanges()
        {
            var zero = Merge(_zeroRanges);
            var keep = Merge(_protectedRanges);
            var k = 0;
            foreach (var (start, end) in zero)
            {
                var position = start;
                while (position < end)
                {
                    while (k < keep.Count && keep[k].End <= position)
                    {
                        k++;
                    }

                    if (k < keep.Count && keep[k].Start <= position)
                    {
                        position = keep[k].End;
                        continue;
                    }

                    var stop = k < keep.Count ? Math.Min(end, keep[k].Start) : end;
                    Array.Clear(data, (int)position, (int)(stop - position));
                    position = stop;
                }
            }
        }

        private static List<(long Start, long End)> Merge(List<(long Start, long End)> ranges)
        {
            var merged = new List<(long Start, long End)>(ranges.Count);
            foreach (var (start, end) in ranges.OrderBy(r => r.Start))
            {
                if (merged.Count > 0 && start <= merged[^1].End)
                {
                    merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
                }
                else
                {
                    merged.Add((start, end));
                }
            }

            return merged;
        }

        // Reads a directory, or returns null when its table does not fit in the block. Throws when
        // the work budget of the block is exhausted.
        private (List<Entry> Entries, uint Next)? ReadIfd(uint offset)
        {
            if (offset < HeaderLength || offset + 2L > data.Length)
            {
                return null;
            }

            var count = ReadU16((int)offset);
            if (offset + 2L + (count * 12L) + 4 > data.Length)
            {
                return null;
            }

            Spend(2 + (count * 12L) + 4);

            var entries = new List<Entry>(count);
            for (var i = 0; i < count; i++)
            {
                var position = (int)offset + 2 + (i * 12);
                entries.Add(new Entry(
                    ReadU16(position),
                    ReadU16(position + 2),
                    ReadU32(position + 4),
                    ReadU32(position + 8),
                    data[position..(position + 12)]));
            }

            return (entries, ReadU32((int)offset + 2 + (count * 12)));
        }

        // The value of a single inline SHORT sits in the first two bytes of the value field in
        // the block's byte order; every other inline value is read as a LONG.
        private uint InlineValue(Entry entry) =>
            entry.Type is 3 or 8
                ? (_bigEndian
                    ? BinaryPrimitives.ReadUInt16BigEndian(entry.Raw.AsSpan(8))
                    : BinaryPrimitives.ReadUInt16LittleEndian(entry.Raw.AsSpan(8)))
                : entry.ValueOrOffset;

        private static long? ValueLength(Entry entry) => TypeSize(entry.Type) is { } size ? size * (long)entry.Count : null;

        private static int? TypeSize(ushort type) => type switch
        {
            1 or 2 or 6 or 7 => 1,
            3 or 8 => 2,
            4 or 9 or 11 or 13 => 4,
            5 or 10 or 12 => 8,
            _ => null,
        };

        private ushort ReadU16(int position) =>
            _bigEndian
                ? BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position))
                : BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));

        private uint ReadU32(int position) =>
            _bigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position))
                : BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));

        private void WriteU16(int position, ushort value)
        {
            if (_bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(position), value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(position), value);
            }
        }
    }
}
