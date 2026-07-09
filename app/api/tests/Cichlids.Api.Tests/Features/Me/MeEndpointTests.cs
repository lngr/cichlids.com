using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Features.Me;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Microsoft.EntityFrameworkCore;

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

    [Fact]
    public async Task Get_LinksMigratedProfileByEmailInsteadOfCreatingANewOne()
    {
        long migratedProfileId;
        await using (var db = fixture.CreateDbContext())
        {
            var migrated = new Profile
            {
                Username = "migrated-legacy-user", DisplayName = "Migrated Legacy User",
                Kind = ProfileKind.Member, CreatedAt = DateTimeOffset.UtcNow.AddYears(-10),
            };
            db.Profiles.Add(migrated);
            await db.SaveChangesAsync();

            db.ProfileIdentities.Add(new ProfileIdentity
            {
                ProfileId = migrated.Id, Provider = "email", Subject = "migrated@example.test",
                CreatedAt = DateTimeOffset.UtcNow.AddYears(-10),
            });
            await db.SaveChangesAsync();
            migratedProfileId = migrated.Id;
        }

        var subject = Guid.NewGuid().ToString();
        var token = TestTokens.Create(
            subject, "some-new-keycloak-name", email: "Migrated@Example.test");

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

    [Fact]
    public async Task Get_MapsModeratorRealmRoleIntoRoles()
    {
        var token = TestTokens.Create(
            Guid.NewGuid().ToString(), "role-mapped-moderator", roles: ["moderator"]);

        var me = await ReadMeAsync(await SendAsync(token));
        Assert.Contains("moderator", me.Roles);
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
