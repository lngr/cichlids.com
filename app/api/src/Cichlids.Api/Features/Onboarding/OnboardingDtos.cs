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
