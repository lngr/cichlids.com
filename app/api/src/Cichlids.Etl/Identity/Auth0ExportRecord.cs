namespace Cichlids.Etl.Identity;

/// <summary>
/// One member record from the Auth0 export: its subject id, contact email if present, whether
/// that email was verified with Auth0, and the connection it signed up through.
/// </summary>
public sealed record Auth0ExportRecord(string Id, string? Email, bool EmailVerified, string Connection);
