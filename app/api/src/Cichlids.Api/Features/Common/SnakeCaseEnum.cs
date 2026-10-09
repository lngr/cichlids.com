using System.Text;

namespace Cichlids.Api.Features.Common;

/// <summary>
/// Converts between the PascalCase enum member names used in C# and the lower snake_case tokens
/// used in the database check constraints, JSON payloads and query string filters, so all three
/// surfaces agree on the same spelling for a given enum value.
/// </summary>
public static class SnakeCaseEnum
{
    public static string ToSnakeCase(Enum value) => ToSnakeCase(value.ToString());

    public static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 4);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    public static bool TryParse<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(ToSnakeCase(candidate), value, StringComparison.OrdinalIgnoreCase))
            {
                result = candidate;
                return true;
            }
        }

        return false;
    }
}
