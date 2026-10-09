using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cichlids.Etl.Identity;

/// <summary>
/// Reads an Auth0 export in newline-delimited JSON, one member object per line, into
/// Auth0ExportRecord values.
/// </summary>
public static class Auth0ExportReader
{
    /// <summary>
    /// Reads every non-blank line from the given reader as one Auth0 export record. The reader is
    /// consumed to its end; the caller owns disposing it.
    /// </summary>
    public static IReadOnlyList<Auth0ExportRecord> Read(TextReader reader)
    {
        var records = new List<Auth0ExportRecord>();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parsed = JsonSerializer.Deserialize<ExportLine>(line)
                ?? throw new InvalidDataException("Auth0 export line deserialized to null.");

            records.Add(new Auth0ExportRecord(
                parsed.Id ?? string.Empty,
                string.IsNullOrEmpty(parsed.Email) ? null : parsed.Email,
                parsed.EmailVerified,
                parsed.Connection ?? string.Empty));
        }

        return records;
    }

    /// <summary>
    /// Reads an Auth0 export file at the given path.
    /// </summary>
    public static IReadOnlyList<Auth0ExportRecord> ReadFile(string path)
    {
        using var reader = new StreamReader(path);
        return Read(reader);
    }

    // Field names match the Auth0 export exactly, including the spaces in "Email Verified"; the
    // export carries further fields (Name, Nickname, Given Name, Family Name, Picture, Created
    // At, Updated At) that this reader has no use for and that System.Text.Json ignores by
    // default.
    private sealed record ExportLine(
        [property: JsonPropertyName("Id")] string? Id,
        [property: JsonPropertyName("Email")] string? Email,
        [property: JsonPropertyName("Email Verified")] bool EmailVerified,
        [property: JsonPropertyName("Connection")] string? Connection);
}
