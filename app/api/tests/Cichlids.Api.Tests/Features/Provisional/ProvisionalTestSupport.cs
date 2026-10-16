using System.Net;
using Cichlids.Api.Tests.Features.Auth.Devices;
using static Cichlids.Api.Tests.Features.Care.CareTestSupport;

namespace Cichlids.Api.Tests.Features.Provisional;

/// <summary>
/// An app installation that registered its key and holds a device token.
/// </summary>
internal sealed record ProvisionalCaller(TestDevice Device, string Token, long ProfileId) : IDisposable
{
    public void Dispose() => Device.Dispose();
}

/// <summary>
/// Provisional callers and the request helpers shared by the tests of their rights.
/// </summary>
internal static class ProvisionalTestSupport
{
    public const string LocalDate = "2026-10-15";

    public static async Task<ProvisionalCaller> NewAsync(HttpClient client)
    {
        var device = new TestDevice(client);
        await device.RegisterAsync();
        return new ProvisionalCaller(device, await device.TokenAsync(), device.ProfileId);
    }

    public static async Task<long> CreateTankAsync(HttpClient client, ProvisionalCaller caller, object? body = null)
    {
        using var response = await SendAsync(client, HttpMethod.Post, "/api/tanks", caller.Token, body ?? new { title = "Provisional tank" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await BodyAsync(response)).GetProperty("id").GetInt64();
    }

    public static Task AssertRegistrationRequiredAsync(HttpResponseMessage response) =>
        AssertProblemAsync(response, HttpStatusCode.Forbidden, "registration_required");
}
