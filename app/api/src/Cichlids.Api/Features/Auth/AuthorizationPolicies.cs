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
}
