namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Names of the authorization policies the endpoints require.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>
    /// Satisfied by the moderator or admin realm role; guards moderation actions such as
    /// deleting a comment.
    /// </summary>
    public const string Moderator = "moderator";

    /// <summary>
    /// Satisfied by the admin realm role only; guards operator-only actions such as registering
    /// webhook subscriptions.
    /// </summary>
    public const string Admin = "admin";

    /// <summary>
    /// Satisfied by an authenticated caller that is registered, which excludes the provisional
    /// profile of a device token. Guards every action that reaches other people.
    /// </summary>
    public const string Registered = "registered";
}
