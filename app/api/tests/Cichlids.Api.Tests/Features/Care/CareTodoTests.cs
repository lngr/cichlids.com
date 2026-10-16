using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;

namespace Cichlids.Api.Tests.Features.Care;

[Collection(ApiCollection.Name)]
public sealed class CareTodoTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<long> _tanks = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = fixture.CreateDbContext();
        await RemoveTanksAsync(db, _tanks);
    }

    [Fact]
    public async Task GivenOwnEntry_WhenAddingATodo_ThenTheEntryAndTheOpenListShowItAndAnEventIsRecorded()
    {
        var (token, tank) = await OwnTankAsync();
        var entry = await LogAsync(fixture.Client, token, tank);
        using var created = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/care/{entry}/todos", token, new { text = "  Buy water conditioner " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var todo = await BodyAsync(created);
        Assert.Equal("Buy water conditioner", todo.GetProperty("text").GetString());
        Assert.Equal(entry, todo.GetProperty("careEntryId").GetInt64());
        Assert.Equal(tank, todo.GetProperty("tankId").GetInt64());
        Assert.False(todo.GetProperty("done").GetBoolean());
        Assert.Equal(JsonValueKind.Null, todo.GetProperty("doneAt").ValueKind);

        using var list = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/tanks/{tank}/care", token);
        var shown = Assert.Single((await BodyAsync(list)).GetProperty("items")[0].GetProperty("todos").EnumerateArray());
        Assert.Equal(todo.GetProperty("id").GetInt64(), shown.GetProperty("id").GetInt64());
        using var open = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/todos", token);
        var item = Assert.Single((await BodyAsync(open)).GetProperty("items").EnumerateArray());
        Assert.Equal("Care test", item.GetProperty("tankTitle").GetString());
        Assert.Equal("Buy water conditioner", item.GetProperty("text").GetString());

        await using var db = fixture.CreateDbContext();
        var events = await EventsAsync(db, "todo", todo.GetProperty("id").GetInt64());
        var created1 = Assert.Single(events);
        Assert.Equal("todo.created", created1.Type);
        Assert.Equal(tank, created1.Payload.GetProperty("tankId").GetInt64());
        Assert.Equal(entry, created1.Payload.GetProperty("careEntryId").GetInt64());
        Assert.Equal("Buy water conditioner", created1.Payload.GetProperty("text").GetString());
        Assert.False(created1.Payload.GetProperty("done").GetBoolean());
        Assert.True(created1.Payload.GetProperty("profileId").GetInt64() > 0);
    }

    [Fact]
    public async Task GivenTank_WhenAddingATodoWithoutAnEntry_ThenItBelongsOnlyToTheTank()
    {
        var (token, tank) = await OwnTankAsync();
        using var created = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/todos", token, new { text = "Clean the filter" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var todo = await BodyAsync(created);
        Assert.Equal(JsonValueKind.Null, todo.GetProperty("careEntryId").ValueKind);
        using var open = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/todos", token);
        Assert.Single((await BodyAsync(open)).GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("x", true)]
    [InlineData("200", true)]
    [InlineData("201", false)]
    public async Task GivenTextLength_WhenAddingATodo_ThenTheLimitIs200Characters(string kind, bool accepted)
    {
        var (token, tank) = await OwnTankAsync();
        var text = kind switch { "200" => new string('t', 200), "201" => new string('t', 201), _ => kind };
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/todos", token, new { text });
        if (accepted) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        else
        {
            await AssertProblemAsync(response, HttpStatusCode.BadRequest, "todo_text_invalid");
            await using var db = fixture.CreateDbContext();
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM care_todo WHERE tank_id = {tank}").SingleAsync());
        }
    }

    [Fact]
    public async Task GivenOpenTodo_WhenMarkedDoneAndReopened_ThenListsEventsAndTimesFollow()
    {
        var (token, tank) = await OwnTankAsync();
        var id = await AddTodoAsync(token, tank, "Order test strips");
        using var done = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/me/todos/{id}", token, new { done = true });
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        var doneBody = await BodyAsync(done);
        Assert.True(doneBody.GetProperty("done").GetBoolean());
        Assert.InRange(DateTimeOffset.Parse(doneBody.GetProperty("doneAt").GetString()!), DateTimeOffset.UtcNow.AddSeconds(-15), DateTimeOffset.UtcNow.AddSeconds(2));
        using var again = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/me/todos/{id}", token, new { done = true });
        Assert.Equal(doneBody.GetProperty("doneAt").GetString(), (await BodyAsync(again)).GetProperty("doneAt").GetString());

        Assert.Empty((await ListAsync(token, "")).GetProperty("items").EnumerateArray());
        Assert.Single((await ListAsync(token, "?state=done")).GetProperty("items").EnumerateArray());
        Assert.Single((await ListAsync(token, "?state=all")).GetProperty("items").EnumerateArray());

        using var reopened = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/me/todos/{id}", token, new { done = false });
        Assert.False((await BodyAsync(reopened)).GetProperty("done").GetBoolean());
        Assert.Single((await ListAsync(token, "")).GetProperty("items").EnumerateArray());

        await using var db = fixture.CreateDbContext();
        var events = await EventsAsync(db, "todo", id);
        Assert.Equal(new[] { "todo.created", "todo.updated", "todo.updated" }, events.Select(e => e.Type));
        Assert.True(events[1].Payload.GetProperty("done").GetBoolean());
        Assert.False(events[2].Payload.GetProperty("done").GetBoolean());
    }

    [Fact]
    public async Task GivenTodoText_WhenChanged_ThenItIsValidatedAndTheEventFollows()
    {
        var (token, tank) = await OwnTankAsync();
        var id = await AddTodoAsync(token, tank, "old");
        using var changed = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/me/todos/{id}", token, new { text = " new " });
        Assert.Equal("new", (await BodyAsync(changed)).GetProperty("text").GetString());
        using var invalid = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/me/todos/{id}", token, new { text = new string('t', 201) });
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "todo_text_invalid");
        using var empty = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/me/todos/{id}", token, new { });
        await AssertProblemAsync(empty, HttpStatusCode.BadRequest, "todo_request_invalid");
        await using var db = fixture.CreateDbContext();
        Assert.Equal(2, (await EventsAsync(db, "todo", id)).Count);
    }

    [Fact]
    public async Task GivenTodosOfSeveralTanks_WhenListing_ThenAllOpenOnesOfTheMemberComeNewestFirst()
    {
        var (token, first) = await OwnTankAsync();
        using var second = await SendAsync(fixture.Client, HttpMethod.Post, "/api/tanks", token, new { title = "Second tank", isPublic = true });
        var secondTank = (await BodyAsync(second)).GetProperty("id").GetInt64();
        _tanks.Add(secondTank);
        var a = await AddTodoAsync(token, first, "a");
        var b = await AddTodoAsync(token, secondTank, "b");
        var c = await AddTodoAsync(token, first, "c");
        using var done = await SendAsync(fixture.Client, HttpMethod.Patch, $"/api/me/todos/{b}", token, new { done = true });
        var (stranger, strangerTank) = await OwnTankAsync();
        await AddTodoAsync(stranger, strangerTank, "not mine");

        var open = await ListAsync(token, "");
        Assert.Equal(2, open.GetProperty("total").GetInt32());
        Assert.Equal(new[] { c, a }, open.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()));
        var all = await ListAsync(token, "?state=all");
        Assert.Equal(3, all.GetProperty("total").GetInt32());
        var paged = await ListAsync(token, "?state=all&offset=1&limit=1");
        Assert.Equal(3, paged.GetProperty("total").GetInt32());
        Assert.Equal(b, Assert.Single(paged.GetProperty("items").EnumerateArray()).GetProperty("id").GetInt64());
        var titles = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("tankTitle").GetString()).Distinct().Order().ToList();
        Assert.Equal(new[] { "Care test", "Second tank" }, titles);
    }

    [Fact]
    public async Task GivenOtherMemberOrAnonymous_WhenTouchingTodos_ThenCodedRefusalsAndNoEvents()
    {
        var (owner, tank) = await OwnTankAsync();
        var entry = await LogAsync(fixture.Client, owner, tank);
        var id = await AddTodoAsync(owner, tank, "private");
        var other = NewMemberToken();
        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Patch, $"/api/me/todos/{id}", new { done = true }),
            (HttpMethod.Delete, $"/api/me/todos/{id}", null),
        })
        {
            using var refused = await SendAsync(fixture.Client, method, url, other, body);
            await AssertProblemAsync(refused, HttpStatusCode.NotFound, "todo_not_found");
        }
        using var onEntry = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/care/{entry}/todos", other, new { text = "x" });
        await AssertProblemAsync(onEntry, HttpStatusCode.NotFound, "care_not_found");
        using var onTank = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/todos", other, new { text = "x" });
        await AssertProblemAsync(onTank, HttpStatusCode.NotFound, "care_not_found");
        Assert.Empty((await ListAsync(other, "?state=all")).GetProperty("items").EnumerateArray());
        foreach (var (method, url) in new[] { (HttpMethod.Get, "/api/me/todos"), (HttpMethod.Patch, $"/api/me/todos/{id}"), (HttpMethod.Post, $"/api/tanks/{tank}/todos") })
        {
            using var anonymous = await SendAsync(fixture.Client, method, url, null, method == HttpMethod.Get ? null : new { done = true });
            await AssertProblemAsync(anonymous, HttpStatusCode.Unauthorized, "care_auth_required");
        }
        await using var db = fixture.CreateDbContext();
        Assert.Single(await EventsAsync(db, "todo", id));
    }

    [Fact]
    public async Task GivenTodo_WhenDeleted_ThenItIsGoneAndAnEventIsRecorded()
    {
        var (token, tank) = await OwnTankAsync();
        var id = await AddTodoAsync(token, tank, "temporary");
        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/todos/{id}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal("temporary", (await BodyAsync(deleted)).GetProperty("text").GetString());
        Assert.Empty((await ListAsync(token, "?state=all")).GetProperty("items").EnumerateArray());
        using var again = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/me/todos/{id}", token);
        await AssertProblemAsync(again, HttpStatusCode.NotFound, "todo_not_found");
        await using var db = fixture.CreateDbContext();
        Assert.Equal(new[] { "todo.created", "todo.deleted" }, (await EventsAsync(db, "todo", id)).Select(e => e.Type));
    }

    [Fact]
    public async Task GivenEntryWithTodo_WhenTheEntryIsDeleted_ThenTheTodoStaysOnTheTank()
    {
        var (token, tank) = await OwnTankAsync();
        var entry = await LogAsync(fixture.Client, token, tank);
        using var created = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/care/{entry}/todos", token, new { text = "keep me" });
        var id = (await BodyAsync(created)).GetProperty("id").GetInt64();
        using var deleted = await SendAsync(fixture.Client, HttpMethod.Delete, $"/api/tanks/{tank}/care/{entry}", token);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var open = await ListAsync(token, "");
        var item = Assert.Single(open.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetInt64());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("careEntryId").ValueKind);
        await using var db = fixture.CreateDbContext();
        var events = await EventsAsync(db, "todo", id);
        Assert.Equal(new[] { "todo.created", "todo.updated" }, events.Select(e => e.Type));
        Assert.Equal(JsonValueKind.Null, events[1].Payload.GetProperty("careEntryId").ValueKind);
    }

    [Fact]
    public async Task GivenDeletedTank_WhenListing_ThenItsTodosAreHidden()
    {
        var (token, tank) = await OwnTankAsync();
        await AddTodoAsync(token, tank, "gone with the tank");
        await using var db = fixture.CreateDbContext();
        await db.Tanks.Where(t => t.Id == tank).ExecuteUpdateAsync(s => s.SetProperty(t => t.DeletedAt, DateTimeOffset.UtcNow));
        Assert.Empty((await ListAsync(token, "?state=all")).GetProperty("items").EnumerateArray());
        using var onTank = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/todos", token, new { text = "x" });
        await AssertProblemAsync(onTank, HttpStatusCode.NotFound, "care_not_found");
    }

    [Fact]
    public async Task GivenInvalidState_WhenListing_ThenCodedBadRequest()
    {
        var (token, _) = await OwnTankAsync();
        using var response = await SendAsync(fixture.Client, HttpMethod.Get, "/api/me/todos?state=bogus", token);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "todo_request_invalid");
    }

    [Fact]
    public async Task GivenInvalidPersistedText_WhenSaved_ThenDatabaseConstraintRefuses()
    {
        var (token, tank) = await OwnTankAsync();
        var id = await AddTodoAsync(token, tank, "valid");
        await using var db = fixture.CreateDbContext();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE public.care_todo SET text = repeat('t', 201) WHERE id = {0}", id));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(
            "UPDATE public.care_todo SET text = '' WHERE id = {0}", id));
    }

    private async Task<(string Token, long Tank)> OwnTankAsync()
    {
        var own = await CareTestSupport.OwnTankAsync(fixture.Client);
        _tanks.Add(own.Tank);
        return own;
    }

    private async Task<long> AddTodoAsync(string token, long tank, string text)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Post, $"/api/tanks/{tank}/todos", token, new { text });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt64();
    }

    private async Task<JsonElement> ListAsync(string token, string query)
    {
        using var response = await SendAsync(fixture.Client, HttpMethod.Get, $"/api/me/todos{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await BodyAsync(response);
    }
}
