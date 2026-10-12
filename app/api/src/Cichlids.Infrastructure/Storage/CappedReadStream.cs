namespace Cichlids.Infrastructure.Storage;

/// <summary>
/// A forward-only read stream over another stream that throws ObjectTooLargeException as soon as
/// more than the given number of bytes has been read from it. Disposing it disposes the inner
/// stream and the optional owner, for example the response the inner stream belongs to.
/// </summary>
public sealed class CappedReadStream(Stream inner, string key, long maxBytes, IDisposable? owner = null) : Stream
{
    private long _bytesRead;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _bytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            owner?.Dispose();
        }

        base.Dispose(disposing);
    }

    private int Count(int read)
    {
        _bytesRead += read;
        if (_bytesRead > maxBytes)
        {
            throw new ObjectTooLargeException(key, maxBytes);
        }

        return read;
    }
}
