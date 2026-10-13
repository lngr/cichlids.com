namespace Cichlids.Infrastructure.Storage;

/// <summary>
/// Thrown when a stored object is larger than the configured download cap, either by its
/// declared length or by the bytes its body delivers.
/// </summary>
public sealed class ObjectTooLargeException(string key, long maxBytes)
    : Exception($"The object '{key}' is larger than the download limit of {maxBytes} bytes.")
{
    public string Key { get; } = key;

    public long MaxBytes { get; } = maxBytes;
}
