using Cichlids.Domain.Archive;
using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Persistence;

/// <summary>
/// The single EF Core model for the application schema, shared by API write paths and the
/// legacy ETL. Table and column mapping follows the accepted relational domain schema; every
/// mapping detail beyond naming lives in the Configurations classes applied here.
/// </summary>
public class CichlidsDbContext : DbContext
{
    public CichlidsDbContext(DbContextOptions<CichlidsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ProfileIdentity> ProfileIdentities => Set<ProfileIdentity>();
    public DbSet<ReleasedHandle> ReleasedHandles => Set<ReleasedHandle>();
    public DbSet<Species> Species => Set<Species>();
    public DbSet<SpeciesCommonName> SpeciesCommonNames => Set<SpeciesCommonName>();
    public DbSet<SpeciesLink> SpeciesLinks => Set<SpeciesLink>();
    public DbSet<CareEntry> CareEntries => Set<CareEntry>();
    public DbSet<CareMeasurementValue> CareMeasurementValues => Set<CareMeasurementValue>();
    public DbSet<VideoJob> VideoJobs => Set<VideoJob>();
    public DbSet<VideoUpload> VideoUploads => Set<VideoUpload>();
    public DbSet<CareTodo> CareTodos => Set<CareTodo>();
    public DbSet<WaterChangeSession> WaterChangeSessions => Set<WaterChangeSession>();
    public DbSet<WaterChangeSessionStep> WaterChangeSessionSteps => Set<WaterChangeSessionStep>();
    public DbSet<CarePlanRule> CarePlanRules => Set<CarePlanRule>();
    public DbSet<CarePlanRuleChange> CarePlanRuleChanges => Set<CarePlanRuleChange>();
    public DbSet<Vacation> Vacations => Set<Vacation>();
    public DbSet<StreakFreeze> StreakFreezes => Set<StreakFreeze>();
    public DbSet<MilestoneDismissal> MilestoneDismissals => Set<MilestoneDismissal>();
    public DbSet<TankDecommissioning> TankDecommissionings => Set<TankDecommissioning>();
    public DbSet<Tank> Tanks => Set<Tank>();
    public DbSet<Inhabitant> Inhabitants => Set<Inhabitant>();
    public DbSet<TankVisibilityConsent> TankVisibilityConsents => Set<TankVisibilityConsent>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<MediaVariant> MediaVariants => Set<MediaVariant>();
    public DbSet<MediaLink> MediaLinks => Set<MediaLink>();
    public DbSet<UsedChecksum> UsedChecksums => Set<UsedChecksum>();
    public DbSet<Story> Stories => Set<Story>();
    public DbSet<StorySubject> StorySubjects => Set<StorySubject>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<CommentVote> CommentVotes => Set<CommentVote>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionEntry> CollectionEntries => Set<CollectionEntry>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<TankSelectedMember> TankSelectedMembers => Set<TankSelectedMember>();
    public DbSet<SlugAlias> SlugAliases => Set<SlugAlias>();
    public DbSet<DiscussionThread> DiscussionThreads => Set<DiscussionThread>();
    public DbSet<DiscussionPost> DiscussionPosts => Set<DiscussionPost>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<DeviceCredential> DeviceCredentials => Set<DeviceCredential>();
    public DbSet<DeviceChallenge> DeviceChallenges => Set<DeviceChallenge>();
    public DbSet<WebSession> WebSessions => Set<WebSession>();
    public DbSet<WebPushSubscription> WebPushSubscriptions => Set<WebPushSubscription>();
    public DbSet<CareReminderSnooze> CareReminderSnoozes => Set<CareReminderSnooze>();
    public DbSet<WebPushReminderSent> WebPushRemindersSent => Set<WebPushReminderSent>();

    /// <summary>
    /// Vaulted legacy comments that could not be migrated into the domain model. Mapped into a
    /// separate "archive" schema so it stays visibly outside the domain model it accompanies.
    /// </summary>
    public DbSet<LegacyComment> LegacyComments => Set<LegacyComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CichlidsDbContext).Assembly);
    }
}
