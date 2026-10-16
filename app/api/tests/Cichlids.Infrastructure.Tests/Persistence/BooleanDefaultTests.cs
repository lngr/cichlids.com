using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Tests.Persistence;

/// <summary>
/// Flags whose column has the database default true keep the value false of a new row.
/// </summary>
[Trait("Category", "Docker")]
[Collection(PostgresCollection.Name)]
public sealed class BooleanDefaultTests(PostgresFixture fixture)
{
    [Fact]
    public async Task A_new_profile_keeps_its_streaks_switched_off()
    {
        long id;
        await using (var context = fixture.CreateContext())
        {
            var profile = new Profile
            {
                Username = $"member-{Guid.NewGuid():N}", Kind = ProfileKind.Member, CreatedAt = DateTimeOffset.UtcNow, StreaksEnabled = false,
            };
            context.Profiles.Add(profile);
            await context.SaveChangesAsync();
            id = profile.Id;
        }

        await using var read = fixture.CreateContext();
        Assert.False(await read.Profiles.Where(p => p.Id == id).Select(p => p.StreaksEnabled).SingleAsync());
    }

    [Fact]
    public async Task A_new_collection_keeps_being_private()
    {
        long id;
        await using (var context = fixture.CreateContext())
        {
            var profile = await TestDataBuilder.CreateProfileAsync(context);
            var collection = new Collection { ProfileId = profile.Id, Title = "Private", IsPublic = false, CreatedAt = DateTimeOffset.UtcNow };
            context.Collections.Add(collection);
            await context.SaveChangesAsync();
            id = collection.Id;
        }

        await using var read = fixture.CreateContext();
        Assert.False(await read.Collections.Where(c => c.Id == id).Select(c => c.IsPublic).SingleAsync());
    }

    [Fact]
    public async Task A_new_webhook_subscription_keeps_being_inactive()
    {
        long id;
        await using (var context = fixture.CreateContext())
        {
            var subscription = new WebhookSubscription
            {
                Url = "https://consumer.example/hook", Secret = "secret", EventTypes = ["tank.created"], Active = false, CreatedAt = DateTimeOffset.UtcNow,
            };
            context.WebhookSubscriptions.Add(subscription);
            await context.SaveChangesAsync();
            id = subscription.Id;
        }

        await using var read = fixture.CreateContext();
        Assert.False(await read.WebhookSubscriptions.Where(s => s.Id == id).Select(s => s.Active).SingleAsync());
    }
}
