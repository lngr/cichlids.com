using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cichlids.Api.Features.Care;
using Cichlids.Api.Tests.Features.Uploads;
using Cichlids.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;
using static Cichlids.Api.Tests.Features.Uploads.UploadTestSupport;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(UploadsCollection.Name)]
public sealed class WaterChangeTests(UploadsFixture fixture)
{
    private static string Base(long tank) => $"/api/tanks/{tank}/water-changes";

    private async Task<JsonElement> StartAsync(string token, long tank, HttpStatusCode expected = HttpStatusCode.Created)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, Base(tank), token);
        Assert.Equal(expected, response.StatusCode);
        return await BodyAsync(response);
    }

    private async Task<HttpResponseMessage> StepAsync(string? token, long tank, long id, string step, string state) =>
        await SendAsync(fixture.Client, HttpMethod.Put, $"{Base(tank)}/{id}/steps/{step}", token, new { state });

    private async Task<HttpResponseMessage> PhotoAsync(string? token, long tank, long id, byte[] bytes, bool inApp = true)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "photo.jpg");
        content.Add(new StringContent(inApp ? "true" : "false"), "capturedInApp");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base(tank)}/{id}/photos") { Content = content };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await fixture.Client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> FinishAsync(string? token, long tank, long id, object? body = null) =>
        await SendAsync(fixture.Client, HttpMethod.Post, $"{Base(tank)}/{id}/finish", token, body ?? new { });

    // Moves the start and the step marks of a session into the past, so a test covers the minimum
    // duration without waiting.
    private async Task AgeAsync(long id, TimeSpan age)
    {
        await using var db = fixture.CreateDbContext();
        var seconds = (int)age.TotalSeconds;
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE water_change_session SET started_at = started_at - make_interval(secs => {seconds}) WHERE id = {id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE water_change_session_step SET at = at - make_interval(secs => {seconds}) WHERE session_id = {id}");
    }

    // Counts the stored originals; the variants live under their own keys.
    private async Task<int> CountKeysAsync(long profileId)
    {
        var count = 0;
        await foreach (var _ in fixture.ObjectStore.ListKeysAsync($"private/care/{profileId}/")) count++;
        return count;
    }

    [Fact]
    public async Task GivenOwnTank_WhenStartingTwice_ThenTheOpenSessionIsReturnedAndOneStartedEventExists()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var first = await StartAsync(token, tank);
        var second = await StartAsync(token, tank, HttpStatusCode.OK);
        Assert.Equal(first.GetProperty("id").GetInt64(), second.GetProperty("id").GetInt64());
        Assert.Equal(tank, first.GetProperty("tankId").GetInt64());
        Assert.Equal(600, first.GetProperty("minDurationSeconds").GetInt32());
        Assert.Equal(TimeSpan.FromHours(24), first.GetProperty("expiresAt").GetDateTimeOffset() - first.GetProperty("startedAt").GetDateTimeOffset());
        await using var db = fixture.CreateDbContext();
        var events = await EventsAsync(db, "water_change", first.GetProperty("id").GetInt64());
        Assert.Equal(["water_change.started"], events.Select(e => e.Type));
        using var current = await SendAsync(fixture.Client, HttpMethod.Get, $"{Base(tank)}/current", token);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        Assert.Equal(first.GetProperty("id").GetInt64(), (await BodyAsync(current)).GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task GivenAnotherMemberOrNoToken_WhenUsingTheSession_ThenTheApiAnswers404Or401WithCodes()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var session = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        var stranger = NewMemberToken();
        using (var start = await SendAsync(fixture.Client, HttpMethod.Post, Base(tank), stranger))
            await AssertProblemAsync(start, HttpStatusCode.NotFound, "water_change_not_found");
        using (var current = await SendAsync(fixture.Client, HttpMethod.Get, $"{Base(tank)}/current", stranger))
            await AssertProblemAsync(current, HttpStatusCode.NotFound, "water_change_not_found");
        using (var step = await StepAsync(stranger, tank, session, "heater_filter_off", "done"))
            await AssertProblemAsync(step, HttpStatusCode.NotFound, "water_change_not_found");
        using (var photo = await PhotoAsync(stranger, tank, session, Jpeg(800, 600)))
            await AssertProblemAsync(photo, HttpStatusCode.NotFound, "water_change_not_found");
        using (var finish = await FinishAsync(stranger, tank, session))
            await AssertProblemAsync(finish, HttpStatusCode.NotFound, "water_change_not_found");
        using (var cancel = await SendAsync(fixture.Client, HttpMethod.Delete, $"{Base(tank)}/{session}", stranger))
            await AssertProblemAsync(cancel, HttpStatusCode.NotFound, "water_change_not_found");
        using (var anonymous = await SendAsync(fixture.Client, HttpMethod.Post, Base(tank), null))
            await AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized, "care_auth_required");
        using var stillOpen = await SendAsync(fixture.Client, HttpMethod.Get, $"{Base(tank)}/current", token);
        Assert.Equal(HttpStatusCode.OK, stillOpen.StatusCode);
    }

    [Fact]
    public async Task GivenOpenSession_WhenMarkingSteps_ThenTheLatestMarkOfAStepCountsAndUnknownValuesAreRefused()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        using (var first = await StepAsync(token, tank, id, "heater_filter_off", "done")) Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var skip = await StepAsync(token, tank, id, "conditioner", "skipped")) Assert.Equal(HttpStatusCode.OK, skip.StatusCode);
        using (var again = await StepAsync(token, tank, id, "conditioner", "done")) Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        using var current = await SendAsync(fixture.Client, HttpMethod.Get, $"{Base(tank)}/current", token);
        var steps = (await BodyAsync(current)).GetProperty("steps").EnumerateArray().ToDictionary(s => s.GetProperty("step").GetString()!, s => s.GetProperty("state").GetString());
        Assert.Equal(2, steps.Count);
        Assert.Equal("done", steps["heater_filter_off"]);
        Assert.Equal("done", steps["conditioner"]);
        using (var badStep = await StepAsync(token, tank, id, "stir", "done")) await AssertProblemAsync(badStep, HttpStatusCode.BadRequest, "water_change_request_invalid");
        using (var badState = await StepAsync(token, tank, id, "refill_started", "later")) await AssertProblemAsync(badState, HttpStatusCode.BadRequest, "water_change_request_invalid");
    }

    [Fact]
    public async Task GivenGuidedModeOfAtLeastTenMinutes_WhenFinishingWithAnInAppPhoto_ThenTheEntryIsVerifiedWithTheDuration()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        foreach (var name in new[] { "heater_filter_off", "draining_started", "refill_started", "heater_filter_on" })
        {
            using var marked = await StepAsync(token, tank, id, name, "done");
            Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        }
        (await PhotoAsync(token, tank, id, Jpeg(800, 600))).Dispose();
        await using (var db = fixture.CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE water_change_session_step SET at = at + interval '12 minutes' WHERE session_id = {id} AND step IN ('refill_started', 'heater_filter_on')");
        await AgeAsync(id, TimeSpan.FromMinutes(14));

        using var finished = await FinishAsync(token, tank, id, new { amount = new { value = 30, unit = "percent" } });
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        var body = await BodyAsync(finished);
        var entry = body.GetProperty("entry");
        Assert.Equal("water_change", entry.GetProperty("kind").GetString());
        Assert.Equal(id, entry.GetProperty("waterChangeSessionId").GetInt64());
        Assert.InRange(entry.GetProperty("durationSeconds").GetInt32(), 14 * 60, 14 * 60 + 60);
        Assert.Equal("verified", entry.GetProperty("evidence").GetProperty("state").GetString());
        Assert.Empty(entry.GetProperty("evidence").GetProperty("missing").EnumerateArray());
        Assert.Equal(30, entry.GetProperty("amount").GetProperty("value").GetDecimal());
        Assert.Equal(1, entry.GetProperty("photos").GetArrayLength());
        var session = body.GetProperty("session");
        Assert.NotEqual(JsonValueKind.Null, session.GetProperty("finishedAt").ValueKind);
        Assert.Equal(entry.GetProperty("durationSeconds").GetInt32(), session.GetProperty("durationSeconds").GetInt32());

        using var list = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        var listed = (await BodyAsync(list)).GetProperty("items")[0];
        Assert.Equal("verified", listed.GetProperty("evidence").GetProperty("state").GetString());

        using var current = await SendAsync(fixture.Client, HttpMethod.Get, $"{Base(tank)}/current", token);
        await AssertProblemAsync(current, HttpStatusCode.NotFound, "water_change_not_found");
        using var step = await StepAsync(token, tank, id, "heater_filter_on", "done");
        await AssertProblemAsync(step, HttpStatusCode.NotFound, "water_change_not_found");
    }

    [Fact]
    public async Task GivenFinish_WhenItCommits_ThenTheEntryAndBothEventsExistTogether()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        using var finished = await FinishAsync(token, tank, id);
        var entryId = (await BodyAsync(finished)).GetProperty("entry").GetProperty("id").GetInt64();
        await using var db = fixture.CreateDbContext();
        var careEvents = await EventsAsync(db, "care", entryId);
        var logged = Assert.Single(careEvents, e => e.Type == "care.logged");
        Assert.Equal(id, logged.Payload.GetProperty("waterChangeSessionId").GetInt64());
        Assert.Equal("partial", logged.Payload.GetProperty("evidence").GetProperty("state").GetString());
        var sessionEvents = await EventsAsync(db, "water_change", id);
        Assert.Equal(["water_change.started", "water_change.finished"], sessionEvents.Select(e => e.Type));
        Assert.Equal(entryId, sessionEvents[1].Payload.GetProperty("careEntryId").GetInt64());
    }

    [Fact]
    public async Task GivenQuickOrSkippedGuidedMode_WhenFinishing_ThenTheEvidenceIsPartialWithTheFinding()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var skipped = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        using (var skipDrain = await StepAsync(token, tank, skipped, "draining_started", "skipped")) Assert.Equal(HttpStatusCode.OK, skipDrain.StatusCode);
        await AgeAsync(skipped, TimeSpan.FromMinutes(30));
        using (var finish = await FinishAsync(token, tank, skipped))
        {
            var evidence = (await BodyAsync(finish)).GetProperty("entry").GetProperty("evidence");
            Assert.Equal("partial", evidence.GetProperty("state").GetString());
            Assert.Equal(["draining_missing", "photo_missing"], evidence.GetProperty("missing").EnumerateArray().Select(e => e.GetString()));
        }
        var quick = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        using (var drain = await StepAsync(token, tank, quick, "draining_started", "done")) Assert.Equal(HttpStatusCode.OK, drain.StatusCode);
        using (var finish = await FinishAsync(token, tank, quick))
        {
            var evidence = (await BodyAsync(finish)).GetProperty("entry").GetProperty("evidence");
            Assert.Equal("partial", evidence.GetProperty("state").GetString());
            Assert.Equal(["duration_too_short", "photo_missing"], evidence.GetProperty("missing").EnumerateArray().Select(e => e.GetString()));
        }
    }

    [Fact]
    public async Task GivenLongGuidedModeWithoutAnInAppPhoto_WhenFinishing_ThenTheEvidenceIsPartialWithThePhotoFinding()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var withoutPhoto = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        await FinishLongAsync(token, tank, withoutPhoto);
        var libraryOnly = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        (await PhotoAsync(token, tank, libraryOnly, Jpeg(800, 600), inApp: false)).Dispose();
        (await PhotoAsync(token, tank, libraryOnly, Jpeg(800, 600), inApp: false)).Dispose();
        await FinishLongAsync(token, tank, libraryOnly);
        foreach (var id in new[] { withoutPhoto, libraryOnly })
        {
            await using var db = fixture.CreateDbContext();
            var entry = await db.CareEntries.AsNoTracking().SingleAsync(e => e.WaterChangeSessionId == id);
            Assert.Equal(WaterChangeEvidenceState.Partial, entry.Evidence);
            Assert.Equal(["photo_missing"], entry.EvidenceMissing);
        }
    }

    [Fact]
    public async Task GivenLongGuidedModeWithARefilledInAppPhoto_WhenFinishing_ThenTheEntryIsVerified()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        (await PhotoAsync(token, tank, id, Jpeg(800, 600), inApp: false)).Dispose();
        (await PhotoAsync(token, tank, id, Jpeg(800, 600))).Dispose();
        var evidence = await FinishLongAsync(token, tank, id);
        Assert.Equal("verified", evidence.GetProperty("state").GetString());
        Assert.Empty(evidence.GetProperty("missing").EnumerateArray());
    }

    private async Task<JsonElement> FinishLongAsync(string token, long tank, long id)
    {
        foreach (var name in new[] { "heater_filter_off", "draining_started", "refill_started", "heater_filter_on" })
        {
            using var marked = await StepAsync(token, tank, id, name, "done");
            Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        }
        await using (var db = fixture.CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE water_change_session_step SET at = at + interval '12 minutes' WHERE session_id = {id} AND step IN ('refill_started', 'heater_filter_on')");
        await AgeAsync(id, TimeSpan.FromMinutes(14));
        using var finished = await FinishAsync(token, tank, id);
        return (await BodyAsync(finished)).GetProperty("entry").GetProperty("evidence").Clone();
    }

    [Fact]
    public async Task GivenPlainCareLog_WhenListing_ThenTheEvidenceIsNone()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        await LogAsync(fixture.Client, token, tank);
        using var list = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        var entry = (await BodyAsync(list)).GetProperty("items")[0];
        Assert.Equal("none", entry.GetProperty("evidence").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("durationSeconds").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("waterChangeSessionId").ValueKind);
    }

    [Fact]
    public async Task GivenPhotos_WhenAdding_ThenTheyStayPrivateInOrderUpToTheLimit()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var session = await StartAsync(token, tank);
        var id = session.GetProperty("id").GetInt64();
        using var first = await PhotoAsync(token, tank, id, Jpeg(1200, 800));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var photo = (await BodyAsync(first)).GetProperty("photos")[0];
        Assert.True(photo.GetProperty("capturedInApp").GetBoolean());
        Assert.StartsWith("/api/care-photos/", photo.GetProperty("urls").GetProperty("thumb").GetString());
        using var served = await fixture.Client.GetAsync(photo.GetProperty("urls").GetProperty("thumb").GetString());
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Contains("private", served.Headers.CacheControl?.ToString());

        await using var db = fixture.CreateDbContext();
        var profileId = await db.WaterChangeSessions.Where(s => s.Id == id).Select(s => s.ProfileId).SingleAsync();
        var item = await db.MediaItems.Include(m => m.Variants).SingleAsync(m => m.OwnerProfileId == profileId);
        Assert.StartsWith("private/", item.StorageKey);
        Assert.All(item.Variants, v => Assert.StartsWith("private/", v.StorageKey));

        using var library = await PhotoAsync(token, tank, id, Jpeg(1000, 700), inApp: false);
        Assert.Equal(HttpStatusCode.OK, library.StatusCode);
        var photos = (await BodyAsync(library)).GetProperty("photos");
        Assert.Equal(2, photos.GetArrayLength());
        Assert.True(photos[0].GetProperty("capturedInApp").GetBoolean());
        Assert.False(photos[1].GetProperty("capturedInApp").GetBoolean());
        for (var count = 3; count <= 6; count++)
        {
            using var more = await PhotoAsync(token, tank, id, Jpeg(900, 600));
            Assert.Equal(HttpStatusCode.OK, more.StatusCode);
        }
        Assert.Equal(6, await CountKeysAsync(profileId));
        using var seventh = await PhotoAsync(token, tank, id, Jpeg(900, 600));
        await AssertProblemAsync(seventh, HttpStatusCode.BadRequest, "water_change_photo_limit");
        Assert.Equal(6, await CountKeysAsync(profileId));
        using var broken = await PhotoAsync(token, tank, id, UndecodableJpeg());
        await AssertProblemAsync(broken, HttpStatusCode.BadRequest, "care_photo_invalid");
    }

    [Fact]
    public async Task GivenSeveralPhotos_WhenFinishing_ThenTheEntryHoldsThemInOrderAndTheNextSessionOffersThemAsOverlays()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        (await PhotoAsync(token, tank, id, Jpeg(1200, 800))).Dispose();
        (await PhotoAsync(token, tank, id, Jpeg(1100, 800), inApp: false)).Dispose();
        (await PhotoAsync(token, tank, id, Jpeg(1000, 800))).Dispose();
        using var finished = await FinishAsync(token, tank, id);
        var body = await BodyAsync(finished);
        var photos = body.GetProperty("entry").GetProperty("photos");
        Assert.Equal(3, photos.GetArrayLength());
        Assert.Equal([1200, 1100, 1000], photos.EnumerateArray().Select(p => p.GetProperty("width").GetInt32()));
        var evidence = body.GetProperty("entry").GetProperty("evidence");
        Assert.Equal("partial", evidence.GetProperty("state").GetString());
        Assert.Equal(["draining_missing"], evidence.GetProperty("missing").EnumerateArray().Select(e => e.GetString()));
        Assert.False(body.GetProperty("session").GetProperty("photos")[1].GetProperty("capturedInApp").GetBoolean());

        var next = await StartAsync(token, tank);
        var previous = next.GetProperty("previousPhotos");
        Assert.Equal(3, previous.GetArrayLength());
        Assert.StartsWith("/api/care-photos/", previous[0].GetProperty("thumb").GetString());
    }

    [Fact]
    public async Task GivenAnInAppPhotoBeforeDrainingStarted_WhenFinishing_ThenItDoesNotCountAsEvidence()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        (await PhotoAsync(token, tank, id, Jpeg(800, 600))).Dispose();
        using (var drain = await StepAsync(token, tank, id, "draining_started", "done")) Assert.Equal(HttpStatusCode.OK, drain.StatusCode);
        using (var refill = await StepAsync(token, tank, id, "refill_started", "done")) Assert.Equal(HttpStatusCode.OK, refill.StatusCode);
        await using (var db = fixture.CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE water_change_session_step SET at = at + interval '12 minutes' WHERE session_id = {id} AND step = 'refill_started'");
        using var finished = await FinishAsync(token, tank, id);
        var entry = (await BodyAsync(finished)).GetProperty("entry");
        Assert.Equal("partial", entry.GetProperty("evidence").GetProperty("state").GetString());
        Assert.Equal(["photo_outside_session"], entry.GetProperty("evidence").GetProperty("missing").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(1, entry.GetProperty("photos").GetArrayLength());
    }

    [Fact]
    public async Task GivenSeveralPhotosOfWhichOneIsInTheSpan_WhenFinishing_ThenTheEntryIsVerified()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        (await PhotoAsync(token, tank, id, Jpeg(800, 600))).Dispose();
        (await PhotoAsync(token, tank, id, Jpeg(810, 600), inApp: false)).Dispose();
        using (var drain = await StepAsync(token, tank, id, "draining_started", "done")) Assert.Equal(HttpStatusCode.OK, drain.StatusCode);
        (await PhotoAsync(token, tank, id, Jpeg(820, 600))).Dispose();
        using (var refill = await StepAsync(token, tank, id, "refill_started", "done")) Assert.Equal(HttpStatusCode.OK, refill.StatusCode);
        await using (var db = fixture.CreateDbContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE water_change_session_step SET at = at + interval '12 minutes' WHERE session_id = {id} AND step = 'refill_started'");
        using var finished = await FinishAsync(token, tank, id);
        var entry = (await BodyAsync(finished)).GetProperty("entry");
        Assert.Equal("verified", entry.GetProperty("evidence").GetProperty("state").GetString());
        Assert.Equal(3, entry.GetProperty("photos").GetArrayLength());
    }

    // Runs a session whose draining span is long enough with one app photo received during draining;
    // captureTimeUtc sets the EXIF capture time of the stored photo before the finish.
    private async Task<JsonElement> RunWithPhotoAsync(string token, long tank, byte[] bytes, DateTime? captureTimeUtc = null)
    {
        var id = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        using (var drain = await StepAsync(token, tank, id, "draining_started", "done")) Assert.Equal(HttpStatusCode.OK, drain.StatusCode);
        (await PhotoAsync(token, tank, id, bytes)).Dispose();
        using (var refill = await StepAsync(token, tank, id, "refill_started", "done")) Assert.Equal(HttpStatusCode.OK, refill.StatusCode);
        await using (var db = fixture.CreateDbContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE water_change_session_step SET at = at + interval '12 minutes' WHERE session_id = {id} AND step = 'refill_started'");
            if (captureTimeUtc is { } captured)
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE media_item SET taken_at_local = {captured}, taken_at_offset_minutes = 0 WHERE id IN (SELECT media_item_id FROM water_change_session_photo WHERE session_id = {id})");
            }
        }
        using var finished = await FinishAsync(token, tank, id);
        return (await BodyAsync(finished)).GetProperty("entry").GetProperty("evidence").Clone();
    }

    private static string[] MissingOf(JsonElement evidence) => [.. evidence.GetProperty("missing").EnumerateArray().Select(e => e.GetString()!)];

    [Fact]
    public async Task GivenACaptureTimeFarFromTheSession_WhenFinishing_ThenThePhotoDoesNotCount()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var old = await RunWithPhotoAsync(token, tank, Jpeg(800, 600), new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        Assert.Equal("partial", old.GetProperty("state").GetString());
        Assert.Equal(["photo_outside_session"], MissingOf(old));
        var future = await RunWithPhotoAsync(token, tank, Jpeg(810, 600), DateTime.UtcNow.AddHours(2));
        Assert.Equal(["photo_outside_session"], MissingOf(future));
    }

    [Fact]
    public async Task GivenACaptureTimeInsideTheSessionOrNone_WhenFinishing_ThenThePhotoCounts()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var inside = await RunWithPhotoAsync(token, tank, Jpeg(800, 600), DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal("verified", inside.GetProperty("state").GetString());
        var none = await RunWithPhotoAsync(token, tank, Jpeg(810, 600));
        Assert.Equal("verified", none.GetProperty("state").GetString());
    }

    [Fact]
    public async Task GivenTheSamePictureForASecondWaterChange_WhenFinishing_ThenItIsReusedAndAnotherMemberStillCounts()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var first = await RunWithPhotoAsync(token, tank, Jpeg(800, 600));
        Assert.Equal("verified", first.GetProperty("state").GetString());
        var second = await RunWithPhotoAsync(token, tank, Jpeg(800, 600));
        Assert.Equal("partial", second.GetProperty("state").GetString());
        Assert.Equal(["photo_reused"], MissingOf(second));
        var third = await RunWithPhotoAsync(token, tank, Jpeg(820, 600));
        Assert.Equal("verified", third.GetProperty("state").GetString());
        var (otherToken, otherTank) = await OwnTankAsync(fixture.Client);
        Assert.Equal("verified", (await RunWithPhotoAsync(otherToken, otherTank, Jpeg(800, 600))).GetProperty("state").GetString());
    }

    [Fact]
    public async Task GivenInvalidAmount_WhenFinishing_ThenNothingIsCreatedAndTheSessionStaysOpen()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        using var finish = await FinishAsync(token, tank, id, new { amount = new { value = 250, unit = "percent" } });
        await AssertProblemAsync(finish, HttpStatusCode.BadRequest, "care_amount_invalid");
        using var list = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        Assert.Equal(0, (await BodyAsync(list)).GetProperty("total").GetInt32());
        using var current = await SendAsync(fixture.Client, HttpMethod.Get, $"{Base(tank)}/current", token);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
    }

    [Fact]
    public async Task GivenSessionOlderThan24Hours_WhenTheMemberReturns_ThenItIsGoneWithItsPhotosAndANewOneStarts()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        (await PhotoAsync(token, tank, id, Jpeg(800, 600))).Dispose();
        await using var db = fixture.CreateDbContext();
        var profileId = await db.WaterChangeSessions.Where(s => s.Id == id).Select(s => s.ProfileId).SingleAsync();
        Assert.Equal(1, await CountKeysAsync(profileId));
        await AgeAsync(id, TimeSpan.FromHours(25));

        using var current = await SendAsync(fixture.Client, HttpMethod.Get, $"{Base(tank)}/current", token);
        await AssertProblemAsync(current, HttpStatusCode.NotFound, "water_change_not_found");
        Assert.False(await db.WaterChangeSessions.AnyAsync(s => s.Id == id));
        Assert.Equal(0, await CountKeysAsync(profileId));
        var fresh = await StartAsync(token, tank);
        Assert.NotEqual(id, fresh.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task GivenExpiredSessions_WhenTheCleanupPassRuns_ThenOnlyOpenSessionsPastTheLifetimeAreRemoved()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var old = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        await AgeAsync(old, TimeSpan.FromHours(30));
        var (otherToken, otherTank) = await OwnTankAsync(fixture.Client);
        var recent = (await StartAsync(otherToken, otherTank)).GetProperty("id").GetInt64();
        using var scope = fixture.Services.CreateScope();
        var removed = await scope.ServiceProvider.GetRequiredService<WaterChangeService>().ExpireAllAsync(CancellationToken.None);
        Assert.True(removed >= 1);
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.WaterChangeSessions.AnyAsync(s => s.Id == old));
        Assert.True(await db.WaterChangeSessions.AnyAsync(s => s.Id == recent));
    }

    [Fact]
    public async Task GivenOpenSession_WhenCancelling_ThenItsRowsAndPhotosAreDeleted()
    {
        var (token, tank) = await OwnTankAsync(fixture.Client);
        var id = (await StartAsync(token, tank)).GetProperty("id").GetInt64();
        (await PhotoAsync(token, tank, id, Jpeg(800, 600))).Dispose();
        await using var db = fixture.CreateDbContext();
        var profileId = await db.WaterChangeSessions.Where(s => s.Id == id).Select(s => s.ProfileId).SingleAsync();
        using var cancel = await SendAsync(fixture.Client, HttpMethod.Delete, $"{Base(tank)}/{id}", token);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.False(await db.WaterChangeSessions.AnyAsync(s => s.Id == id));
        Assert.Equal(0, await CountKeysAsync(profileId));
        using var again = await SendAsync(fixture.Client, HttpMethod.Delete, $"{Base(tank)}/{id}", token);
        await AssertProblemAsync(again, HttpStatusCode.NotFound, "water_change_not_found");
    }
}
