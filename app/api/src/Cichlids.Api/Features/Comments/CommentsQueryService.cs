using Cichlids.Api.Features.Common;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Comments;

/// <summary>
/// Query logic shared by the picture and tank comment endpoints: only non-deleted comments,
/// oldest first, with the author resolved when the comment has one and the stars of the rating
/// given together with the comment. A comment and its rating are separate rows; they belong
/// together when they share the legacy comment id (migrated rows) or, for comments written in
/// this application, when they have the same target, author and creation moment (one request
/// creates both).
/// </summary>
public sealed class CommentsQueryService(CichlidsDbContext context, IObjectStore objectStore)
{
    public Task<PagedResponse<CommentDto>> ListForPostAsync(long postId, int offset, int limit, CancellationToken cancellationToken) =>
        ListAsync(
            context.Comments.Where(c => c.PostId == postId),
            context.Ratings.Where(r => r.PostId == postId),
            offset, limit, cancellationToken);

    public Task<PagedResponse<CommentDto>> ListForTankAsync(long tankId, int offset, int limit, CancellationToken cancellationToken) =>
        ListAsync(
            context.Comments.Where(c => c.TankId == tankId),
            context.Ratings.Where(r => r.TankId == tankId),
            offset, limit, cancellationToken);

    private async Task<PagedResponse<CommentDto>> ListAsync(
        IQueryable<Comment> scope, IQueryable<Rating> ratingScope, int offset, int limit, CancellationToken cancellationToken)
    {
        var visible = scope.Where(c => c.DeletedAt == null);

        var total = await visible.CountAsync(cancellationToken);

        var page = await visible
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip(offset).Take(limit)
            .Select(c => new { c.Id, c.LegacyId, c.Body, c.CreatedAt, c.Score, c.PosterName, c.AuthorProfileId })
            .ToListAsync(cancellationToken);

        var authorIds = page.Where(c => c.AuthorProfileId != null).Select(c => c.AuthorProfileId!.Value).Distinct().ToList();

        var authors = await AuthorBatchLoader.LoadAsync(context, authorIds, cancellationToken);

        var legacyIds = page.Where(c => c.LegacyId != null).Select(c => c.LegacyId).ToList();
        var createdAts = page.Where(c => c.LegacyId == null && c.AuthorProfileId != null).Select(c => c.CreatedAt).Distinct().ToList();

        var ratings = legacyIds.Count == 0 && createdAts.Count == 0
            ? []
            : await ratingScope
                .Where(r => (r.LegacyCommentId != null && legacyIds.Contains(r.LegacyCommentId))
                    || (r.LegacyCommentId == null && createdAts.Contains(r.CreatedAt)))
                .Select(r => new { r.LegacyCommentId, r.ProfileId, r.CreatedAt, r.Stars })
                .ToListAsync(cancellationToken);

        short? StarsOf(int? legacyId, long? authorProfileId, DateTimeOffset createdAt) =>
            legacyId is not null
                ? ratings.FirstOrDefault(r => r.LegacyCommentId == legacyId)?.Stars
                : ratings.FirstOrDefault(r =>
                    r.LegacyCommentId == null && r.ProfileId == authorProfileId && r.CreatedAt == createdAt)?.Stars;

        var items = page
            .Select(c => new CommentDto(
                c.Id,
                c.Body,
                c.CreatedAt,
                c.Score,
                c.AuthorProfileId is null && c.LegacyId is null ? null : StarsOf(c.LegacyId, c.AuthorProfileId, c.CreatedAt),
                c.AuthorProfileId is { } authorId ? authors[authorId].ToDto(objectStore) : null,
                c.AuthorProfileId is null ? c.PosterName : null))
            .ToList();

        return new PagedResponse<CommentDto>(total, items);
    }
}
