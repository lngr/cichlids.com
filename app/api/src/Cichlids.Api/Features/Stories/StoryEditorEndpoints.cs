using System.Security.Claims;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Cichlids.Api.Features.Uploads;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Cichlids.Api.Features.Stories;

/// <summary>
/// The story editor of the signed-in member and the quick share of a file. Every route answers for the stories
/// and files of the caller only; the story of another member and the file of another member answer like a
/// missing one.
/// </summary>
public static class StoryEditorEndpoints
{
    public static IEndpointRouteBuilder MapStoryEditorEndpoints(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me/stories").WithTags("Stories").RequireAuthorization();
        me.MapGet("/", ListAsync).WithName("ListMyStories");
        me.MapPost("/", CreateAsync).WithName("CreateMyStory").ProducesProblem(StatusCodes.Status409Conflict);
        me.MapGet("/{id:long}", GetAsync).WithName("GetMyStory").ProducesProblem(StatusCodes.Status404NotFound);
        me.MapPut("/{id:long}", UpdateTextAsync).WithName("UpdateMyStory").ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        me.MapDelete("/{id:long}", DeleteAsync).WithName("DeleteMyStory").ProducesProblem(StatusCodes.Status404NotFound);
        me.MapPost("/{id:long}/withdraw", WithdrawAsync).WithName("WithdrawMyStory").ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        me.MapPost("/{id:long}/items", AddItemAsync).WithName("AddMyStoryItem").ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        me.MapPut("/{id:long}/items/{itemId:long}", SetCaptionAsync).WithName("SetMyStoryItemCaption").ProducesProblem(StatusCodes.Status404NotFound);
        me.MapDelete("/{id:long}/items/{itemId:long}", RemoveItemAsync).WithName("RemoveMyStoryItem").ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        me.MapPut("/{id:long}/order", ReorderAsync).WithName("ReorderMyStory").ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        me.MapPut("/{id:long}/cover", SetCoverAsync).WithName("SetMyStoryCover").ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        me.MapPost("/{id:long}/subjects", AddSubjectAsync).WithName("AddMyStorySubject").ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        me.MapPut("/{id:long}/subjects/{subjectId:long}", PlaceSubjectAsync).WithName("PlaceMyStorySubject").ProducesProblem(StatusCodes.Status404NotFound);
        me.MapDelete("/{id:long}/subjects/{subjectId:long}", RemoveSubjectAsync).WithName("RemoveMyStorySubject").ProducesProblem(StatusCodes.Status404NotFound);

        app.MapPost("/api/me/media/{id:long}/share", ShareAsync).WithName("ShareMyMedia").WithTags("Me").RequireAuthorization()
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }

    private static async Task<Ok<PagedResponse<MyStoryDto>>> ListAsync(
        int? offset, int? limit, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorQueryService query, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        var (skip, take) = Pagination.Normalize(offset, limit);
        return TypedResults.Ok(await query.ListAsync(profile.Id, skip, take, cancellationToken));
    }

    private static async Task<Results<Created<StoryEditorDto>, ProblemHttpResult>> CreateAsync(
        StoryTextRequest request, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        var result = await editor.CreateAsync(profile.Id, request, cancellationToken);
        return result.Error is null ? TypedResults.Created($"/api/me/stories/{result.Editor!.Id}", result.Editor) : ProblemFor(result.Error);
    }

    private static async Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> GetAsync(
        long id, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorQueryService query, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        var story = await query.GetAsync(id, profile.Id, cancellationToken);
        return story is null ? ProblemFor(StoryEditorCodes.StoryNotFound) : TypedResults.Ok(story);
    }

    private static Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> UpdateTextAsync(
        long id, StoryTextRequest request, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken) =>
        EditAsync(user, profiles, profileId => editor.UpdateTextAsync(id, profileId, request, cancellationToken), cancellationToken);

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        long id, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        return await editor.DeleteAsync(id, profile.Id, cancellationToken) ? TypedResults.NoContent() : ProblemFor(StoryEditorCodes.StoryNotFound);
    }

    private static async Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> WithdrawAsync(
        long id, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        return ToResult(await editor.WithdrawAsync(id, profile.Id, cancellationToken));
    }

    private static Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> AddItemAsync(
        long id, AddStoryItemRequest request, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken) =>
        EditAsync(user, profiles, profileId => editor.AddItemAsync(id, profileId, request, cancellationToken), cancellationToken);

    private static Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> SetCaptionAsync(
        long id, long itemId, StoryCaptionRequest request, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken) =>
        EditAsync(user, profiles, profileId => editor.SetCaptionAsync(id, profileId, itemId, request.Caption, cancellationToken), cancellationToken);

    private static Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> RemoveItemAsync(
        long id, long itemId, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken) =>
        EditAsync(user, profiles, profileId => editor.RemoveItemAsync(id, profileId, itemId, cancellationToken), cancellationToken);

    private static Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> ReorderAsync(
        long id, StoryOrderRequest request, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken) =>
        EditAsync(user, profiles, profileId => editor.ReorderAsync(id, profileId, request, cancellationToken), cancellationToken);

    private static Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> SetCoverAsync(
        long id, StoryCoverRequest request, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken) =>
        EditAsync(user, profiles, profileId => editor.SetCoverAsync(id, profileId, request.ItemId, cancellationToken), cancellationToken);

    private static Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> AddSubjectAsync(
        long id, AddStorySubjectRequest request, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken) =>
        EditAsync(user, profiles, profileId => editor.AddSubjectAsync(id, profileId, request, cancellationToken), cancellationToken);

    private static Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> PlaceSubjectAsync(
        long id, long subjectId, StorySubjectPlacementRequest request, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken) =>
        EditAsync(user, profiles, profileId => editor.PlaceSubjectAsync(id, profileId, subjectId, request.Positioned, cancellationToken), cancellationToken);

    private static Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> RemoveSubjectAsync(
        long id, long subjectId, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken) =>
        EditAsync(user, profiles, profileId => editor.RemoveSubjectAsync(id, profileId, subjectId, cancellationToken), cancellationToken);

    private static async Task<Results<Created<StoryDetailDto>, ProblemHttpResult>> ShareAsync(
        long id, PublishStoryRequest request, ClaimsPrincipal user, CurrentProfileService profiles, StoryEditorService editor, CancellationToken cancellationToken)
    {
        if (!UploadsEndpoints.TryValidatePublish(request, out var title, out var description, out var topic, out _))
        {
            return ProblemFor(StoryEditorCodes.TextInvalid);
        }

        var profile = await profiles.ResolveAsync(user, cancellationToken);
        var (story, error) = await editor.QuickShareAsync(id, profile.Id, title, description, topic, cancellationToken);
        return error is null ? TypedResults.Created($"/api/stories/{story!.Slug}", story) : ProblemFor(error);
    }

    private static async Task<Results<Ok<StoryEditorDto>, ProblemHttpResult>> EditAsync(
        ClaimsPrincipal user, CurrentProfileService profiles, Func<long, Task<StoryEditResult>> edit, CancellationToken cancellationToken)
    {
        var profile = await profiles.ResolveAsync(user, cancellationToken);
        return ToResult(await edit(profile.Id));
    }

    private static Results<Ok<StoryEditorDto>, ProblemHttpResult> ToResult(StoryEditResult result) =>
        result.Error is null ? TypedResults.Ok(result.Editor!) : ProblemFor(result.Error);

    private static ProblemHttpResult ProblemFor(string code) => code switch
    {
        StoryEditorCodes.StoryNotFound => ApiProblems.WithCode(StatusCodes.Status404NotFound, "Story unavailable", "You have no such story.", code),
        StoryEditorCodes.MediaNotFound => ApiProblems.WithCode(StatusCodes.Status404NotFound, "File unavailable", "This file does not exist in your media library.", code),
        StoryEditorCodes.ItemNotFound => ApiProblems.WithCode(StatusCodes.Status404NotFound, "Item unavailable", "The story has no such item.", code),
        StoryEditorCodes.SubjectNotFound => ApiProblems.WithCode(StatusCodes.Status404NotFound, "Subject unavailable", "This subject does not exist or is not available to you.", code),
        StoryEditorCodes.MediaNotReady => ApiProblems.WithCode(StatusCodes.Status409Conflict, "Video not ready", "The video is still processing and can be used when it is ready.", code),
        StoryEditorCodes.MediaAlreadyShared => ApiProblems.WithCode(StatusCodes.Status409Conflict, "Already shared", "This file was shared to the gallery already. Add it to a story instead.", code),
        StoryEditorCodes.ItemExists => ApiProblems.WithCode(StatusCodes.Status409Conflict, "Item exists", "The story shows this file already.", code),
        StoryEditorCodes.ItemLimit => ApiProblems.WithCode(StatusCodes.Status409Conflict, "Story is full", $"A story has at most {StoryEditorService.MaxItems} items.", code),
        StoryEditorCodes.SubjectExists => ApiProblems.WithCode(StatusCodes.Status409Conflict, "Subject exists", "The story points to this subject already.", code),
        StoryEditorCodes.SubjectLimit => ApiProblems.WithCode(StatusCodes.Status409Conflict, "Too many subjects", $"A story has at most {StoryEditorService.MaxSubjects} subjects.", code),
        StoryEditorCodes.NeedsItem => ApiProblems.WithCode(StatusCodes.Status409Conflict, "Story without items", "A published story keeps at least one item. Withdraw or delete the story instead.", code),
        StoryEditorCodes.NotPublished => ApiProblems.WithCode(StatusCodes.Status409Conflict, "Story not published", "Only a published story can be withdrawn.", code),
        UploadsEndpoints.DraftLimitReachedCode => UploadsEndpoints.DraftLimitProblem(),
        StoryEditorCodes.OrderInvalid => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Invalid order", "List every item and every card of the story exactly once.", code),
        StoryEditorCodes.CoverInvalid => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Invalid cover", "The cover must be an item of the story.", code),
        StoryEditorCodes.CaptionInvalid => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Invalid caption", $"A caption has at most {StoryEditorService.MaxCaptionLength} characters.", code),
        _ => ApiProblems.WithCode(StatusCodes.Status400BadRequest, "Invalid story text",
            "Send a title of up to 200 characters, a description of up to 5000 characters and a topic of cichlids, tanks or offtopic.", StoryEditorCodes.TextInvalid),
    };
}
