using Cichlids.Infrastructure.Storage;

namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// Deletes stored objects after the database change that made them unreferenced. A failing delete
/// is logged and leaves the object behind; it never undoes the committed change.
/// </summary>
public sealed class StoredObjectDeleter(IObjectStore objectStore, ILogger<StoredObjectDeleter> logger)
{
    // Runs without the request's cancellation token: cleanup that stops halfway because the
    // client went away would leave exactly the orphans it exists to prevent.
    public async Task DeleteAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            try
            {
                await objectStore.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete stored object {StorageKey}.", key);
            }
        }
    }
}
