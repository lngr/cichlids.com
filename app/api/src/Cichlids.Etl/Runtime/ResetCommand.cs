using Cichlids.Etl.Identity;

namespace Cichlids.Etl.Runtime;

/// <summary>
/// The "reset" command: returns the target database and the Keycloak realm to the state a first
/// run starts from. It empties and migrates the database, deletes every realm user who holds neither
/// the realm role admin nor the realm role tester directly and disallows self-registration. A user
/// who has one of these roles only through a group or a composite role is deleted. Before it drops anything it gets a Keycloak
/// admin token and reads the realm, so an unreachable Keycloak leaves the database as it is. A
/// missing realm counts as a realm without users. The object store is out of its reach, so media
/// objects stay. Runs only where the reset policy allows it.
/// </summary>
public static class ResetCommand
{
    private static readonly string[] KeptRealmRoles = ["admin", "tester"];
    private const int DeleteParallelism = 8;

    /// <summary>
    /// Runs the reset and returns 0 on success and 1 when the environment is refused or the
    /// Keycloak settings or preflight fail.
    /// </summary>
    public static Task<int> RunAsync(
        string targetConnectionString,
        KeycloakAdminSettings keycloak,
        string? environmentName,
        CancellationToken cancellationToken) =>
        RunAsync(targetConnectionString, () => keycloak, environmentName, cancellationToken);

    /// <summary>
    /// Runs the reset like the overload with settings, resolving the Keycloak settings only after
    /// the environment is allowed.
    /// </summary>
    public static async Task<int> RunAsync(
        string targetConnectionString,
        Func<KeycloakAdminSettings> resolveKeycloak,
        string? environmentName,
        CancellationToken cancellationToken)
    {
        try
        {
            ResetPolicy.EnsureAllowed(environmentName);
        }
        catch (ResetRefusedException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        KeycloakAdminSettings keycloak;
        try
        {
            keycloak = resolveKeycloak();
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        using var client = new KeycloakAdminClient(keycloak);

        bool realmExists;
        try
        {
            realmExists = await client.RealmExistsAsync(cancellationToken);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or InvalidOperationException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Console.Error.WriteLine(
                $"Keycloak at {keycloak.BaseUrl} could not be read ({ex.Message}); nothing was reset.");
            return 1;
        }

        if (!realmExists)
        {
            Console.Error.WriteLine(
                $"Warning: Keycloak at {keycloak.BaseUrl} has no realm '{keycloak.Realm}'; there are no users to delete.");
        }

        await TargetDatabaseReset.ResetAsync(targetConnectionString, environmentName, cancellationToken);

        if (!realmExists)
        {
            return 0;
        }

        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in KeptRealmRoles)
        {
            kept.UnionWith(await client.ListRealmRoleUserIdsAsync(role, cancellationToken));
        }

        var toDelete = (await client.ListUserIdsAsync(cancellationToken)).Where(id => !kept.Contains(id)).ToList();

        Console.WriteLine($"Deleting {toDelete.Count} user(s) of realm '{keycloak.Realm}'; keeping {kept.Count} with the realm role {string.Join(" or ", KeptRealmRoles)}...");
        var removed = await client.DeleteUsersAsync(toDelete, DeleteParallelism, cancellationToken);
        await client.CloseRegistrationAsync(cancellationToken);
        Console.WriteLine(
            $"Removed {removed} realm user(s) (deleted, or already gone) and closed self-registration.");

        return 0;
    }
}
