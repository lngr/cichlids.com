using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Cichlids.Infrastructure.Persistence.Conversions;

/// <summary>
/// Converts an enum to and from the lower snake_case text stored in its column, so enum values
/// stay self-describing in SQL tooling independent of the C# member names.
/// </summary>
public sealed class SnakeCaseEnumConverter<TEnum> : ValueConverter<TEnum, string>
    where TEnum : struct, Enum
{
    public SnakeCaseEnumConverter()
        : base(value => ToSnakeCase(value), value => FromSnakeCase(value))
    {
    }

    private static string ToSnakeCase(TEnum value)
    {
        var name = value.ToString();
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

    private static TEnum FromSnakeCase(string value)
    {
        var pascal = string.Concat(value.Split('_').Select(part =>
            part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));

        return Enum.Parse<TEnum>(pascal);
    }
}
