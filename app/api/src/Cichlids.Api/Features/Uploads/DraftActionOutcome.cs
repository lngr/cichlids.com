namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// Outcome of a publish or discard request on a draft: it succeeded, the caller has no such post,
/// or the post is not a draft.
/// </summary>
public enum DraftActionOutcome
{
    Succeeded,
    NotFound,
    NotDraft,
}
