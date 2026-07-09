using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.LegacyRedirects;

/// <summary>
/// Resolves each legacy URL pattern (ADR-0021) to its current target: a picture slug alias, a
/// tank, member or species legacy id, a discussion thread legacy id, or a wiki article name
/// matched against the species catalog. Every method returns null when the pattern is well-formed
/// but names nothing that migrated, which the endpoint turns into a 410 Gone.
/// </summary>
public sealed class LegacyRedirectsQueryService(CichlidsDbContext context)
{
    /// <summary>
    /// Resolves a picture alias against every slug a post ever had (case-sensitive first, then a
    /// case-insensitive fallback for links that got the casing slightly wrong) and returns the
    /// post's current canonical slug.
    /// </summary>
    public async Task<string?> ResolvePictureCanonicalSlugAsync(string alias, CancellationToken cancellationToken)
    {
        var postId = await context.SlugAliases
            .Where(s => s.Value == alias)
            .Select(s => (long?)s.PostId)
            .FirstOrDefaultAsync(cancellationToken);

        if (postId is null)
        {
            postId = await context.SlugAliases
                .Where(s => EF.Functions.ILike(s.Value, alias))
                .Select(s => (long?)s.PostId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (postId is null)
        {
            return null;
        }

        return await context.SlugAliases
            .Where(s => s.PostId == postId && s.IsCanonical)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<long?> ResolveTankIdAsync(int legacyId, CancellationToken cancellationToken) =>
        context.Tanks.Where(t => t.LegacyId == legacyId).Select(t => (long?)t.Id).FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Resolves a member's legacy uid to its current profile id. Only profiles of kind
    /// <see cref="ProfileKind.Member"/> are eligible: a legacy account that only produced an
    /// unlisted placeholder profile never had a public member page to redirect to.
    /// </summary>
    public Task<long?> ResolveMemberProfileIdAsync(int legacyUid, CancellationToken cancellationToken) =>
        context.Profiles
            .Where(p => p.LegacyId == legacyUid && p.Kind == ProfileKind.Member)
            .Select(p => (long?)p.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<string?> ResolveSpeciesSlugAsync(string alias, CancellationToken cancellationToken) =>
        context.Species
            .Where(s => s.Slug != null && EF.Functions.ILike(s.Slug, alias))
            .Select(s => s.Slug)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<long?> ResolveDiscussionThreadIdAsync(int legacyId, CancellationToken cancellationToken) =>
        context.DiscussionThreads
            .Where(t => t.LegacyId == legacyId)
            .Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Resolves a wiki article name (already underscore-to-space normalized by the caller) to a
    /// species slug, trying an exact display-name match first, then the "Genus name" form, then
    /// the species' own slug re-derived from the same name, so a legacy title that only agrees
    /// with the catalog after one of these normalizations still finds its species. The catalog is
    /// small enough to compare in memory rather than push each fallback into SQL.
    /// </summary>
    public async Task<string?> ResolveWikiSpeciesSlugAsync(string decodedName, CancellationToken cancellationToken)
    {
        var candidates = await context.Species
            .Select(s => new { s.Genus, s.Name, s.DisplayName, s.Slug })
            .ToListAsync(cancellationToken);

        var byDisplayName = candidates.FirstOrDefault(s => string.Equals(s.DisplayName, decodedName, StringComparison.OrdinalIgnoreCase));
        if (byDisplayName is not null)
        {
            return byDisplayName.Slug;
        }

        var byGenusAndName = candidates.FirstOrDefault(
            s => string.Equals($"{s.Genus} {s.Name}", decodedName, StringComparison.OrdinalIgnoreCase));
        if (byGenusAndName is not null)
        {
            return byGenusAndName.Slug;
        }

        var normalizedSlug = NormalizeToSlug(decodedName);
        return candidates.FirstOrDefault(s => string.Equals(s.Slug, normalizedSlug, StringComparison.OrdinalIgnoreCase))?.Slug;
    }

    private static string NormalizeToSlug(string name) => name.Trim().ToLowerInvariant().Replace(' ', '-');
}
