using Cichlids.Etl.Identity;

namespace Cichlids.Etl.Tests;

public sealed class Auth0ExportReaderTests
{
    [Fact]
    public void ParsesAPasswordRecord()
    {
        var ndjson = """{"Id":"auth0|5ab6123456789","Email":"member@example.com","Email Verified":true,"Connection":"Username-Password-Authentication","Name":"member@example.com"}""";

        var records = Auth0ExportReader.Read(new StringReader(ndjson));

        var record = Assert.Single(records);
        Assert.Equal("auth0|5ab6123456789", record.Id);
        Assert.Equal("member@example.com", record.Email);
        Assert.True(record.EmailVerified);
        Assert.Equal("Username-Password-Authentication", record.Connection);
    }

    [Fact]
    public void SkipsBlankLinesBetweenRecords()
    {
        var ndjson = """
            {"Id":"auth0|1","Email":"one@example.com","Email Verified":false,"Connection":"Username-Password-Authentication"}

            {"Id":"auth0|2","Email":"two@example.com","Email Verified":false,"Connection":"Username-Password-Authentication"}
            """;

        var records = Auth0ExportReader.Read(new StringReader(ndjson));

        Assert.Equal(2, records.Count);
        Assert.Equal(["auth0|1", "auth0|2"], records.Select(r => r.Id));
    }

    [Fact]
    public void TreatsAMissingEmailFieldAsNull()
    {
        var ndjson = """{"Id":"facebook|1234","Email Verified":false,"Connection":"facebook"}""";

        var record = Assert.Single(Auth0ExportReader.Read(new StringReader(ndjson)));

        Assert.Null(record.Email);
    }

    [Fact]
    public void TreatsAnEmptyEmailFieldAsNull()
    {
        var ndjson = """{"Id":"facebook|1234","Email":"","Email Verified":false,"Connection":"facebook"}""";

        var record = Assert.Single(Auth0ExportReader.Read(new StringReader(ndjson)));

        Assert.Null(record.Email);
    }
}
