using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Cichlids.Api.Features.Common;

/// <summary>
/// The size limit for requests with a JSON body. A declared body length above the limit is
/// answered with 413 problem details and the code request_too_large before the endpoint runs;
/// for a body without a declared length the server's body size limit is lowered to the same
/// value, so reading beyond it fails with 413. Other content types, such as the multipart
/// photo upload, keep the limit of their endpoint.
/// </summary>
public static class JsonRequestSizeLimit
{
    public const long MaxBytes = 64 * 1024;

    public const string TooLargeCode = "request_too_large";

    public static IApplicationBuilder UseJsonRequestSizeLimit(this IApplicationBuilder app) =>
        app.Use(InvokeAsync);

    public static async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!IsJson(context.Request.ContentType))
        {
            await next(context);
            return;
        }

        if (context.Request.ContentLength > MaxBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(
                new ProblemDetails
                {
                    Status = StatusCodes.Status413PayloadTooLarge,
                    Title = "Request too large",
                    Detail = $"JSON requests must not exceed {MaxBytes / 1024} KB.",
                    Extensions = { ["code"] = TooLargeCode },
                },
                options: null,
                contentType: "application/problem+json");
            return;
        }

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = MaxBytes;
        }

        await next(context);
    }

    private static bool IsJson(string? contentType) =>
        contentType is not null
        && (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("+json", StringComparison.OrdinalIgnoreCase));
}
