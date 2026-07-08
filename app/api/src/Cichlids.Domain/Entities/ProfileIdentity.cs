namespace Cichlids.Domain.Entities;

/// <summary>
/// External identity (an OIDC subject, including a legacy identity provider) linked to a
/// profile for login mapping and identity provider migration.
/// </summary>
public class ProfileIdentity
{
    public long Id { get; set; }
    public long ProfileId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
