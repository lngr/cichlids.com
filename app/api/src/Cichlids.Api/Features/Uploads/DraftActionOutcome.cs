namespace Cichlids.Api.Features.Uploads;

/// <summary>
/// Outcome of a publish or discard request on a draft: it succeeded, the caller has no such story,
/// the story is not a draft, or the story has no item to publish.
/// </summary>
public enum DraftActionOutcome
{
    Succeeded,
    NotFound,
    NotDraft,
    NoItems,
}
