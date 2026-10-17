using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Tanks;
using Cichlids.Api.Features.Videos;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Fish;

/// <summary>
/// A tank a named fish lives or lived in. Deleted is true for a tank its owner deleted.
/// </summary>
public sealed record FishTankDto(long Id, string Title, bool Deleted);

/// <summary>
/// A photo on the page of a named fish.
/// </summary>
public sealed record FishPhotoDto(long MediaItemId, int? Width, int? Height, ImageUrlsDto Image, DateTimeOffset CreatedAt);

/// <summary>
/// The kinds of items on the timeline of a named fish. MovedIn, Growth, Spawning, Moved and Died come from
/// the day it moved in and from its events, Left from the removal of a fish that was given away or moved, and
/// Anniversary from the completed years since the move-in.
/// </summary>
public enum FishTimelineKind { MovedIn, Growth, Spawning, Moved, Died, Left, Anniversary }

/// <summary>
/// A measured length: the centimetres the fish is stored with and the value and unit the owner typed.
/// </summary>
public sealed record FishGrowthDto(decimal LengthCm, decimal InputValue, DimensionUnit InputUnit);

/// <summary>
/// A spawning. Generation is the generation of a pure-bred offspring (1 for F1) and is null for a hybrid.
/// </summary>
public sealed record FishSpawningDto(int? Generation, bool Hybrid);

/// <summary>
/// A move between tanks of the owner. A tank is null when the viewer may not see it.
/// </summary>
public sealed record FishMoveDto(FishTankDto? FromTank, FishTankDto? ToTank);

/// <summary>
/// One item of the timeline of a named fish, dated by On (YYYY-MM-DD). EventId names the stored event of a
/// growth, spawning, move or death. The member that matches the kind is set: Growth, Spawning and Move for
/// their kinds, Year for an anniversary and Reason for a fish that was given away or moved.
/// </summary>
public sealed record FishTimelineItemDto(
    FishTimelineKind Kind, string On, long? EventId, FishGrowthDto? Growth, FishSpawningDto? Spawning, FishMoveDto? Move,
    int? Year, InhabitantRemovalReason? Reason);

/// <summary>
/// The page of a named fish: its name and species, the tank it lives in (or lived in last), its dates, its
/// photos and videos newest first and its timeline newest first. A fish that died is a memorial. IsOwner is
/// true for the member the fish belongs to.
/// </summary>
public sealed record FishDto(
    long Id, string Name, InhabitantSpeciesRefDto? Species, AuthorDto Owner, bool IsOwner, FishTankDto Tank, string? MovedInOn,
    string? RemovedOn, InhabitantRemovalReason? RemovalReason, bool Memorial, IReadOnlyList<FishPhotoDto> Photos,
    IReadOnlyList<VideoDto> Videos, IReadOnlyList<FishTimelineItemDto> Timeline);

/// <summary>
/// A measured length. Unit is cm or inch and the length is positive.
/// </summary>
public sealed record RecordGrowthRequest(decimal? Length, string? Unit, string? On);

/// <summary>
/// A spawning. A pure-bred offspring needs its Generation (1 for F1); a Hybrid offspring has none.
/// </summary>
public sealed record RecordSpawningRequest(int? Generation, bool? Hybrid, string? On);

/// <summary>
/// The move of a fish to another live tank of its owner.
/// </summary>
public sealed record MoveFishRequest(long? ToTankId, string? On);

/// <summary>
/// The death of a fish.
/// </summary>
public sealed record RecordDeathRequest(string? On);

/// <summary>
/// A file of the media library of the owner to show on the page of the fish.
/// </summary>
public sealed record LinkFishMediaRequest(long? MediaItemId);
