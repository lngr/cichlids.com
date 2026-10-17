using System.Text.Json;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Library;
using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Onboarding;

/// <summary>
/// Adopts the archive tanks of a member outside the onboarding: lists the migrated photos that can be assigned
/// to a tank in the order of their dates and adopts the tank with the chosen photos as showcase links. Problem
/// codes: legacy_tank_not_found, tank_already_adopted, adoption_photos_invalid, media_not_found and
/// media_already_linked.
/// </summary>
public sealed class ArchiveTankAdoptionService(
    CichlidsDbContext context, MediaLibraryService library, MediaLibraryQueryService libraryQuery, TimeProvider clock)
{
    public const string TankNotFoundCode = "legacy_tank_not_found";
    public const string AlreadyAdoptedCode = "tank_already_adopted";
    public const string PhotosInvalidCode = "adoption_photos_invalid";
    public const int MaxPhotos = 100;
    private const string ReturnedBadge = "returned";

    /// <summary>
    /// Whether the ids are a usable choice: positive, without duplicates and at most MaxPhotos.
    /// </summary>
    public static bool ValidPhotoIds(long[]? ids) =>
        ids is not null && ids.Length <= MaxPhotos && ids.All(i => i > 0) && ids.Distinct().Count() == ids.Length;

    /// <summary>
    /// A page of the migrated photos of the member that are linked neither to the tank nor to the profile, in the
    /// order of their dates (capture time, publication or creation time of their story, time of the media item).
    /// The error is a problem code.
    /// </summary>
    public async Task<(PagedResponse<PhotoSuggestionDto>? Page, string? Error)> SuggestAsync(
        long profileId, long tankId, int offset, int limit, CancellationToken cancellationToken)
    {
        var tank = await context.Tanks.AsNoTracking().Where(t => t.Id == tankId && t.ProfileId == profileId && t.LegacyId != null
                && t.State == TankState.Published && t.DeletedAt == null)
            .Select(t => new { t.AdoptedAt }).SingleOrDefaultAsync(cancellationToken);
        if (tank is null)
        {
            return (null, TankNotFoundCode);
        }

        if (tank.AdoptedAt is not null)
        {
            return (null, AlreadyAdoptedCode);
        }

        var candidates = context.MediaItems.Where(m => m.OwnerProfileId == profileId && m.LegacyId != null && m.Kind == MediaKind.Photo
            && !context.MediaLinks.Any(l => l.MediaItemId == m.Id && (l.TankId == tankId || l.ContextType == MediaLinkContext.Profile)));
        var rows = await candidates.AsNoTracking()
            .Select(m => new { m.Id, m.TakenAtLocal, m.TakenAtOffsetMinutes, m.CreatedAt }).ToListAsync(cancellationToken);
        var storyDates = (await context.MediaLinks.StoryLinks().Where(l => candidates.Any(m => m.Id == l.MediaItemId))
                .Join(context.Stories.Where(s => s.DeletedAt == null), l => l.StoryId, s => s.Id,
                    (l, s) => new { l.MediaItemId, Date = s.PublishedAt ?? s.CreatedAt })
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.MediaItemId).ToDictionary(g => g.Key, g => g.Min(r => r.Date));

        var ordered = rows.Select(r =>
            {
                DateTimeOffset? takenAt = r.TakenAtLocal is { } local
                    ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromMinutes(r.TakenAtOffsetMinutes ?? 0))
                    : null;
                return (r.Id, TakenAt: takenAt, Date: takenAt ?? storyDates.GetValueOrDefault(r.Id, r.CreatedAt));
            })
            .OrderBy(r => r.Date).ThenBy(r => r.Id).ToList();
        var page = ordered.Skip(offset).Take(limit).ToList();
        var images = await libraryQuery.PhotoImagesAsync(profileId, page.Select(r => r.Id).ToList(), cancellationToken);
        return (new PagedResponse<PhotoSuggestionDto>(
            ordered.Count, page.Select(r => new PhotoSuggestionDto(r.Id, images.GetValueOrDefault(r.Id), r.TakenAt, r.Date)).ToList()), null);
    }

    /// <summary>
    /// Adopts the archive tank of the member in one transaction under the row locks of the profile and the tank,
    /// links the chosen photos with the showcase role in the chosen order, records tank.adopted and awards the
    /// returned badge when the member does not hold it yet. The result badge is null when the member held it
    /// before. The error is a problem code and nothing is stored then.
    /// </summary>
    public async Task<(AdoptTankResultDto? Result, string? Error)> AdoptAsync(
        long profileId, long tankId, long[] mediaItemIds, CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var profile = await context.Profiles.FromSqlInterpolated($"SELECT * FROM public.profile WHERE id = {profileId} FOR UPDATE").SingleAsync(cancellationToken);
        await context.Entry(profile).ReloadAsync(cancellationToken);
        var tank = await context.Tanks.FromSqlInterpolated($"SELECT * FROM public.tank WHERE id = {tankId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken);
        if (tank is null || tank.ProfileId != profileId || tank.LegacyId is null || tank.State != TankState.Published || tank.DeletedAt != null)
        {
            return (null, TankNotFoundCode);
        }

        if (tank.AdoptedAt is not null)
        {
            return (null, AlreadyAdoptedCode);
        }

        if (await library.LinkPhotosToTankAsync(profileId, tankId, MediaLinkRole.Showcase, mediaItemIds, cancellationToken) is { } linkError)
        {
            return (null, linkError);
        }

        var now = clock.GetUtcNow();
        tank.AdoptedAt = now;
        string? badge = null;
        if (!profile.Badges.Contains(ReturnedBadge))
        {
            profile.Badges = profile.Badges.Append(ReturnedBadge).ToArray();
            badge = ReturnedBadge;
        }

        context.OutboxEvents.Add(OutboxEvent.Create("tank.adopted", "tank", tankId.ToString(),
            JsonSerializer.Serialize(new { tankId, profileId }, new JsonSerializerOptions(JsonSerializerDefaults.Web)), now));
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (new AdoptTankResultDto(tankId, tank.Title, badge), null);
    }
}
