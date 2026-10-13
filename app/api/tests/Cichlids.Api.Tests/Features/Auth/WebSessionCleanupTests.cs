using System.Security.Cryptography;
using Cichlids.Api.Features.Auth.WebSessions;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Tests.Features.Auth;

/// <summary>
/// The cleanup of web sessions that cannot be used any more: sessions unused for longer than the
/// retention are deleted after their offline session at Keycloak is revoked; sessions in use stay.
/// </summary>
[Collection(WebSessionCollection.Name)]
[Trait("Story", "task-3.41")]
public sealed class WebSessionCleanupTests(WebSessionFixture fixture)
{
    [Fact]
    public async Task A_session_unused_for_longer_than_the_retention_is_revoked_and_deleted()
    {
        var stale = await InsertSessionAsync(DateTimeOffset.UtcNow.AddDays(-401));

        var deleted = await RunCleanupAsync();

        Assert.True(deleted >= 1);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.WebSessions.AnyAsync(s => s.Subject == stale.Subject));
        Assert.Contains(fixture.Keycloak.LogoutRequests, form => form.GetValueOrDefault("refresh_token") == stale.RefreshToken);
    }

    [Fact]
    public async Task A_session_used_within_the_retention_stays()
    {
        var active = await InsertSessionAsync(DateTimeOffset.UtcNow.AddDays(-399));

        await RunCleanupAsync();

        await using var db = fixture.CreateDbContext();
        Assert.True(await db.WebSessions.AnyAsync(s => s.Subject == active.Subject));
        Assert.DoesNotContain(fixture.Keycloak.LogoutRequests, form => form.GetValueOrDefault("refresh_token") == active.RefreshToken);
    }

    [Fact]
    public async Task A_session_whose_revocation_got_no_answer_stays_until_the_next_pass()
    {
        var stale = await InsertSessionAsync(DateTimeOffset.UtcNow.AddDays(-500));
        fixture.Keycloak.BeUnreachableForLogoutOf(stale.RefreshToken);

        await RunCleanupAsync();

        await using (var db = fixture.CreateDbContext())
        {
            Assert.True(await db.WebSessions.AnyAsync(s => s.Subject == stale.Subject));
        }

        fixture.Keycloak.BeReachableForLogoutOf(stale.RefreshToken);
        await RunCleanupAsync();

        await using var after = fixture.CreateDbContext();
        Assert.False(await after.WebSessions.AnyAsync(s => s.Subject == stale.Subject));
    }

    [Fact]
    public async Task Passes_running_at_the_same_time_delete_a_session_once()
    {
        var stale = await InsertSessionAsync(DateTimeOffset.UtcNow.AddDays(-450));

        var passes = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => RunCleanupAsync()));

        Assert.True(passes.Sum() >= 1);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.WebSessions.AnyAsync(s => s.Subject == stale.Subject));
        Assert.Equal(0, await RunCleanupAsync());
    }

    [Fact]
    public async Task A_pass_deletes_at_most_one_batch()
    {
        var first = await InsertSessionAsync(DateTimeOffset.UtcNow.AddDays(-401));
        var second = await InsertSessionAsync(DateTimeOffset.UtcNow.AddDays(-402));

        using var scope = fixture.Services.CreateScope();
        var service = new WebSessionCleanupService(
            scope.ServiceProvider.GetRequiredService<CichlidsDbContext>(),
            scope.ServiceProvider.GetRequiredService<KeycloakOidcClient>(),
            Options.Create(new WebSessionCleanupOptions { BatchSize = 1 }),
            TimeProvider.System);

        var deleted = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        await using var db = fixture.CreateDbContext();
        var remaining = await db.WebSessions.CountAsync(s => s.Subject == first.Subject || s.Subject == second.Subject);
        Assert.Equal(1, remaining);
    }

    [Fact]
    public void The_defaults_expire_a_session_with_its_cookie_and_run_once_a_day()
    {
        var options = new WebSessionCleanupOptions();

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromDays(400), options.Retention);
        Assert.Equal(TimeSpan.FromDays(1), options.Interval);
        Assert.Equal(100, options.BatchSize);
    }

    private async Task<int> RunCleanupAsync()
    {
        using var scope = fixture.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<WebSessionCleanupService>().RunOnceAsync(CancellationToken.None);
    }

    private async Task<WebSession> InsertSessionAsync(DateTimeOffset lastSeenAt)
    {
        var session = new WebSession
        {
            SessionIdHash = SHA256.HashData(Guid.NewGuid().ToByteArray()),
            Subject = Guid.NewGuid().ToString(),
            AccessToken = "access",
            AccessTokenExpiresAt = lastSeenAt.AddMinutes(5),
            RefreshToken = $"offline-{Guid.NewGuid():N}",
            CsrfToken = "csrf",
            CreatedAt = lastSeenAt,
            LastSeenAt = lastSeenAt,
        };
        await using var db = fixture.CreateDbContext();
        db.WebSessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }
}
