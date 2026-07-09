using Cichlids.Api.Features.Common;
using Cichlids.Domain.Enums;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.LegacyRedirects;

/// <summary>
/// Anonymous endpoints for every legacy URL pattern of the predecessor site (ADR-0021). Every
/// response is either a permanent redirect to the pattern's current route or a 410 Gone when the
/// pattern is well-formed but names nothing that migrated; nothing here ever renders content
/// itself. Routes live at the site root (not under <c>/api</c>) because that is where the legacy
/// links point, and are chosen so none of them can collide with an <c>/api/...</c> route.
/// </summary>
public static class LegacyRedirectsEndpoints
{
    // The three legacy Phorum forum ids, in the same numbering ForumMigrationStep reads them
    // with, needed here to turn a list.php forum id into the matching community category.
    private static readonly Dictionary<int, DiscussionCategory> CategoryByForumId = new()
    {
        [1] = DiscussionCategory.Cichlids,
        [2] = DiscussionCategory.African,
        [3] = DiscussionCategory.MarketPlace,
    };

    public static IEndpointRouteBuilder MapLegacyRedirectsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pictures/pic/{alias}.html", ResolvePictureAsync).WithName("LegacyPictureRedirectHtml");
        app.MapGet("/pictures/pic/{alias}", ResolvePictureAsync).WithName("LegacyPictureRedirect");
        app.MapGet("/tanks/details/{legacyId}", ResolveTankAsync).WithName("LegacyTankRedirect");
        app.MapGet("/members/{legacyUid}/{*rest}", ResolveMemberAsync).WithName("LegacyMemberRedirectWithRest");
        app.MapGet("/members/{legacyUid}", ResolveMemberAsync).WithName("LegacyMemberRedirect");
        app.MapGet("/browse/species/{alias}.html", ResolveSpeciesAsync).WithName("LegacySpeciesRedirect");
        app.MapGet("/disc/read.php", ResolveDiscussionReadAsync).WithName("LegacyDiscussionReadRedirect");
        app.MapGet("/disc/list.php", ResolveDiscussionListAsync).WithName("LegacyDiscussionListRedirect");
        app.MapGet("/wiki/index.php/{name}", ResolveWikiAsync).WithName("LegacyWikiRedirect");

        return app;
    }

    private static async Task<Results<RedirectHttpResult, StatusCodeHttpResult>> ResolvePictureAsync(
        LegacyRedirectsQueryService queryService, string alias, CancellationToken cancellationToken)
    {
        var canonicalSlug = await queryService.ResolvePictureCanonicalSlugAsync(alias, cancellationToken);
        return canonicalSlug is null ? Gone() : Redirect($"/pictures/{canonicalSlug}");
    }

    private static async Task<Results<RedirectHttpResult, StatusCodeHttpResult>> ResolveTankAsync(
        LegacyRedirectsQueryService queryService, string legacyId, CancellationToken cancellationToken)
    {
        if (!int.TryParse(legacyId, out var parsedLegacyId))
        {
            return Gone();
        }

        var tankId = await queryService.ResolveTankIdAsync(parsedLegacyId, cancellationToken);
        return tankId is null ? Gone() : Redirect($"/tanks/{tankId}");
    }

    private static async Task<Results<RedirectHttpResult, StatusCodeHttpResult>> ResolveMemberAsync(
        LegacyRedirectsQueryService queryService, string legacyUid, CancellationToken cancellationToken)
    {
        if (!int.TryParse(legacyUid, out var parsedLegacyUid))
        {
            return Gone();
        }

        var profileId = await queryService.ResolveMemberProfileIdAsync(parsedLegacyUid, cancellationToken);
        return profileId is null ? Gone() : Redirect($"/members/{profileId}");
    }

    private static async Task<Results<RedirectHttpResult, StatusCodeHttpResult>> ResolveSpeciesAsync(
        LegacyRedirectsQueryService queryService, string alias, CancellationToken cancellationToken)
    {
        var slug = await queryService.ResolveSpeciesSlugAsync(alias, cancellationToken);
        return slug is null ? Gone() : Redirect($"/species/{slug}");
    }

    /// <summary>
    /// Phorum's read.php took its target as a raw, comma-separated, key-less query string
    /// (forum id, thread legacy id, and optionally a post legacy id for the in-thread anchor); the
    /// post legacy id plays no part in resolving the redirect target, only the thread does.
    /// </summary>
    private static async Task<Results<RedirectHttpResult, StatusCodeHttpResult>> ResolveDiscussionReadAsync(
        LegacyRedirectsQueryService queryService, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var tokens = ParseRawQueryTokens(httpContext.Request.QueryString.Value);
        if (tokens.Length < 2 || !int.TryParse(tokens[1], out var threadLegacyId))
        {
            return Gone();
        }

        var threadId = await queryService.ResolveDiscussionThreadIdAsync(threadLegacyId, cancellationToken);
        return threadId is null ? Gone() : Redirect($"/community/{threadId}");
    }

    /// <summary>
    /// Phorum's list.php took its target forum as a raw, single-value query string with no key.
    /// </summary>
    private static Results<RedirectHttpResult, StatusCodeHttpResult> ResolveDiscussionListAsync(HttpContext httpContext)
    {
        var tokens = ParseRawQueryTokens(httpContext.Request.QueryString.Value);
        if (tokens.Length < 1 || !int.TryParse(tokens[0], out var forumId) || !CategoryByForumId.TryGetValue(forumId, out var category))
        {
            return Gone();
        }

        return Redirect($"/community?category={SnakeCaseEnum.ToSnakeCase(category)}");
    }

    private static async Task<Results<RedirectHttpResult, StatusCodeHttpResult>> ResolveWikiAsync(
        LegacyRedirectsQueryService queryService, string name, CancellationToken cancellationToken)
    {
        var decodedName = name.Replace('_', ' ');
        var slug = await queryService.ResolveWikiSpeciesSlugAsync(decodedName, cancellationToken);
        return slug is null ? Gone() : Redirect($"/species/{slug}");
    }

    private static string[] ParseRawQueryTokens(string? rawQueryString)
    {
        if (string.IsNullOrEmpty(rawQueryString))
        {
            return [];
        }

        var value = rawQueryString.StartsWith('?') ? rawQueryString[1..] : rawQueryString;
        return value.Length == 0 ? [] : value.Split(',', StringSplitOptions.TrimEntries);
    }

    private static RedirectHttpResult Redirect(string relativeUrl) => TypedResults.Redirect(relativeUrl, permanent: true);

    private static StatusCodeHttpResult Gone() => TypedResults.StatusCode(StatusCodes.Status410Gone);
}
