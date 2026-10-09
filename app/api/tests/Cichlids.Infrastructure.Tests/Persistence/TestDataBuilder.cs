using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence;

namespace Cichlids.Infrastructure.Tests.Persistence;

/// <summary>
/// Seeds the minimal graph of rows a constraint or roundtrip test needs, with a fresh random
/// suffix on every unique value so tests can share the same migrated database without
/// colliding on unique constraints.
/// </summary>
internal static class TestDataBuilder
{
    public static async Task<Profile> CreateProfileAsync(CichlidsDbContext context)
    {
        var profile = new Profile
        {
            Username = $"member-{Guid.NewGuid():N}",
            Kind = ProfileKind.Member,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        context.Profiles.Add(profile);
        await context.SaveChangesAsync();
        return profile;
    }

    public static async Task<Tank> CreateTankAsync(CichlidsDbContext context, long profileId)
    {
        var tank = new Tank
        {
            ProfileId = profileId,
            Title = $"Tank {Guid.NewGuid():N}",
            State = TankState.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        context.Tanks.Add(tank);
        await context.SaveChangesAsync();
        return tank;
    }

    public static async Task<Post> CreatePostAsync(CichlidsDbContext context, long authorProfileId)
    {
        var post = new Post
        {
            AuthorProfileId = authorProfileId,
            Kind = PostKind.Single,
            Topic = PostTopic.Cichlids,
            State = PostState.Draft,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        context.Posts.Add(post);
        await context.SaveChangesAsync();
        return post;
    }
}
