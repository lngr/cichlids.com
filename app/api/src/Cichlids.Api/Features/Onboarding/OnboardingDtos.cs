using Cichlids.Api.Features.Media;
using Cichlids.Api.Features.Tanks;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Onboarding;

public sealed record OnboardingRequest(
    CreateTankRequest? Tank = null, long? LegacyTankId = null, TankRegion? Region = null,
    string[]? Interests = null, bool Skip = false);
public sealed record OnboardingResultDto(long? TankId, string? Title, string? Badge);
public sealed record LegacyTankDto(long Id, string Title, TankRegion? Region);
public sealed record InterestsRequest(string[] Interests);
public sealed record RefineRegionRequest(TankRegion Region);
public sealed record AdoptTankRequest(long[] MediaItemIds);
public sealed record AdoptTankResultDto(long TankId, string Title, string? Badge);

/// <summary>
/// A migrated photo of the member that can be assigned to an archive tank. Date is the moment the photo is
/// placed at: its capture time, otherwise the publication time of its story, otherwise the time of the media
/// item. TakenAt is the capture time and is null without one.
/// </summary>
public sealed record PhotoSuggestionDto(long MediaItemId, ImageUrlsDto? Image, DateTimeOffset? TakenAt, DateTimeOffset Date);
