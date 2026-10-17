using Cichlids.Api.Features.Auth.Devices;

namespace Cichlids.Api.Features.Auth;

/// <summary>
/// Records the activity of the registered member behind an authenticated request, once per
/// request and after authorization. A provisional caller and an anonymous request are not
/// tracked. A subject that was recorded within the recording interval sends no statement. A failing write is logged as a warning and does not fail the request.
/// </summary>
public static class MemberActivityMiddleware
{
    public static IApplicationBuilder UseMemberActivity(this IApplicationBuilder app) => app.Use(InvokeAsync);

    private static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (context.User.KeycloakSubject() is { } subject)
        {
            var services = context.RequestServices;
            var throttle = services.GetRequiredService<MemberActivityThrottle>();
            try
            {
                if (!throttle.IsRecent(subject)
                    && await services.GetRequiredService<MemberActivityService>()
                        .RecordAsync(subject, services.GetRequiredService<TimeProvider>().GetUtcNow(), context.RequestAborted))
                {
                    throttle.Remember(subject);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MemberActivityMiddleware).FullName!)
                    .LogWarning(exception, "The activity of a member could not be recorded.");
            }
        }

        await next(context);
    }
}
