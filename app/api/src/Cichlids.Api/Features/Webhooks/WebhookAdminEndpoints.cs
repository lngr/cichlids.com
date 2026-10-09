using Cichlids.Api.Features.Auth;
using Cichlids.Domain.Entities;
using Cichlids.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Api.Features.Webhooks;

/// <summary>
/// Minimal admin management for webhook subscriptions: registration and listing only. There is no
/// update or delete endpoint; re-registering a corrected subscription is cheap at this scale and
/// an operator UI does not exist yet to justify the extra surface.
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
        CichlidsDbContext context, [FromBody] CreateWebhookRequest request, CancellationToken cancellationToken)
    {
        var url = request.Url?.Trim();
        var secret = request.Secret?.Trim();
        var eventTypes = request.EventTypes?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .ToList() ?? [];

        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            return TypedResults.BadRequest("A valid absolute url is required.");
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
