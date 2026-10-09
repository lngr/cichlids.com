using Cichlids.Domain.Enums;
using Cichlids.Etl.Persistence;
using Cichlids.Etl.Runtime;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cichlids.Etl.Steps;

/// <summary>
/// Resolves the owning profile for content whose legacy owner has no migrated member profile:
/// either a specific legacy user with no profile row (their account was hard-deleted or excluded
/// by <see cref="ProfileMigrationStep"/>), or anonymous legacy content with no owner at all
/// (<c>fe_user = 0</c>). Both attribution paths are shared by every step that migrates
/// user-owned content, so a tank and a picture from the same removed member land on the same
/// placeholder profile.
/// </summary>
public static class PlaceholderProfiles
{
    private static readonly ValueConverter<ProfileKind, string> KindConverter = new SnakeCaseEnumConverter<ProfileKind>();

    /// <summary>
    /// Finds or creates the archived placeholder profile for one specific legacy user id. Reuses
    /// the same <c>legacy_id</c> unique index <see cref="ProfileMigrationStep"/> upserts real
    /// member profiles on, so this is idempotent across runs and can never collide with a real
    /// profile for the same legacy user (a legacy id has at most one profile row of either kind).
    /// </summary>
    public static async Task<long> EnsurePlaceholderProfileAsync(
        EtlContext context, int legacyUserId, CancellationToken cancellationToken)
    {
        var values = new (string, object?)[]
        {
            ("legacy_id", legacyUserId),
            ("username", $"former-member-{legacyUserId}"),
            ("display_name", "Former member"),
            ("kind", KindConverter.ConvertToProvider(ProfileKind.Archived)),
            ("created_at", DateTimeOffset.UtcNow),
        };

        var (profileId, _) = await PgUpsert.UpsertAsync(
            context.Target, context.Transaction, "profile", "legacy_id", values, cancellationToken);
        return profileId;
    }

    /// <summary>
    /// Finds or creates the single shared profile that owns anonymous legacy content
    /// (<c>fe_user = 0</c>, which has no legacy user id to key a placeholder on). Idempotent over
    /// the fixed username instead of a legacy id, since this profile has none.
    /// </summary>
    public static async Task<long> EnsureCommunityArchiveProfileAsync(
        EtlContext context, CancellationToken cancellationToken)
    {
        var values = new (string, object?)[]
        {
            ("username", "community-archive"),
            ("display_name", "Community archive"),
            ("kind", KindConverter.ConvertToProvider(ProfileKind.System)),
            ("created_at", DateTimeOffset.UtcNow),
        };

        var (profileId, _) = await PgUpsert.UpsertAsync(
            context.Target, context.Transaction, "profile", "username", values, cancellationToken);
        return profileId;
    }
}
