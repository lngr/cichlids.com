using System.Security.Claims;
using System.Text.RegularExpressions;
using Cichlids.Api.Features.Auth;
using Cichlids.Api.Features.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Cichlids.Api.Features.WebPush;

public static partial class WebPushEndpoints
{
    /// <summary>
    /// Whether a request path addresses the Web Push routes of a member, so framework refusals such
    /// as binding errors or a missing login on these routes carry a Web Push problem code.
    /// </summary>
    public static bool IsWebPushPath(PathString path) => path.HasValue && WebPushPath().IsMatch(path.Value!);

    [GeneratedRegex("^/api/me/web-push(/subscriptions)?/?$", RegexOptions.CultureInvariant)]
    private static partial Regex WebPushPath();

    public static IEndpointRouteBuilder MapWebPushEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/web-push").WithTags("Web Push").RequireAuthorization();
        group.MapGet("", Config).WithName("GetWebPushConfig").ProducesProblem(401).ProducesProblem(403);
        group.MapPost("/subscriptions", SubscribeAsync).WithName("SubscribeWebPush")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);
        group.MapDelete("/subscriptions", UnsubscribeAsync).WithName("UnsubscribeWebPush")
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);
        return app;
    }

    private static Ok<WebPushConfigDto> Config(WebPushSettings settings) =>
        TypedResults.Ok(new WebPushConfigDto(settings.Available, settings.Available ? settings.PublicKey : null));

    private static async Task<Results<Ok<WebPushSubscriptionDto>, Created<WebPushSubscriptionDto>, ProblemHttpResult>> SubscribeAsync(
        SubscribeWebPushRequest request, ClaimsPrincipal user, CurrentProfileService profiles, WebPushSubscriptionService subscriptions, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        var (subscription, created, error) = await subscriptions.SubscribeAsync(profile.Id, request, ct);
        if (error is not null) return Problem(error);
        return created
            ? TypedResults.Created($"/api/me/web-push/subscriptions/{subscription!.Id}", subscription)
            : TypedResults.Ok(subscription!);
    }

    private static async Task<NoContent> UnsubscribeAsync(
        [FromBody] UnsubscribeWebPushRequest request, ClaimsPrincipal user, CurrentProfileService profiles, WebPushSubscriptionService subscriptions, CancellationToken ct)
    {
        var profile = await profiles.ResolveAsync(user, ct);
        await subscriptions.UnsubscribeAsync(profile.Id, request.Endpoint, ct);
        return TypedResults.NoContent();
    }

    internal static ProblemHttpResult Problem(string code) => ApiProblems.WithCode(
        code switch { "web_push_unavailable" => 404, "web_push_limit_reached" => 409, _ => 400 },
        "Web Push request refused", code switch
        {
            "web_push_unavailable" => "This server does not send browser notifications.",
            "web_push_limit_reached" => "A member keeps up to 10 browsers with reminders. Switch reminders off in a browser you no longer use.",
            "web_push_endpoint_not_allowed" => "The push service of this browser is not supported.",
            _ => "Send the endpoint, the keys p256dh and auth of the browser subscription, and the IANA time zone of the browser.",
        }, code);
}
