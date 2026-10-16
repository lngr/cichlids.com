using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Features.WebPush;
using Cichlids.Domain.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;

namespace Cichlids.Api.Tests.Features.WebPush;

/// <summary>
/// The pass that sends the care reminders as Web Push messages: one message per rule and due date
/// and subscription, however often or from however many instances it runs, encrypted for the
/// browser, without content beyond the tank title, the kind and the address to open. Subscriptions
/// the push service reports as gone are deleted and other failures back off.
/// </summary>
[Collection(ApiCollection.Name)]
[Trait("Story", "task-5.12")]
public sealed class WebPushReminderServiceTests(ApiFixture fixture) : IAsyncLifetime
{
    // 2026-06-16 07:30 UTC is 09:30 in Berlin, 03:30 in New York and 19:30 in Auckland.
    private static readonly DateTimeOffset DueMorning = DateTimeOffset.Parse("2026-06-16T07:30:00Z");
    private static readonly DateTimeOffset LastWaterChange = DateTimeOffset.Parse("2026-06-09T08:00:00Z");

    private readonly FakePushService _push = new();
    private readonly List<long> _tanks = [];
    private readonly List<string> _endpoints = [];
    private readonly List<FakeBrowser> _browsers = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        var rows = await db.WebPushSubscriptions.Where(s => _endpoints.Contains(s.Endpoint)).ToListAsync();
        var ids = rows.Select(r => r.Id.ToString()).ToList();
        await db.OutboxEvents.Where(e => e.AggregateType == "web_push_subscription" && ids.Contains(e.AggregateId)).ExecuteDeleteAsync();
        db.WebPushSubscriptions.RemoveRange(rows);
        await db.SaveChangesAsync();
        await RemoveTanksAsync(db, _tanks);
        foreach (var browser in _browsers) browser.Dispose();
        _push.Dispose();
    }

    private WebPushSettings Settings => fixture.Services.GetRequiredService<WebPushSettings>();

    private WebPushReminderService Service(Cichlids.Infrastructure.Persistence.CichlidsDbContext db, FakeTimeProvider clock) =>
        new(db, new CarePlanService(db, clock), new WebPushTransport(new HttpClient(_push), Settings), Settings, clock,
            NullLogger<WebPushReminderService>.Instance);

    private async Task<WebPushPass> RunAsync(FakeTimeProvider clock)
    {
        await using var db = fixture.CreateDbContext();
        return await Service(db, clock).RunOnceAsync(CancellationToken.None);
    }

    private static FakeTimeProvider ClockAt(DateTimeOffset instant)
    {
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(instant);
        return clock;
    }

    private async Task<(string Token, long Tank)> TankAsync(DateTimeOffset? lastWaterChange = null, string title = "Reminder tank")
    {
        var token = NewMemberToken();
        using var created = await SendAsync(fixture.Client, HttpMethod.Post, "/api/tanks", token, new { title });
        var tank = (await BodyAsync(created)).GetProperty("id").GetInt64();
        _tanks.Add(tank);
        if (lastWaterChange is { } at) await LogAsync(fixture.Client, token, tank, new { kind = "water_change", occurredAt = at });
        return (token, tank);
    }

    private async Task<FakeBrowser> SubscribeAsync(string token, string timeZone = "Europe/Berlin")
    {
        var browser = new FakeBrowser(FakeBrowser.NewEndpoint());
        _browsers.Add(browser);
        _endpoints.Add(browser.Endpoint);
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, "/api/me/web-push/subscriptions", token, browser.SubscribeBody(timeZone));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return browser;
    }

    [Fact]
    public async Task GivenAWaterChangeDueToday_WhenTheDayReachesNineAtTheBrowsersClock_ThenOneEncryptedMessageWithOnlyTheReminderIsSent()
    {
        var (token, tank) = await TankAsync(LastWaterChange, "Malawi 240");
        var browser = await SubscribeAsync(token);

        var pass = await RunAsync(ClockAt(DueMorning));

        Assert.Equal(1, pass.Sent);
        var request = Assert.Single(_push.RequestsFor(browser.Endpoint));
        Assert.Equal("aes128gcm", request.Headers["Content-Encoding"]);
        var payload = JsonDocument.Parse(browser.Decrypt(request.Body)).RootElement;
        Assert.Equal(["dueDate", "kind", "tankId", "tankTitle", "url"], payload.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(tank, payload.GetProperty("tankId").GetInt64());
        Assert.Equal("Malawi 240", payload.GetProperty("tankTitle").GetString());
        Assert.Equal("water_change", payload.GetProperty("kind").GetString());
        Assert.Equal("2026-06-16", payload.GetProperty("dueDate").GetString());
        Assert.Equal($"/care?tank={tank}", payload.GetProperty("url").GetString());
    }

    [Fact]
    public async Task GivenAMessage_WhenReadingItsAuthorization_ThenItIsASignedVapidTokenForThePushServiceOrigin()
    {
        var (token, _) = await TankAsync(LastWaterChange);
        var browser = await SubscribeAsync(token);

        await RunAsync(ClockAt(DueMorning));

        var request = Assert.Single(_push.RequestsFor(browser.Endpoint));
        var authorization = request.Headers["Authorization"];
        Assert.StartsWith("vapid ", authorization);
        var parts = authorization["vapid ".Length..].Split(", ").ToDictionary(p => p[..p.IndexOf('=')], p => p[(p.IndexOf('=') + 1)..]);
        Assert.Equal(ApiFixture.VapidKeyPair.PublicKey, parts["k"]);
        var jwt = parts["t"].Split('.');
        var claims = JsonDocument.Parse(WebEncoders.Base64UrlDecode(jwt[1])).RootElement;
        Assert.Equal("https://push.example.test", claims.GetProperty("aud").GetString());
        Assert.Equal("mailto:operator@example.test", claims.GetProperty("sub").GetString());
        Assert.InRange(claims.GetProperty("exp").GetInt64(), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds());
        var publicKey = WebEncoders.Base64UrlDecode(ApiFixture.VapidKeyPair.PublicKey);
        using var verifier = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = publicKey[1..33], Y = publicKey[33..65] } });
        Assert.True(verifier.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), WebEncoders.Base64UrlDecode(jwt[2]), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public async Task GivenAMessageWasSent_WhenThePassRunsAgainOrOnTwoInstancesAtOnce_ThenNoSecondMessageIsSent()
    {
        var (token, _) = await TankAsync(LastWaterChange);
        var browser = await SubscribeAsync(token);
        var clock = ClockAt(DueMorning);
        await RunAsync(clock);

        clock.Advance(TimeSpan.FromMinutes(5));
        await RunAsync(clock);
        clock.Advance(TimeSpan.FromHours(3));
        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();
        await Task.WhenAll(Service(first, clock).RunOnceAsync(CancellationToken.None), Service(second, clock).RunOnceAsync(CancellationToken.None));

        Assert.Single(_push.RequestsFor(browser.Endpoint));
    }

    [Fact]
    public async Task GivenTwoInstancesAndAFreshReminder_WhenTheyRunAtOnce_ThenExactlyOneMessageIsSent()
    {
        var (token, _) = await TankAsync(LastWaterChange);
        var browser = await SubscribeAsync(token);
        var clock = ClockAt(DueMorning);
        await using var first = fixture.CreateDbContext();
        await using var second = fixture.CreateDbContext();

        await Task.WhenAll(Service(first, clock).RunOnceAsync(CancellationToken.None), Service(second, clock).RunOnceAsync(CancellationToken.None));

        Assert.Single(_push.RequestsFor(browser.Endpoint));
    }

    [Fact]
    public async Task GivenOneMemberWithBrowsersInTwoTimeZones_WhenItIsMorningInOnlyOne_ThenOnlyThatBrowserIsNotified()
    {
        var (token, _) = await TankAsync(LastWaterChange);
        var berlin = await SubscribeAsync(token, "Europe/Berlin");
        var newYork = await SubscribeAsync(token, "America/New_York");

        await RunAsync(ClockAt(DueMorning));

        Assert.Single(_push.RequestsFor(berlin.Endpoint));
        Assert.Empty(_push.RequestsFor(newYork.Endpoint));
        var clock = ClockAt(DueMorning.AddHours(6));
        await RunAsync(clock);
        Assert.Single(_push.RequestsFor(berlin.Endpoint));
        Assert.Single(_push.RequestsFor(newYork.Endpoint));
    }

    [Fact]
    public async Task GivenBeforeNineAtTheBrowsersClock_WhenThePassRuns_ThenNothingIsSent()
    {
        var (token, _) = await TankAsync(LastWaterChange);
        var browser = await SubscribeAsync(token);

        await RunAsync(ClockAt(DateTimeOffset.Parse("2026-06-16T06:59:00Z")));

        Assert.Empty(_push.RequestsFor(browser.Endpoint));
    }

    [Fact]
    public async Task GivenOverdueNeverLoggedOrSwitchedOffCare_WhenThePassRuns_ThenNothingIsSent()
    {
        var (overdueToken, _) = await TankAsync(LastWaterChange.AddDays(-3));
        var (neverToken, _) = await TankAsync();
        var (offToken, offTank) = await TankAsync(LastWaterChange);
        using var off = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/tanks/{offTank}/care/plan?date=2026-06-16", offToken, new { remindersEnabled = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        var endpoints = new[] { await SubscribeAsync(overdueToken), await SubscribeAsync(neverToken), await SubscribeAsync(offToken) };

        var pass = await RunAsync(ClockAt(DueMorning));

        Assert.All(endpoints, browser => Assert.Empty(_push.RequestsFor(browser.Endpoint)));
        Assert.Equal(0, pass.Sent);
    }

    [Fact]
    public async Task GivenAReminderWasSnoozed_WhenTheNextDayReachesNine_ThenItIsSentOnceMore()
    {
        var (token, tank) = await TankAsync(LastWaterChange);
        var browser = await SubscribeAsync(token);
        var clock = ClockAt(DueMorning);
        await RunAsync(clock);
        await using (var db = fixture.CreateDbContext())
        {
            db.CareReminderSnoozes.Add(new CareReminderSnooze
            {
                ProfileId = await db.Tanks.Where(t => t.Id == tank).Select(t => t.ProfileId).SingleAsync(), TankId = tank,
                Kind = Cichlids.Domain.Enums.CareKind.WaterChange, DueDate = new DateOnly(2026, 6, 16), UntilDate = new DateOnly(2026, 6, 17),
                CreatedAt = DueMorning,
            });
            await db.SaveChangesAsync();
        }

        clock.SetUtcNow(DateTimeOffset.Parse("2026-06-17T06:59:00Z"));
        await RunAsync(clock);
        Assert.Single(_push.RequestsFor(browser.Endpoint));
        clock.SetUtcNow(DateTimeOffset.Parse("2026-06-17T07:05:00Z"));
        await RunAsync(clock);
        await RunAsync(clock);

        var requests = _push.RequestsFor(browser.Endpoint);
        Assert.Equal(2, requests.Count);
        Assert.Equal("2026-06-16", JsonDocument.Parse(browser.Decrypt(requests[1].Body)).RootElement.GetProperty("dueDate").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    public async Task GivenThePushServiceReportsTheSubscriptionGone_WhenSending_ThenTheSubscriptionIsDeletedWithOneEvent(HttpStatusCode status)
    {
        var (token, _) = await TankAsync(LastWaterChange);
        var browser = await SubscribeAsync(token);
        _push.Answer(browser.Endpoint, status);

        var pass = await RunAsync(ClockAt(DueMorning));

        Assert.Equal(1, pass.Removed);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.WebPushSubscriptions.AnyAsync(s => s.Endpoint == browser.Endpoint));
        var events = await db.OutboxEvents.AsNoTracking().Where(e => e.EventType == "web_push.unsubscribed").ToListAsync();
        Assert.Contains(events, e => JsonDocument.Parse(e.Payload).RootElement.GetProperty("reason").GetString() == "gone");
        await RunAsync(ClockAt(DueMorning.AddMinutes(5)));
        Assert.Single(_push.RequestsFor(browser.Endpoint));
    }

    [Fact]
    public async Task GivenAServerError_WhenSending_ThenTheSubscriptionBacksOffAndTheReminderIsRetriedAfterwards()
    {
        var (token, _) = await TankAsync(LastWaterChange);
        var browser = await SubscribeAsync(token);
        _push.Answer(browser.Endpoint, HttpStatusCode.InternalServerError);
        var clock = ClockAt(DueMorning);

        var failed = await RunAsync(clock);

        Assert.Equal(1, failed.Failed);
        await using (var db = fixture.CreateDbContext())
        {
            var row = await db.WebPushSubscriptions.AsNoTracking().SingleAsync(s => s.Endpoint == browser.Endpoint);
            Assert.Equal(1, row.FailureCount);
            Assert.Equal(DueMorning.AddMinutes(5), row.NextAttemptAt);
        }

        clock.Advance(TimeSpan.FromMinutes(1));
        await RunAsync(clock);
        Assert.Single(_push.RequestsFor(browser.Endpoint));

        _push.Answer(browser.Endpoint, HttpStatusCode.Created);
        clock.Advance(TimeSpan.FromMinutes(5));
        var retried = await RunAsync(clock);

        Assert.Equal(1, retried.Sent);
        Assert.Equal(2, _push.RequestsFor(browser.Endpoint).Count);
        await using var after = fixture.CreateDbContext();
        var healed = await after.WebPushSubscriptions.AsNoTracking().SingleAsync(s => s.Endpoint == browser.Endpoint);
        Assert.Equal(0, healed.FailureCount);
        Assert.Null(healed.NextAttemptAt);
        Assert.Equal(clock.GetUtcNow(), healed.LastSuccessAt);
    }

    [Fact]
    public async Task GivenARetryAfterOfTheTooManyRequestsAnswer_WhenSending_ThenTheNextAttemptWaitsThatLong()
    {
        var (token, _) = await TankAsync(LastWaterChange);
        var browser = await SubscribeAsync(token);
        _push.Answer(browser.Endpoint, HttpStatusCode.TooManyRequests, retryAfterSeconds: 1200);

        await RunAsync(ClockAt(DueMorning));

        await using var db = fixture.CreateDbContext();
        Assert.Equal(DueMorning.AddMinutes(20), (await db.WebPushSubscriptions.AsNoTracking().SingleAsync(s => s.Endpoint == browser.Endpoint)).NextAttemptAt);
    }

    [Fact]
    public async Task GivenAnEndpointThatTheAllowlistNoLongerAdmits_WhenThePassRuns_ThenNothingIsSentToItAndTheSubscriptionIsDeleted()
    {
        var (token, _) = await TankAsync(LastWaterChange);
        var browser = await SubscribeAsync(token);
        await using (var db = fixture.CreateDbContext())
        {
            var row = await db.WebPushSubscriptions.SingleAsync(s => s.Endpoint == browser.Endpoint);
            row.Endpoint = "https://elsewhere.example.test/send/abc";
            _endpoints.Add(row.Endpoint);
            await db.SaveChangesAsync();
        }

        await RunAsync(ClockAt(DueMorning));

        Assert.Empty(_push.RequestsFor("https://elsewhere.example.test/send/abc"));
        await using var after = fixture.CreateDbContext();
        Assert.False(await after.WebPushSubscriptions.AnyAsync(s => s.Endpoint == "https://elsewhere.example.test/send/abc"));
    }
}
