using System.Text.Json;

namespace Cichlids.Api.Features.Common;

/// <summary>
/// JSON naming policy for enum member names, so enum values serialize to the same lower
/// snake_case tokens accepted by query string filters and stored in the database.
/// </summary>
public sealed class SnakeCaseJsonNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name) => SnakeCaseEnum.ToSnakeCase(name);
}
