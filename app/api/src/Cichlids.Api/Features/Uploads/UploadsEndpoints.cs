using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Pictures;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Media;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cichlids.Api.Features.Uploads;

public static class UploadsEndpoints
{
    /// <summary>
    /// Largest accepted photo file, 25 MiB.
    /// </summary>
    public const long MaxFileBytes = 25L * 1024 * 1024;

    // Request body limit for an upload: the largest accepted file plus room for the multipart
    // boundaries and part headers, so a file at the limit reaches the size check in the handler.
    private const long MaxRequestBytes = MaxFileBytes + (1024 * 1024);

    private const int MaxTitleLength = 200;
    private const int MaxDescriptionLength = 5000;

    private static readonly PostTopic[] PublishableTopics = [PostTopic.Cichlids, PostTopic.Tanks, PostTopic.Offtopic];

    public static IEndpointRouteBuilder MapUploadsEndpoints(this IEndpointRouteBuilder app)
    {
        // Callers authenticate with a bearer token, never a cookie, so a cross-site form post
        // cannot act on a user's behalf and antiforgery validation has nothing to protect.
        app.MapPost("/api/uploads", UploadAsync)
            .WithName("UploadPhoto")
            .WithTags("Uploads")
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes))
            .WithFormOptions(multipartBodyLengthLimit: MaxRequestBytes)
            .Produces(StatusCodes.Status413PayloadTooLarge);

        app.MapGet("/api/me/drafts", ListDraftsAsync)
            .WithName("ListMyDrafts")
            .WithTags("Me")
            .RequireAuthorization();

        app.MapPost("/api/posts/{id:long}/publish", PublishAsync)
            .WithName("PublishPost")
            .WithTags("Posts")
            .RequireAuthorization();

        app.MapDelete("/api/posts/{id:long}", DiscardAsync)
            .WithName("DiscardPost")
            .WithTags("Posts")
            .RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Validates a publish request: a title of 1 to 200 characters after trimming, a description
    /// of at most 5000 characters (blank counts as none), and a topic of cichlids, tanks or
    /// offtopic, cichlids when omitted. Returns the normalized values for the write path, or an
    /// error message for the 400 response.
    /// </summary>
    public static bool TryValidatePublish(
        PublishPostRequest request, out string title, out string? description, out PostTopic topic, out string error)
    {
        title = request.Title?.Trim() ?? string.Empty;
        description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        topic = PostTopic.Cichlids;
        error = string.Empty;

        if (title.Length is 0 or > MaxTitleLength)
        {
            error = $"title is required and must be at most {MaxTitleLength} characters.";
            return false;
        }

        if (description is { Length: > MaxDescriptionLength })
        {
            error = $"description must be at most {MaxDescriptionLength} characters.";
            return false;
        }

        if (request.Topic is not null
            && (!SnakeCaseEnum.TryParse<PostTopic>(request.Topic, out topic) || !PublishableTopics.Contains(topic)))
        {
            error = "topic must be one of: cichlids, tanks, offtopic.";
            return false;
        }

        return true;
    }

    private static async Task<Results<Created<DraftDto>, BadRequest<string>>> UploadAsync(
        UploadsWriteService uploadsWriteService,
        CurrentProfileService currentProfileService,
        ClaimsPrincipal user,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return TypedResults.BadRequest("A non-empty file field is required.");
        }

        if (file.Length > MaxFileBytes)
        {
            return TypedResults.BadRequest($"The file exceeds the limit of {MaxFileBytes / (1024 * 1024)} MB.");
        }

        var format = UploadImageFormat.FromContentType(file.ContentType);
        if (format is null)
        {
            return TypedResults.BadRequest("The file must be image/jpeg, image/png or image/webp.");
        }

        var bytes = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }

        if (!format.MatchesSignature(bytes))
        {
            return TypedResults.BadRequest($"The file content is not {format.ContentType}.");
        }

        var owner = await currentProfileService.ResolveAsync(user, cancellationToken);

        try
        {
            var draft = await uploadsWriteService.CreateDraftAsync(
                owner, bytes, format, Path.GetFileName(file.FileName), cancellationToken);
            return TypedResults.Created((string?)null, draft);
        }
        catch (ImageTooLargeException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
        catch (ImageProcessor.BrokenImageException)
        {
            return TypedResults.BadRequest("The file could not be decoded as an image.");
        }
    }

    private static async Task<Ok<IReadOnlyList<DraftDto>>> ListDraftsAsync(
        DraftsQueryService draftsQueryService,
        CurrentProfileService currentProfileService,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var profile = await currentProfileService.ResolveAsync(user, cancellationToken);
        return TypedResults.Ok(await draftsQueryService.ListAsync(profile.Id, cancellationToken));
    }

    private static async Task<Results<Ok<PictureDetailDto>, BadRequest<string>, NotFound, Conflict<string>>> PublishAsync(
        PostPublishService postPublishService,
        CurrentProfileService currentProfileService,
        ClaimsPrincipal user,
        long id,
        PublishPostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidatePublish(request, out var title, out var description, out var topic, out var error))
        {
            return TypedResults.BadRequest(error);
        }

        var author = await currentProfileService.ResolveAsync(user, cancellationToken);
        var (outcome, detail) = await postPublishService.PublishAsync(id, author.Id, title, description, topic, cancellationToken);

        return outcome switch
        {
            DraftActionOutcome.Succeeded => TypedResults.Ok(detail!),
            DraftActionOutcome.NotDraft => TypedResults.Conflict("Only a draft can be published."),
            _ => TypedResults.NotFound(),
        };
    }

    private static async Task<Results<NoContent, NotFound, Conflict<string>>> DiscardAsync(
        UploadsWriteService uploadsWriteService,
        CurrentProfileService currentProfileService,
        ClaimsPrincipal user,
        long id,
        CancellationToken cancellationToken)
    {
        var author = await currentProfileService.ResolveAsync(user, cancellationToken);
        var outcome = await uploadsWriteService.DiscardAsync(id, author.Id, cancellationToken);

        return outcome switch
        {
            DraftActionOutcome.Succeeded => TypedResults.NoContent(),
            DraftActionOutcome.NotDraft => TypedResults.Conflict("Only a draft can be discarded."),
            _ => TypedResults.NotFound(),
        };
    }
}
