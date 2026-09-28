using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Features.Me;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Cichlids.Api.Tests.Features.Me;

[Collection(ApiCollection.Name)]
public class MeEndpointTests(ApiFixture fixture)
{
    [Fact]
    public async Task Get_WithoutTokenReturnsUnauthorized()
    {
        var response = await fixture.Client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_WithMalformedTokenReturnsUnauthorized()
    {
        var response = await SendAsync("not-a-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_CreatesProfileWithOidcIdentityAndSecondCallFindsIt()
    {
        var subject = Guid.NewGuid().ToString();
        var token = TestTokens.Create(subject, "fresh-oidc-user", name: "Fresh User");

        var first = await ReadMeAsync(await SendAsync(token));
        Assert.Equal("fresh-oidc-user", first.Profile.Username);
        Assert.Equal("Fresh User", first.Profile.DisplayName);
        Assert.Empty(first.Roles);

        await using (var db = fixture.CreateDbContext())
        {
            var identity = await db.ProfileIdentities
                .SingleAsync(i => i.Provider == "oidc" && i.Subject == subject);
            Assert.Equal(first.Profile.Id, identity.ProfileId);

            var profile = await db.Profiles.SingleAsync(p => p.Id == first.Profile.Id);
            Assert.Equal(ProfileKind.Member, profile.Kind);
        }

        var second = await ReadMeAsync(await SendAsync(token));
        Assert.Equal(first.Profile.Id, second.Profile.Id);
    }

    [Fact]
    public async Task Get_SuffixesUsernameWhenPreferredUsernameIsTaken()
    {
        await using (var db = fixture.CreateDbContext())
        {
            if (!await db.Profiles.AnyAsync(p => p.Username == "collision-taken"))
            {
                db.Profiles.Add(new Profile
                {
                    Username = "collision-taken", Kind = ProfileKind.Member, CreatedAt = DateTimeOffset.UtcNow,
                });
                await db.SaveChangesAsync();
            }
        }

        var token = TestTokens.Create(Guid.NewGuid().ToString(), "collision-taken");

        var me = await ReadMeAsync(await SendAsync(token));
        Assert.Equal("collision-taken-2", me.Profile.Username);
    }

    [Theory]
    [InlineData("first.login@example.test", "First Login")]
    [InlineData("", "First Login")]
    [InlineData("   ", "First Login")]
    public async Task Get_GeneratesTheHandleWhenThePreferredUsernameIsBlankOrAnEmailAddress(string preferredUsername, string name)
    {
        var subject = Guid.NewGuid().ToString();
        var token = TestTokens.Create(subject, preferredUsername, name: name);

        var me = await ReadMeAsync(await SendAsync(token));

        Assert.Equal(Names().Generate(GeneratedNames.HandleInput(subject)), me.Profile.Username);
        Assert.Equal(name, me.Profile.DisplayName);
    }

    [Theory]
    [InlineData("mail-as-name", "first.login@example.test")]
    [InlineData("mail-in-name", "First Login <first.login@example.test>")]
    public async Task Get_UsesTheHandleAsDisplayNameWhenTheNameClaimHoldsAnEmailAddress(string preferredUsername, string name)
    {
        var token = TestTokens.Create(Guid.NewGuid().ToString(), preferredUsername, name: name);

        var me = await ReadMeAsync(await SendAsync(token));

        Assert.Equal(preferredUsername, me.Profile.Username);
        Assert.Equal(preferredUsername, me.Profile.DisplayName);
    }

    [Fact]
    public async Task Get_GeneratesTheHandleAndDisplayNameWhenBothClaimsHoldAnEmailAddress()
    {
        var subject = Guid.NewGuid().ToString();
        var token = TestTokens.Create(subject, "both@example.test", name: "both@example.test", email: "both@example.test");

        var me = await ReadMeAsync(await SendAsync(token));

        var handle = Names().Generate(GeneratedNames.HandleInput(subject));
        Assert.Equal(handle, me.Profile.Username);
        Assert.Equal(handle, me.Profile.DisplayName);
    }

    [Fact]
    public async Task Get_TakesTheNextGeneratedCandidateWhenTheFirstIsTaken()
    {
        var subject = Guid.NewGuid().ToString();
        var candidates = Names().Candidates(GeneratedNames.HandleInput(subject)).Take(2).ToList();
        await using (var db = fixture.CreateDbContext())
        {
            db.Profiles.Add(new Profile { Username = candidates[0], Kind = ProfileKind.Member, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var me = await ReadMeAsync(await SendAsync(TestTokens.Create(subject, "taken@example.test")));

        Assert.Equal(candidates[1], me.Profile.Username);
    }

    [Fact]
    public async Task Get_LinksMigratedProfileByVerifiedEmailInsteadOfCreatingANewOne()
    {
        var migratedProfileId = await SeedMigratedProfileAsync("migrated-legacy-user", "migrated@example.test");

        var subject = Guid.NewGuid().ToString();
        var token = TestTokens.Create(
            subject, "some-new-keycloak-name", email: "Migrated@Example.test", emailVerified: true);

        var me = await ReadMeAsync(await SendAsync(token));
        Assert.Equal(migratedProfileId, me.Profile.Id);
        Assert.Equal("migrated-legacy-user", me.Profile.Username);

        await using (var db = fixture.CreateDbContext())
        {
            var oidcIdentity = await db.ProfileIdentities
                .SingleAsync(i => i.Provider == "oidc" && i.Subject == subject);
            Assert.Equal(migratedProfileId, oidcIdentity.ProfileId);
            Assert.False(await db.Profiles.AnyAsync(p => p.Username == "some-new-keycloak-name"));
        }
    }

    [Theory]
    [InlineData("unverified-legacy-user", "unverified@example.test", false)]
    [InlineData("unflagged-legacy-user", "unflagged@example.test", null)]
    public async Task Get_CreatesAFreshProfileWhenTheEmailIsNotVerified(string legacyUsername, string email, bool? emailVerified)
    {
        var migratedProfileId = await SeedMigratedProfileAsync(legacyUsername, email);

        var subject = Guid.NewGuid().ToString();
        var token = TestTokens.Create(subject, $"{legacyUsername}-login", email: email, emailVerified: emailVerified);

        var me = await ReadMeAsync(await SendAsync(token));
        Assert.NotEqual(migratedProfileId, me.Profile.Id);
        Assert.Equal($"{legacyUsername}-login", me.Profile.Username);

        await using var db = fixture.CreateDbContext();
        Assert.False(await db.ProfileIdentities.AnyAsync(i => i.ProfileId == migratedProfileId && i.Provider == "oidc"));
    }

    [Fact]
    public async Task Get_MapsModeratorRealmRoleIntoRoles()
    {
        var token = TestTokens.Create(
            Guid.NewGuid().ToString(), "role-mapped-moderator", roles: ["moderator"]);

        var me = await ReadMeAsync(await SendAsync(token));
        Assert.Contains("moderator", me.Roles);
    }

    private GeneratedNames Names() => fixture.Services.GetRequiredService<GeneratedNames>();

    private async Task<long> SeedMigratedProfileAsync(string username, string email)
    {
        await using var db = fixture.CreateDbContext();
        var migrated = new Profile
        {
            Username = username, DisplayName = username,
            Kind = ProfileKind.Member, CreatedAt = DateTimeOffset.UtcNow.AddYears(-10),
        };
        db.Profiles.Add(migrated);
        await db.SaveChangesAsync();

        db.ProfileIdentities.Add(new ProfileIdentity
        {
            ProfileId = migrated.Id, Provider = "email", Subject = email,
            CreatedAt = DateTimeOffset.UtcNow.AddYears(-10),
        });
        await db.SaveChangesAsync();
        return migrated.Id;
    }

    private async Task<HttpResponseMessage> SendAsync(string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }

    private static async Task<MeDto> ReadMeAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonSerializer.Deserialize<MeDto>(
            await response.Content.ReadAsStringAsync(), TestJson.Options)!;
    }
}
