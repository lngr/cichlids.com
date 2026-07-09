namespace Cichlids.Api.Features.Profiles;

public sealed record ProfileStatsDto(int PictureCount, int TankCount, int CommentCount);

public sealed record ProfileDetailDto(
    long Id,
    string Username,
    string? DisplayName,
    string? City,
    string? CountryCode,
    DateTimeOffset CreatedAt,
    string? AvatarUrl,
    string? ProfileImageUrl,
    ProfileStatsDto Stats);
