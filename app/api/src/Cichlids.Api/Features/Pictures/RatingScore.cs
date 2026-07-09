namespace Cichlids.Api.Features.Pictures;

/// <summary>
/// Bayesian-average rating score used to sort pictures by rating: a post's own average is pulled
/// toward a fixed prior mean by an amount that shrinks as its rating count grows, so a handful of
/// five-star ratings cannot outrank a post with hundreds of consistently good ones.
/// </summary>
public static class RatingScore
{
    /// <summary>
    /// The number of prior "virtual" ratings a post is assumed to start with, at the prior mean.
    /// </summary>
    public const int PriorWeight = 50;

    /// <summary>
    /// The rating a post is assumed to have before any real ratings pull it away from it.
    /// </summary>
    public const double PriorMean = 4.0;
}
