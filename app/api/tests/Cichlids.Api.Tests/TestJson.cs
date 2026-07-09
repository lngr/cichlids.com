using System.Text.Json;
using System.Text.Json.Serialization;
using Cichlids.Api.Features.Common;

namespace Cichlids.Api.Tests;

/// <summary>
/// The JSON options every test uses to deserialize a response body, matching the naming policy
/// the API itself serializes with (camelCase properties, snake_case enum values) so a test never
/// silently deserializes into all-default fields.
/// </summary>
internal static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(new SnakeCaseJsonNamingPolicy()) },
    };
}
