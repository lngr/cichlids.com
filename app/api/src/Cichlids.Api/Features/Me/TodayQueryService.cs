using System.Text.Json.Serialization;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Discover;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Features.Uploads;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Me;

public sealed record TodayTankDto(long Id, string Title, TankTimeLapsePhotoDto? NewestPhoto);
public sealed record TodayMemoryDto(long PostId, string? Slug, string? Title, int YearsAgo, ImageUrlsDto Image);
public sealed record TodayWeeklyTankDto(TankRegion Region, DiscoverItemDto Tank);
public sealed record TodayDto(string DisplayName, int? MemberSinceYear, IReadOnlyList<TodayTankDto> Tanks,
    int DraftCount, IReadOnlyList<DraftDto> Drafts, TodayWeeklyTankDto? TankOfTheWeek,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TodayMemoryDto? OnThisDay = null);

public sealed class TodayQueryService(CichlidsDbContext context, TankTimeLapseQueryService series,
    DraftsQueryService drafts, DiscoverQueryService discover, IObjectStore objectStore)
{
    public async Task<TodayDto> GetAsync(Profile profile, DateOnly date, CancellationToken cancellationToken)
    {
        var tanks = await context.Tanks.AsNoTracking()
            .Where(t => t.ProfileId == profile.Id && t.State == TankState.Published && t.DeletedAt == null)
            .OrderByDescending(t => t.PublishedAt ?? t.CreatedAt).ThenByDescending(t => t.Id)
            .Take(10).Select(t => new { t.Id, t.Title }).ToListAsync(cancellationToken);
        var photos = await series.GetManyAsync(tanks.Select(t => t.Id).ToArray(), cancellationToken, profile.Id, allowSigned: true);
        var cards = tanks.Select(t => new TodayTankDto(t.Id, t.Title,
            photos.GetValueOrDefault(t.Id)?.Photos.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.MediaItemId).FirstOrDefault())).ToList();
        var draftCount = await context.Posts.CountAsync(p => p.AuthorProfileId == profile.Id
            && p.State == PostState.Draft && p.DeletedAt == null, cancellationToken);
        var recentDrafts = await drafts.ListAsync(profile.Id, cancellationToken, 3);
        var memory = await MemoryAsync(profile.Id, date, cancellationToken);
        var archive = await discover.GetAsync(DiscoverQueryService.SelectableRegions, cancellationToken);
        var orderedRegions = profile.Interests.Select(interest => SnakeCaseEnum.TryParse<TankRegion>(interest, out var region) ? (TankRegion?)region : null)
            .Where(r => r != null && r != TankRegion.AmericasUnassigned).Select(r => r!.Value)
            .Concat(DiscoverQueryService.SelectableRegions).Distinct();
        var weekly = orderedRegions.Select(r => archive.Regions.FirstOrDefault(a => a.Region == r && a.TankOfTheWeek != null))
            .FirstOrDefault(r => r != null);
        return new TodayDto(profile.DisplayName ?? profile.Username, profile.LegacyId != null ? profile.CreatedAt.Year : null,
            cards, draftCount, recentDrafts, weekly == null ? null : new(weekly.Region, weekly.TankOfTheWeek!), memory);
    }

    private async Task<TodayMemoryDto?> MemoryAsync(long profileId, DateOnly date, CancellationToken cancellationToken)
    {
        var earlierThan = new DateTimeOffset(date.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var earlierThanLocal = new DateTime(date.Year, 1, 1);

        // A photo with a capture time counts on the calendar day it was taken (the wall-clock day
        // of the camera); a photo without one counts on the day its post was published.
        var match = await (from post in context.Posts.AsNoTracking()
            join link in context.PostMedia on post.Id equals link.PostId
            join media in context.MediaItems on link.MediaItemId equals media.Id
            let published = post.PublishedAt ?? post.CreatedAt
            where post.AuthorProfileId == profileId && post.State == PostState.Published && post.DeletedAt == null
                && media.Kind == MediaKind.Photo && (media.OwnerProfileId == null || media.OwnerProfileId == profileId)
                && (media.TakenAtLocal != null
                    ? media.TakenAtLocal < earlierThanLocal
                        && media.TakenAtLocal.Value.Month == date.Month && media.TakenAtLocal.Value.Day == date.Day
                    : published < earlierThan && published.Month == date.Month && published.Day == date.Day)
            let year = media.TakenAtLocal != null ? media.TakenAtLocal.Value.Year : published.Year
            orderby year descending, published descending, post.Id descending, link.Sort, link.Id
            select new { post.Id, post.Title, Year = year, MediaId = media.Id }).FirstOrDefaultAsync(cancellationToken);
        if (match == null) return null;
        var variants = await context.MediaVariants.Where(v => v.MediaItemId == match.MediaId)
            .ToDictionaryAsync(v => v.Label, v => v.StorageKey, cancellationToken);
        var slug = await context.SlugAliases.Where(s => s.PostId == match.Id && s.IsCanonical)
            .Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
        return new TodayMemoryDto(match.Id, slug, match.Title, date.Year - match.Year, new MediaUrlBuilder(objectStore).Build(variants));
    }
}
