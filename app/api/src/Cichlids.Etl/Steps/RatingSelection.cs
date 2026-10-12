namespace Cichlids.Etl.Steps;

/// <summary>
/// One legacy rating that could migrate: its legacy comment uid, the rating member's legacy user
/// id (0 for an anonymous rating), the rated target as legacy type and item, its creation time in
/// Unix seconds, and whether the member rated their own target.
/// </summary>
public readonly record struct RatingCandidate(int Uid, int Rater, int TargetType, int TargetId, long CreatedAt, bool IsSelf);

/// <summary>
/// The legacy comment uids whose rating migrates, the number of older ratings a newer rating of
/// the same member on the same target replaces, and the number of self-ratings left out.
/// </summary>
public sealed record RatingSelectionResult(IReadOnlySet<int> Winners, int Collapsed, int SelfDropped);

/// <summary>
/// Selects the legacy ratings that migrate. A member keeps one rating per target: a rating of
/// the member on their own target is dropped, and among the remaining ratings of a member on a
/// target the newest one wins, a tie going to the higher legacy uid. Anonymous ratings have no
/// member, so every one of them migrates unchanged.
/// </summary>
public static class RatingSelection
{
    public static RatingSelectionResult Select(IEnumerable<RatingCandidate> candidates)
    {
        var winners = new HashSet<int>();
        var newestByMemberAndTarget = new Dictionary<(int Rater, int TargetType, int TargetId), (long CreatedAt, int Uid)>();
        var memberRatings = 0;
        var selfDropped = 0;

        foreach (var candidate in candidates)
        {
            if (candidate.Rater == 0)
            {
                winners.Add(candidate.Uid);
                continue;
            }

            if (candidate.IsSelf)
            {
                selfDropped++;
                continue;
            }

            memberRatings++;
            var key = (candidate.Rater, candidate.TargetType, candidate.TargetId);
            var current = (candidate.CreatedAt, candidate.Uid);
            if (!newestByMemberAndTarget.TryGetValue(key, out var newest) || current.CompareTo(newest) > 0)
            {
                newestByMemberAndTarget[key] = current;
            }
        }

        foreach (var (_, uid) in newestByMemberAndTarget.Values)
        {
            winners.Add(uid);
        }

        return new RatingSelectionResult(winners, memberRatings - newestByMemberAndTarget.Count, selfDropped);
    }
}
