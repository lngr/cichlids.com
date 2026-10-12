using Cichlids.Api.Features.Auth;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Outbox;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Webhooks;

/// <summary>
/// Admin management for webhook subscriptions: registration and listing. A registration is
/// accepted only for a target the webhook target allowlist allows. Correcting a subscription means
/// registering a new one, which is cheap at this scale.
/// </summary>
public static class WebhookAdminEndpoints
{
    public static IEndpointRouteBuilder MapWebhookAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/webhooks")
            .WithTags("Webhooks")
            .RequireAuthorization(AuthorizationPolicies.Admin);

        group.MapPost("/", CreateAsync).WithName("CreateWebhookSubscription");
        group.MapGet("/", ListAsync).WithName("ListWebhookSubscriptions");

        return app;
    }

    private static async Task<Results<Created<WebhookDto>, BadRequest<string>>> CreateAsync(
        CichlidsDbContext context,
        WebhookTargetPolicy targetPolicy,
        [FromBody] CreateWebhookRequest request,
        CancellationToken cancellationToken)
    {
        var url = request.Url?.Trim();
        var secret = request.Secret?.Trim();
        var eventTypes = request.EventTypes?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList() ?? [];

        if (!targetPolicy.IsAllowed(url, out var rejection))
        {
            return TypedResults.BadRequest(rejection);
        }

        if (string.IsNullOrEmpty(secret))
        {
            return TypedResults.BadRequest("A non-empty secret is required.");
        }

        if (eventTypes.Count == 0)
        {
            return TypedResults.BadRequest("At least one event type is required.");
        }

        var subscription = new WebhookSubscription
        {
            Url = url,
            Secret = secret,
            EventTypes = eventTypes,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        context.WebhookSubscriptions.Add(subscription);
        await context.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/admin/webhooks/{subscription.Id}", ToDto(subscription));
    }

    private static async Task<Ok<List<WebhookDto>>> ListAsync(CichlidsDbContext context, CancellationToken cancellationToken)
    {
        var subscriptions = await context.WebhookSubscriptions.OrderBy(s => s.Id).ToListAsync(cancellationToken);
        return TypedResults.Ok(subscriptions.Select(ToDto).ToList());
    }

    private static WebhookDto ToDto(WebhookSubscription subscription) =>
        new(subscription.Id, subscription.Url, subscription.EventTypes, subscription.Active, subscription.CreatedAt);
}
