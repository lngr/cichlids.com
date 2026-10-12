// Story: task-3.35
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Cichlids.Api.Features.Auth;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Identity;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cichlids.Api.Tests.Features.Auth;

/// <summary>
/// First login of a new account: the profile and its oidc identity are created in one save, a
/// parallel first call resolves to the profile that won the race, and a login bound to a profile
/// without membership is refused.
/// </summary>
[Collection(ApiCollection.Name)]
public class FirstLoginTests(ApiFixture fixture)
{
    [Fact]
    public async Task ParallelFirstCallWithSameSubjectResolvesToTheWinner()
    {
        var subject = Guid.NewGuid().ToString();
        var username = $"race-{Guid.NewGuid():N}";
        var winnerId = 0L;
        var interceptor = new OneShotInterceptor(async () =>
            winnerId = await CommitProfileAsync(username, subject));

        await using var context = fixture.CreateDbContext(interceptor);
        var resolved = await NewService(context).ResolveAsync(Principal(subject, username), CancellationToken.None);

        Assert.Equal(winnerId, resolved.Id);
        await using var check = fixture.CreateDbContext();
        Assert.Equal(1, await check.ProfileIdentities.CountAsync(i => i.Subject == subject));
        Assert.Equal(1, await check.Profiles.CountAsync(p => p.Username.StartsWith(username)));
    }

    [Fact]
    public async Task ParallelFirstCallWithSameSubjectLeavesNoOrphanProfile()
    {
        var subject = Guid.NewGuid().ToString();
        var winnerName = $"winner-{Guid.NewGuid():N}";
        var losingName = $"loser-{Guid.NewGuid():N}";
        var winnerId = 0L;
        var interceptor = new OneShotInterceptor(async () =>
            winnerId = await CommitProfileAsync(winnerName, subject));

        await using var context = fixture.CreateDbContext(interceptor);
        var resolved = await NewService(context).ResolveAsync(Principal(subject, losingName), CancellationToken.None);

        Assert.Equal(winnerId, resolved.Id);
        await using var check = fixture.CreateDbContext();
        Assert.False(await check.Profiles.AnyAsync(p => p.Username == losingName));
        Assert.Equal(0, await check.Profiles.CountAsync(
            p => p.Kind == ProfileKind.Member && p.LegacyId == null && !p.Identities.Any()
                 && p.Username.StartsWith("loser-")));
    }

    [Fact]
    public async Task ParallelFirstCallWithOtherSubjectAndSameUsernameGetsSuffixedUsername()
    {
        var winnerSubject = Guid.NewGuid().ToString();
        var subject = Guid.NewGuid().ToString();
        var username = $"same-{Guid.NewGuid():N}";
        var interceptor = new OneShotInterceptor(async () => await CommitProfileAsync(username, winnerSubject));

        await using var context = fixture.CreateDbContext(interceptor);
        var resolved = await NewService(context).ResolveAsync(Principal(subject, username), CancellationToken.None);

        Assert.Equal($"{username}-2", resolved.Username);
        await using var check = fixture.CreateDbContext();
        Assert.Equal(1, await check.ProfileIdentities.CountAsync(i => i.Subject == subject));
        Assert.Equal(1, await check.ProfileIdentities.CountAsync(i => i.Subject == winnerSubject));
    }

    [Fact]
    public async Task ConcurrentFirstRequestsAllSucceedAndCreateOneProfile()
    {
        var subject = Guid.NewGuid().ToString();
        var username = $"burst-{Guid.NewGuid():N}";
        var token = TestTokens.Create(subject, username);

        var gate = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, 10)
            .Select(async i =>
            {
                await gate.Task;
                return await GetAsync(i % 2 == 0 ? "/api/me" : "/api/me/drafts", token);
            })
            .ToList();
        gate.SetResult();
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        await using var db = fixture.CreateDbContext();
        var identity = await db.ProfileIdentities.SingleAsync(i => i.Provider == "oidc" && i.Subject == subject);
        Assert.Equal(1, await db.Profiles.CountAsync(p => p.Username.StartsWith(username)));
        var me = await GetAsync("/api/me", token);
        using var document = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal(identity.ProfileId, document.RootElement.GetProperty("profile").GetProperty("id").GetInt64());
    }

    [Theory]
    [InlineData(ProfileKind.Archived)]
    [InlineData(ProfileKind.System)]
    public async Task LoginBoundToNonMemberProfileIsRefusedOnEveryEndpoint(ProfileKind kind)
    {
        var subject = Guid.NewGuid().ToString();
        var profileId = await CommitProfileAsync($"nonmember-{Guid.NewGuid():N}", subject, kind, legacyId: Random.Shared.Next(1_000_000, int.MaxValue));
        var token = TestTokens.Create(subject, "nonmember-login");

        foreach (var path in new[] { "/api/me", "/api/me/drafts" })
        {
            var response = await GetAsync(path, token);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("non_member_profile", document.RootElement.GetProperty("code").GetString());
        }

        await using var db = fixture.CreateDbContext();
        Assert.Equal(1, await db.ProfileIdentities.CountAsync(i => i.Subject == subject));
        Assert.Equal(profileId, (await db.ProfileIdentities.SingleAsync(i => i.Subject == subject)).ProfileId);
        Assert.False(await db.Profiles.AnyAsync(p => p.Username == "nonmember-login"));
    }

    [Theory]
    [InlineData(ProfileKind.Archived)]
    [InlineData(ProfileKind.System)]
    public async Task ResolveThrowsAndLogsAWarningForNonMemberProfile(ProfileKind kind)
    {
        var subject = Guid.NewGuid().ToString();
        var profileId = await CommitProfileAsync($"logged-{Guid.NewGuid():N}", subject, kind, legacyId: Random.Shared.Next(1_000_000, int.MaxValue));
        var logger = new RecordingLogger<CurrentProfileService>();

        await using var context = fixture.CreateDbContext();
        var exception = await Assert.ThrowsAsync<NonMemberLoginException>(() =>
            NewService(context, logger).ResolveAsync(Principal(subject, "someone"), CancellationToken.None));

        Assert.Equal(profileId, exception.ProfileId);
        Assert.Equal(kind, exception.Kind);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(profileId.ToString(), entry.Message);
        Assert.Contains(kind.ToString(), entry.Message);
    }

    private CurrentProfileService NewService(CichlidsDbContext context, ILogger<CurrentProfileService>? logger = null) =>
        new(context, fixture.Services.GetRequiredService<Lazy<GeneratedNames>>(), logger ?? new RecordingLogger<CurrentProfileService>());

    private static ClaimsPrincipal Principal(string subject, string username) =>
        new(new ClaimsIdentity(
            [new Claim("sub", subject), new Claim("preferred_username", username)], "test"));

    private async Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }

    private async Task<long> CommitProfileAsync(
        string username, string subject, ProfileKind kind = ProfileKind.Member, int? legacyId = null)
    {
        await using var db = fixture.CreateDbContext();
        var profile = new Profile
        {
            Username = username,
            DisplayName = username,
            Kind = kind,
            LegacyId = legacyId,
            CreatedAt = DateTimeOffset.UtcNow,
            Identities =
            [
                new ProfileIdentity { Provider = "oidc", Subject = subject, CreatedAt = DateTimeOffset.UtcNow },
            ],
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync();
        return profile.Id;
    }

    /// <summary>
    /// Runs an action once, right before the first save that inserts a profile, so a test can
    /// commit a competing profile between the service's lookup and its insert.
    /// </summary>
    private sealed class OneShotInterceptor(Func<Task> action) : SaveChangesInterceptor
    {
        private bool _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_fired && eventData.Context!.ChangeTracker.Entries<Profile>().Any(e => e.State == EntityState.Added))
            {
                _fired = true;
                await action();
            }

            return result;
        }
    }
}
