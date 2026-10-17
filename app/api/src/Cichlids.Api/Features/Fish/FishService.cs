using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Fish;

/// <summary>
/// Reads the page of a named fish. The owner always reads the page of the own fish, also after the tank was
/// deleted. Everyone else reads it while they may see the tank the fish lives in, or lived in last: a
/// published tank that is not deleted and whose profile level reaches them. Every other caller meets the
/// answer for a missing fish. Photos and videos are signed for the viewer as on the tank page.
/// </summary>
public sealed class FishService(CichlidsDbContext context, IObjectStore objectStore, TimeProvider clock, MediaUrlSigner? signer = null)
{
    public const string NotFoundCode = "fish_not_found";

    private readonly MediaUrlBuilder _mediaUrlBuilder = new(objectStore, signer);

    /// <summary>
    /// The page of the fish for a viewer, or null when the fish does not exist, is not a named fish or the
    /// viewer may not see it. Anniversaries are shown up to the given local date.
    /// </summary>
    public async Task<FishDto?> GetAsync(long id, long? viewerProfileId, DateOnly today, CancellationToken cancellationToken)
    {
        var fish = await context.Inhabitants.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id && i.Name != null, cancellationToken);
        if (fish is null) return null;
        var isOwner = viewerProfileId is { } viewer && viewer == fish.OwnerProfileId;
        var tank = await context.Tanks.AsNoTracking().Where(t => t.Id == fish.TankId)
            .Select(t => new { t.Id, t.Title, Deleted = t.DeletedAt != null, t.State }).SingleAsync(cancellationToken);
        if (!isOwner && (tank.Deleted || tank.State != TankState.Published
            || !await context.TanksVisibleTo(viewerProfileId).AnyAsync(t => t.Id == tank.Id, cancellationToken)))
            return null;

        var events = await context.InhabitantEvents.AsNoTracking().Where(e => e.InhabitantId == id).ToListAsync(cancellationToken);
        var tanks = await ReachableTanksAsync(events.SelectMany(e => new[] { e.FromTankId, e.ToTankId }).OfType<long>().Append(fish.TankId),
            viewerProfileId, isOwner, cancellationToken);
        var timeline = FishTimeline.Order(FishTimeline.Items(fish, events, tanks, today));
        var (photos, videos) = await MediaAsync(id, viewerProfileId, isOwner, cancellationToken);
        var owner = (await AuthorBatchLoader.LoadAsync(context, [fish.OwnerProfileId], cancellationToken))[fish.OwnerProfileId].ToDto(objectStore);
        var dto = (await InhabitantMapper.ToDtosAsync(context, [fish], cancellationToken))[fish.Id];
        return new FishDto(
            fish.Id, fish.Name!, dto.Species, owner, isOwner, new FishTankDto(tank.Id, tank.Title, tank.Deleted), dto.MovedInOn, dto.RemovedOn,
            fish.RemovalReason, fish.RemovalReason == InhabitantRemovalReason.Died, photos, videos, timeline);
    }

    // The tanks among the given ids the viewer may name: all of them for the owner, otherwise the published
    // tanks that are not deleted and whose profile the viewer sees.
    private async Task<Dictionary<long, FishTankDto>> ReachableTanksAsync(
        IEnumerable<long> tankIds, long? viewerProfileId, bool isOwner, CancellationToken cancellationToken)
    {
        var ids = tankIds.Distinct().ToList();
        var query = isOwner
            ? context.Tanks.Where(t => t.ProfileId == viewerProfileId)
            : context.TanksVisibleTo(viewerProfileId).Where(t => t.State == TankState.Published && t.DeletedAt == null);
        return await query.AsNoTracking().Where(t => ids.Contains(t.Id))
            .Select(t => new FishTankDto(t.Id, t.Title, t.DeletedAt != null)).ToDictionaryAsync(t => t.Id, cancellationToken);
    }

    private async Task<(List<FishPhotoDto> Photos, List<VideoDto> Videos)> MediaAsync(
        long inhabitantId, long? viewerProfileId, bool isOwner, CancellationToken cancellationToken)
    {
        var items = await VideoQueryService.WithDetails(context.MediaItems.AsNoTracking()
                .Where(m => context.MediaLinks.Any(l => l.InhabitantId == inhabitantId && l.MediaItemId == m.Id)))
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).ToListAsync(cancellationToken);
        var ids = items.Select(i => i.Id).ToList();
        var now = clock.GetUtcNow();
        var anonymousVisible = await context.AnonymousVisibleMediaItemIdsAsync(ids, now, cancellationToken);
        var generations = await context.VisibleMediaGenerationsAsync(ids, viewerProfileId, now, cancellationToken);
        long? GenerationOf(MediaItem item) => generations.TryGetValue(item.Id, out var generation) ? generation : null;

        var photos = items.Where(i => i.Kind == MediaKind.Photo).Select(i => new FishPhotoDto(
            i.Id, i.Width, i.Height, _mediaUrlBuilder.Build(i.Variants, allowSigned: true, anonymousVisible.Contains(i.Id), GenerationOf(i)), i.CreatedAt)).ToList();
        var videos = items.Where(i => i.Kind == MediaKind.Video)
            .Select(i => VideoQueryService.ToPublicDto(i, objectStore, signer, allowSigned: true, anonymousVisible.Contains(i.Id), GenerationOf(i))).ToList();
        return (photos, isOwner ? videos : videos.Where(v => v.State == VideoState.Ready).ToList());
    }
}
