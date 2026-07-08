using MySqlConnector;

namespace Cichlids.Etl.Persistence;

/// <summary>
/// Reads legacy TYPO3 text columns, which use an empty string rather than SQL NULL to mean "no
/// value" throughout the schema. These helpers normalize both spellings to a single nullable
/// result so step code does not repeat the same blank check at every column.
/// </summary>
public static class MySqlReaderExtensions
{
    public static string? GetTrimmedOrNull(this MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetString(ordinal).Trim();
        return value.Length == 0 ? null : value;
    }

    public static string GetStringOrEmpty(this MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    public static int? GetNullableInt32(this MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }
}
