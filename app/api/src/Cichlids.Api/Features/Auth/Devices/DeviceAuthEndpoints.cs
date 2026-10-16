using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using Cichlids.Api.Features.Common;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Cichlids.Api.Features.Auth.Devices;

/// <summary>
/// The anonymous endpoints through which an app installation gets its provisional profile and its
/// tokens: registering a public key, asking for a challenge and exchanging the signed challenge for
/// a device token. Registration is limited per hour and the two token requests per minute, each per
/// network address.
/// </summary>
public static class DeviceAuthEndpoints
{
    public const string RegistrationLimiter = "device-registration";
    public const string TokenLimiter = "device-token";

    public const string KeyInvalidCode = "device_key_invalid";
    public const string KeyRetiredCode = "device_key_retired";
    public const string RefusedCode = "device_auth_refused";
    public const string RateLimitedCode = "device_rate_limited";

    public static IEndpointRouteBuilder MapDeviceAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/device").WithTags("DeviceAuth").AllowAnonymous();

        group.MapPost("/credentials", RegisterAsync).WithName("RegisterDeviceKey")
            .RequireRateLimiting(RegistrationLimiter)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapPost("/challenges", ChallengeAsync).WithName("CreateDeviceChallenge")
            .RequireRateLimiting(TokenLimiter)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapPost("/tokens", TokenAsync).WithName("CreateDeviceToken")
            .RequireRateLimiting(TokenLimiter)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        return app;
    }

    /// <summary>
    /// Adds the per-address limiters of the two endpoint groups. The limits are read from the
    /// DeviceAuth options when a request arrives.
    /// </summary>
    public static IServiceCollection AddDeviceAuthRateLimits(this IServiceCollection services) =>
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await ApiProblems.WithCode(
                    StatusCodes.Status429TooManyRequests, "Too many requests", "This network address made too many requests. Try again later.", RateLimitedCode)
                    .ExecuteAsync(context.HttpContext);
            };
            limiter.AddPolicy(RegistrationLimiter, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<DeviceAuthOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    "registration:" + AddressKey(context.Connection.RemoteIpAddress),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.MaxRegistrationsPerAddressPerHour,
                        Window = TimeSpan.FromHours(1),
                        QueueLimit = 0,
                    });
            });
            limiter.AddPolicy(TokenLimiter, context =>
            {
                var limits = context.RequestServices.GetRequiredService<IOptions<DeviceAuthOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(
                    "token:" + AddressKey(context.Connection.RemoteIpAddress),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.MaxTokenRequestsPerAddressPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
        });

    /// <summary>
    /// The key a network address is counted under: the address itself for IPv4, the /64 network for
    /// IPv6, since a household or a phone holds a whole /64.
    /// </summary>
    public static string AddressKey(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }

    private static async Task<Results<Created<DeviceCredentialDto>, Ok<DeviceCredentialDto>, ProblemHttpResult>> RegisterAsync(
        RegisterDeviceKeyRequest? request, DeviceAuthService service, CancellationToken cancellationToken)
    {
        var registration = await service.RegisterAsync(request?.PublicKey, cancellationToken);
        return registration.Outcome switch
        {
            DeviceRegistrationOutcome.Created => TypedResults.Created(
                $"/api/auth/device/credentials/{registration.Credential!.CredentialId}", registration.Credential),
            DeviceRegistrationOutcome.AlreadyRegistered => TypedResults.Ok(registration.Credential!),
            DeviceRegistrationOutcome.KeyRetired => ApiProblems.WithCode(
                StatusCodes.Status409Conflict, "Key retired", "This key was retired and cannot register again. Create a new key.", KeyRetiredCode),
            _ => ApiProblems.WithCode(
                StatusCodes.Status400BadRequest, "Key invalid",
                "Send publicKey as the base64url SubjectPublicKeyInfo of an ECDSA P-256 public key.", KeyInvalidCode),
        };
    }

    private static async Task<Ok<DeviceChallengeDto>> ChallengeAsync(DeviceAuthService service, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await service.IssueChallengeAsync(cancellationToken));
    }

    private static async Task<Results<Ok<DeviceTokenDto>, ProblemHttpResult>> TokenAsync(
        DeviceTokenRequest? request, DeviceAuthService service, HttpResponse response, CancellationToken cancellationToken)
    {
        response.Headers.CacheControl = "no-store";
        var token = request is null ? null : await service.ExchangeAsync(request, cancellationToken);
        return token is null
            ? ApiProblems.WithCode(
                StatusCodes.Status401Unauthorized, "Token refused", "The challenge, the credential or the signature is not accepted.", RefusedCode)
            : TypedResults.Ok(new DeviceTokenDto(token.AccessToken, "Bearer", token.ExpiresAt));
    }
}
