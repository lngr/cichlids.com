using Cichlids.Etl.Runtime;

namespace Cichlids.Etl.Tests;

[Collection(VerifyEtlCollection.Name)]
public sealed class VerifyStepTests(VerifyEtlFixture fixture)
{
    [Fact]
    public async Task ReportsNoMismatchesAfterAFullPipelineRun()
    {
        Assert.Equal(0, fixture.PipelineExitCode);
        var report = fixture.PipelineReport;
        Assert.True(report.Passed, DescribeMismatches(report));
        Assert.Equal(0, report.MismatchCount);

        AssertRow(report, "species", 2, 2);
        AssertRow(report, "profiles_member", 2, 2);
        AssertInfoRow(report, "profiles_archived", 1);
        AssertInfoRow(report, "profiles_system", 1);
        AssertInfoRow(report, "profiles_member_registered", 0);
        AssertRow(report, "identities_auth0", 1, 1);
        AssertRow(report, "identities_legacy_openid", 1, 1);
        AssertRow(report, "identities_email", 1, 1);
        AssertRow(report, "tanks", 2, 2);
        AssertRow(report, "discussion_thread", 1, 1);
        AssertRow(report, "discussion_post", 2, 2);
        AssertRow(report, "media_originals", 2, 2);
        AssertRow(report, "media_forum_attachments", 1, 1);
        AssertRow(report, "post", 2, 2);
        AssertRow(report, "post_species", 3, 3);
        AssertRow(report, "comment", 3, 3);
        AssertRow(report, "rating", 2, 2);
        AssertRow(report, "vault_legacy_comment", 3, 3);
        AssertRow(report, "comment_vote", 1, 1);
        AssertRow(report, "collection", 1, 1);
        AssertRow(report, "collection_entry", 2, 2);
        AssertRow(report, "post_without_media", 0, 0);
        AssertRow(report, "post_published_without_canonical_slug", 0, 0);
        AssertRow(report, "slug_alias_one_canonical_per_post", 0, 0);
        AssertRow(report, "slug_alias_orphans", 0, 0);
        AssertRow(report, "slug_alias_unique_value", 0, 0);
        AssertRow(report, "post_without_slug_alias", 0, 0);
        AssertRow(report, "post_rating_count_consistency", 0, 0);
        AssertRow(report, "comment_score_consistency", 0, 0);
        AssertRow(report, "discussion_thread_post_count_consistency", 0, 0);
        AssertRow(report, "profile_username_email_like", 0, 0);
        AssertRow(report, "profile_display_name_email_like", 0, 0);
        AssertRow(report, "profile_avatar_gravatar", 0, 0);
        AssertRow(report, "comment_poster_name_email_like", 0, 0);
        AssertRow(report, "discussion_post_poster_name_email_like", 0, 0);
        AssertInfoRow(report, "guest_names_generated", 1);
    }

    [Fact]
    public void EveryRowNamesItsRule()
    {
        Assert.All(fixture.PipelineReport.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Rule), row.Entity));
    }

    [Fact]
    public async Task FlagsAMemberProfileNoLegacyAccountJustifies()
    {
        var report = await fixture.VerifyInRolledBackTransactionAsync(context => VerifyEtlFixture.ExecuteAsync(
            context,
            "INSERT INTO profile (legacy_id, username, display_name, kind, created_at) "
            + "VALUES (999001, 'verify-fixture-rogue-member', 'Rogue Member', 'member', now())"));

        Assert.False(report.Passed);
        AssertMismatch(report, "profiles_member", 2, 3);

        var cleanReport = await fixture.VerifyInRolledBackTransactionAsync();
        Assert.True(cleanReport.Passed, DescribeMismatches(cleanReport));
    }

    [Fact]
    public async Task IgnoresAMemberProfileTheApiCreatedForAFirstLoginWhenCountingMigratedMembers()
    {
        var report = await fixture.VerifyInRolledBackTransactionAsync(context => VerifyEtlFixture.ExecuteAsync(
            context,
            "INSERT INTO profile (username, display_name, kind, created_at) "
            + "VALUES ('verify-fixture-first-login-member', 'First Login Member', 'member', now())"));

        Assert.True(report.Passed, DescribeMismatches(report));
        AssertRow(report, "profiles_member", 2, 2);
        AssertInfoRow(report, "profiles_member_registered", 1);
    }

    [Fact]
    public async Task FlagsAProfileWhoseHandleOrDisplayNameHoldsAnEmailAddress()
    {
        var report = await fixture.VerifyInRolledBackTransactionAsync(context => VerifyEtlFixture.ExecuteAsync(
            context,
            "INSERT INTO profile (legacy_id, username, display_name, kind, created_at) "
            + "VALUES (999003, 'exposed@verify.example', 'Exposed <exposed@verify.example>', 'archived', now())"));

        Assert.False(report.Passed);
        AssertMismatch(report, "profile_username_email_like", 0, 1);
        AssertMismatch(report, "profile_display_name_email_like", 0, 1);
        AssertRow(report, "profiles_member", 2, 2);
    }

    [Fact]
    public async Task FlagsAProfileWhoseExternalAvatarUrlIsAGravatarUrl()
    {
        var report = await fixture.VerifyInRolledBackTransactionAsync(context => VerifyEtlFixture.ExecuteAsync(
            context,
            "UPDATE profile SET external_avatar_url = 'http://gravatar.com/avatar/injected' WHERE legacy_id = 9011"));

        Assert.False(report.Passed);
        AssertMismatch(report, "profile_avatar_gravatar", 0, 1);
    }

    [Fact]
    public async Task FlagsAGuestNameThatHoldsAnEmailAddress()
    {
        await using (var db = VerifyEtlFixture.CreateTargetContext(fixture.PipelineConnectionString))
        {
            Assert.Equal("guest_00001", db.DiscussionPosts.Single(p => p.LegacyId == 98002).PosterName);
        }

        var report = await fixture.VerifyInRolledBackTransactionAsync(async context =>
        {
            await VerifyEtlFixture.ExecuteAsync(
                context, "UPDATE comment SET poster_name = 'Exposed <exposed@verify.example>' WHERE legacy_id = 9303");
            await VerifyEtlFixture.ExecuteAsync(
                context, "UPDATE discussion_post SET poster_name = 'exposed@verify.example' WHERE legacy_id = 98002");
        });

        Assert.False(report.Passed);
        AssertMismatch(report, "comment_poster_name_email_like", 0, 1);
        AssertMismatch(report, "discussion_post_poster_name_email_like", 0, 1);
    }

    private static void AssertMismatch(VerificationReport report, string entity, long expected, long actual)
    {
        var row = Assert.Single(report.Rows, r => r.Entity == entity);
        Assert.Equal(expected, row.Expected);
        Assert.Equal(actual, row.Actual);
        Assert.Equal(VerificationStatus.Mismatch, row.Status);
    }

    private static void AssertRow(VerificationReport report, string entity, long expected, long actual)
    {
        var row = Assert.Single(report.Rows, r => r.Entity == entity);
        Assert.Equal(expected, row.Expected);
        Assert.Equal(actual, row.Actual);
        Assert.Equal(VerificationStatus.Ok, row.Status);
    }

    private static void AssertInfoRow(VerificationReport report, string entity, long value)
    {
        var row = Assert.Single(report.Rows, r => r.Entity == entity);
        Assert.Equal(value, row.Expected);
        Assert.Equal(value, row.Actual);
        Assert.Equal(VerificationStatus.Info, row.Status);
    }

    private static string DescribeMismatches(VerificationReport report) => string.Join(
        "; ",
        report.Rows
            .Where(r => r.Status == VerificationStatus.Mismatch)
            .Select(r => $"{r.Entity}: expected {r.Expected}, actual {r.Actual}"));
}
