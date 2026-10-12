namespace Cichlids.Infrastructure.Tests;

/// <summary>
/// Groups the test classes that call libvips, so they run one after another and the process-wide
/// libvips memory statistics of one test are not raised by another running in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class LibvipsCollection
{
    public const string Name = "Libvips";
}
