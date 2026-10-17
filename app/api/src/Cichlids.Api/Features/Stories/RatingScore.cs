using System.Linq.Expressions;
using Cichlids.Domain.Entities;

namespace Cichlids.Api.Features.Stories;

/// <summary>
/// Bayesian-average rating score used to sort pictures by rating: a story's own average is pulled
/// toward a fixed prior mean by an amount that shrinks as its rating count grows, so a handful of
/// five-star ratings cannot outrank a story with hundreds of consistently good ones.
/// </summary>
public static class RatingScore
{
    /// <summary>
    /// The number of prior "virtual" ratings a story is assumed to start with, at the prior mean.
    /// </summary>
    public const int PriorWeight = 50;

    /// <summary>
    /// The rating a story is assumed to have before any real ratings pull it away from it.
    /// </summary>
    public const double PriorMean = 4.0;

    /// <summary>
    /// The score of a story as a database expression, for ordering stories by rating.
    /// </summary>
    public static readonly Expression<Func<Story, double>> OfStory = p =>
        (double)p.RatingCount / (p.RatingCount + PriorWeight) * (p.RatingAverage ?? 0) +
        (double)PriorWeight / (p.RatingCount + PriorWeight) * PriorMean;
}
