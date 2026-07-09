using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Etl.Runtime;
using Cichlids.Etl.Steps;

namespace Cichlids.Etl.Tests;

[Collection(VerifyEtlCollection.Name)]
public sealed class VerifyStepTests(VerifyEtlFixture fixture)
{
    [Fact]
    public async Task ReportsNoMismatchesAfterAFullPipelineRunAndFlagsAnInjectedDeviation()
    {
        await using var context = await EtlContext.CreateAsync(
            fixture.LegacyConnectionString, fixture.TargetConnectionString, dryRun: false, CancellationToken.None, fixture.ObjectStore);

        foreach (var step in EtlStepRegistry.All)
        {
            await EtlRunner.RunAsync(step, context, CancellationToken.None);
        }

        var report = context.VerificationReport;
        Assert.NotNull(report);
        Assert.True(report!.Passed, DescribeMismatches(report));
        Assert.Equal(0, report.MismatchCount);

        AssertRow(report, "species", 2, 2);
        AssertRow(report, "profiles_member", 2, 2);
        AssertInfoRow(report, "profiles_archived", 1);
        AssertInfoRow(report, "profiles_system", 1);
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

        // Inject a target-only member profile with no legacy counterpart, the same way a bug
        // introducing an extra row (or a re-run losing track of one) would show up: a real
        // discrepancy between what the source justifies and what the target holds.
        await using (var db = fixture.CreateTargetContext())
        {
            db.Profiles.Add(new Profile
            {
                Username = "verify-fixture-rogue-member",
                DisplayName = "Rogue Member",
                Kind = ProfileKind.Member,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        try
        {
            await EtlRunner.RunAsync(new VerifyStep(), context, CancellationToken.None);
            var deviatingReport = context.VerificationReport;

            Assert.NotNull(deviatingReport);
            Assert.False(deviatingReport!.Passed);
            var mismatchRow = Assert.Single(deviatingReport.Rows, r => r.Entity == "profiles_member");
            Assert.Equal(VerificationStatus.Mismatch, mismatchRow.Status);
            Assert.Equal(2, mismatchRow.Expected);
            Assert.Equal(3, mismatchRow.Actual);
            Assert.Equal(1, mismatchRow.Delta);
        }
        finally
        {
            await using var db = fixture.CreateTargetContext();
            var rogue = db.Profiles.Single(p => p.Username == "verify-fixture-rogue-member");
            db.Profiles.Remove(rogue);
            await db.SaveChangesAsync();
        }

        // The injected row is gone again, so a clean re-run passes exactly like the first one.
        await EtlRunner.RunAsync(new VerifyStep(), context, CancellationToken.None);
        Assert.True(context.VerificationReport!.Passed);
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
