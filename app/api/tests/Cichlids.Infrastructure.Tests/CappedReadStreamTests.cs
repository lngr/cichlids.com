using Cichlids.Infrastructure.Storage;

namespace Cichlids.Infrastructure.Tests;

public sealed class CappedReadStreamTests
{
    [Fact]
    public async Task Reads_content_up_to_the_cap()
    {
        var content = new byte[100];
        Random.Shared.NextBytes(content);
        await using var capped = new CappedReadStream(new MemoryStream(content), "k", maxBytes: 100);

        using var buffer = new MemoryStream();
        await capped.CopyToAsync(buffer);

        Assert.Equal(content, buffer.ToArray());
    }

    [Fact]
    public async Task Throws_once_the_inner_stream_delivers_more_than_the_cap()
    {
        // The inner stream stands for a response whose Content-Length claimed at most the cap
        // while the body is longer.
        await using var capped = new CappedReadStream(new MemoryStream(new byte[101]), "lying.bin", maxBytes: 100);

        var ex = await Assert.ThrowsAsync<ObjectTooLargeException>(() => capped.CopyToAsync(Stream.Null));

        Assert.Equal("lying.bin", ex.Key);
    }

    [Fact]
    public void Throws_on_a_synchronous_read_past_the_cap()
    {
        using var capped = new CappedReadStream(new MemoryStream(new byte[10]), "k", maxBytes: 4);
        var buffer = new byte[3];

        Assert.Equal(3, capped.Read(buffer, 0, 3));
        Assert.Throws<ObjectTooLargeException>(() => capped.Read(buffer, 0, 3));
    }

    [Fact]
    public void Disposes_the_owner_together_with_the_stream()
    {
        var owner = new DisposeProbe();
        var capped = new CappedReadStream(new MemoryStream([1]), "k", maxBytes: 4, owner);

        capped.Dispose();

        Assert.True(owner.Disposed);
    }

    private sealed class DisposeProbe : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
