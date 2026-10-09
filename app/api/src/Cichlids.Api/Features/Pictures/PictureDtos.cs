using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Media;
using Cichlids.Domain.Enums;

namespace Cichlids.Api.Features.Pictures;

public sealed record PictureListItemDto(
    long Id,
    string Slug,
    string? Title,
    string? Description,
    DateTimeOffset? PublishedAt,
    long ViewCount,
    double? RatingAverage,
    int RatingCount,
    int CommentCount,
    PostTopic Topic,
    AuthorDto Author,
    ImageUrlsDto? Image);

public sealed record PictureDetailDto(
    long Id,
    string Slug,
    string CanonicalSlug,
    string? Title,
    string? Description,
    DateTimeOffset? PublishedAt,
    long ViewCount,
    double? RatingAverage,
    int RatingCount,
    int CommentCount,
    PostTopic Topic,
    AuthorDto Author,
    ImageUrlsDto? Image);
