using Cichlids.Api.Features.Common;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Cichlids.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Comments;

/// <summary>
/// Query logic shared by the picture and tank comment endpoints: only non-deleted comments,
/// oldest first, with the author resolved when the comment has one.
/// </summary>
public sealed class CommentsQueryService(CichlidsDbContext context, IObjectStore objectStore)
{
    public Task<PagedResponse<CommentDto>> ListForPostAsync(long postId, int offset, int limit, CancellationToken cancellationToken) =>
        ListAsync(context.Comments.Where(c => c.PostId == postId), offset, limit, cancellationToken);

    public Task<PagedResponse<CommentDto>> ListForTankAsync(long tankId, int offset, int limit, CancellationToken cancellationToken) =>
        ListAsync(context.Comments.Where(c => c.TankId == tankId), offset, limit, cancellationToken);

    private async Task<PagedResponse<CommentDto>> ListAsync(
        IQueryable<Comment> scope, int offset, int limit, CancellationToken cancellationToken)
    {
        var visible = scope.Where(c => c.DeletedAt == null);

        var total = await visible.CountAsync(cancellationToken);

        var page = await visible
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Skip(offset).Take(limit)
            .Select(c => new { c.Id, c.Body, c.CreatedAt, c.Score, c.PosterName, c.AuthorProfileId })
            .ToListAsync(cancellationToken);

        var authorIds = page.Where(c => c.AuthorProfileId != null).Select(c => c.AuthorProfileId!.Value).Distinct().ToList();

        var authors = await AuthorBatchLoader.LoadAsync(context, authorIds, cancellationToken);

        var items = page
            .Select(c => new CommentDto(
                c.Id,
                c.Body,
                c.CreatedAt,
                c.Score,
                c.AuthorProfileId is { } authorId ? authors[authorId].ToDto(objectStore) : null,
                c.AuthorProfileId is null ? c.PosterName : null))
            .ToList();

        return new PagedResponse<CommentDto>(total, items);
    }
}
