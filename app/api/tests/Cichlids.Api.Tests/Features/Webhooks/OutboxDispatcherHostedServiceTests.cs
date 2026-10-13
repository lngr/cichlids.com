using System.Net;
using Cichlids.Api.Features.Webhooks;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Outbox;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Cichlids.Api.Tests.Features.Webhooks;

/// <summary>
/// The hosted polling loop as it runs inside the API host. A failing pass is logged and polling
/// goes on, so neither a broken event nor an unreachable database stops the host.
/// </summary>
[Collection(OutboxDispatchCollection.Name)]
public class OutboxDispatcherHostedServiceTests(OutboxDispatchFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetOutboxAsync();

    public Task DisposeAsync() => fixture.ResetOutboxAsync();

    [Fact]
    public async Task ApiKeepsServingWhileTheLoopHitsACorruptEvent()
    {
        await using var listener = new TestWebhookListener();
        listener.Start();
        await using (var db = fixture.CreateDbContext())
        {
            db.WebhookSubscriptions.Add(new WebhookSubscription
            {
                Url = listener.Uri.ToString(),
                Secret = "hosted-secret",
                EventTypes = ["test.event"],
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // The allowlist is read while the application is being built, so it is set as a host
            // setting.
            builder.UseSetting("Webhooks:AllowedTargetHosts:0", "127.0.0.1");
            builder.UseSetting("Webhooks:AllowedTargetHosts:1", "localhost");
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Cichlids"] = fixture.ConnectionString,
                    ["ObjectStorage:ServiceUrl"] = "http://objects.test",
                    ["ObjectStorage:PublicBaseUrl"] = "http://objects.test/cichlids-media",
                    ["OutboxDispatcher:Enabled"] = "true",
                    ["OutboxDispatcher:PollInterval"] = "00:00:00.200",
                }));
        });
        using var client = factory.CreateClient();
        var lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();

        var now = DateTimeOffset.UtcNow;
        await InsertCorruptEventAsync(now.AddSeconds(-2));
        Guid good;
        await using (var db = fixture.CreateDbContext())
        {
            var outboxEvent = OutboxEvent.Create("test.event", "post", "1", """{"postId":1}""", now.AddSeconds(-1));
            db.OutboxEvents.Add(outboxEvent);
            await db.SaveChangesAsync();
            good = outboxEvent.Id;
        }

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (listener.AcceptedRequests.Count == 0 && DateTime.UtcNow < deadline)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
            await Task.Delay(100);
        }

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
            await Task.Delay(250);
        }

        Assert.Equal([good], listener.AcceptedRequests.Select(r => r.EventId));
        Assert.False(lifetime.ApplicationStopping.IsCancellationRequested);
    }

    [Fact]
    public async Task DatabaseOutageIsLoggedAndPollingContinues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Cichlids"] = "Host=127.0.0.1;Port=1;Database=cichlids;Username=cichlids;Password=cichlids;Timeout=2",
                ["OutboxDispatcher:Enabled"] = "true",
                ["OutboxDispatcher:PollInterval"] = "00:00:00.100",
                ["Webhooks:AllowedTargetHosts:0"] = "127.0.0.1",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCichlidsDbContext(configuration);
        services.AddOutboxDispatcher(configuration, WebhookTargetOptions.Resolve(configuration));
        await using var provider = services.BuildServiceProvider();

        var logger = new FakeLogger<OutboxDispatcherHostedService>();
        var hostedService = ActivatorUtilities.CreateInstance<OutboxDispatcherHostedService>(provider, logger);

        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (ErrorCount(logger) < 2 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }

            Assert.True(ErrorCount(logger) >= 2, $"Expected at least two error logs, got {ErrorCount(logger)}.");
            Assert.NotNull(hostedService.ExecuteTask);
            Assert.False(hostedService.ExecuteTask.IsCompleted);
            Assert.Contains(
                logger.Collector.GetSnapshot(),
                record => record.Level == LogLevel.Information && record.Message.Contains("127.0.0.1"));
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        Assert.False(hostedService.ExecuteTask!.IsFaulted);
    }

    private static int ErrorCount(FakeLogger logger) =>
        logger.Collector.GetSnapshot().Count(record => record.Level == LogLevel.Error);

    private async Task InsertCorruptEventAsync(DateTimeOffset occurredAt)
    {
        var id = Guid.CreateVersion7();
        var payload = new string('[', 100) + new string(']', 100);
        await using var db = fixture.CreateDbContext();
        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO outbox_event (id, occurred_at, event_type, aggregate_type, aggregate_id, payload, next_attempt_at)
            VALUES ({id}, {occurredAt}, 'test.event', 'post', '1', {payload}::jsonb, {occurredAt})
            """);
    }
}
