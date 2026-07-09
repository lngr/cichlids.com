namespace Cichlids.Api.Features.Common;

/// <summary>
/// Offset/limit paging shared by every list endpoint: limit defaults to 20 and is capped at 50,
/// offset defaults to 0 and never goes negative.
/// </summary>
public static class Pagination
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 50;

    public static (int Offset, int Limit) Normalize(int? offset, int? limit)
    {
        var normalizedOffset = offset is > 0 ? offset.Value : 0;
        var normalizedLimit = limit switch
        {
            null or <= 0 => DefaultLimit,
            > MaxLimit => MaxLimit,
            _ => limit.Value,
        };

        return (normalizedOffset, normalizedLimit);
    }
}

/// <summary>
/// Uniform list response shape: the total row count matching the filter, independent of paging,
/// alongside the page of items actually returned.
/// </summary>
public sealed record PagedResponse<T>(int Total, IReadOnlyList<T> Items);
