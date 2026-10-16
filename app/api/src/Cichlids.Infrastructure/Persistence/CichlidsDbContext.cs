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
    public DbSet<TankDecommissioning> TankDecommissionings => Set<TankDecommissioning>();
    public DbSet<Tank> Tanks => Set<Tank>();
    public DbSet<Inhabitant> Inhabitants => Set<Inhabitant>();
    public DbSet<TankVisibilityConsent> TankVisibilityConsents => Set<TankVisibilityConsent>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<MediaVariant> MediaVariants => Set<MediaVariant>();
    public DbSet<MediaLink> MediaLinks => Set<MediaLink>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostSpecies> PostSpecies => Set<PostSpecies>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<CommentVote> CommentVotes => Set<CommentVote>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionEntry> CollectionEntries => Set<CollectionEntry>();
    public DbSet<Follow> Follows => Set<Follow>();
    public DbSet<SlugAlias> SlugAliases => Set<SlugAlias>();
    public DbSet<DiscussionThread> DiscussionThreads => Set<DiscussionThread>();
    public DbSet<DiscussionPost> DiscussionPosts => Set<DiscussionPost>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebSession> WebSessions => Set<WebSession>();
    public DbSet<WebPushSubscription> WebPushSubscriptions => Set<WebPushSubscription>();
    public DbSet<CareReminderSnooze> CareReminderSnoozes => Set<CareReminderSnooze>();
    public DbSet<WebPushReminderSent> WebPushRemindersSent => Set<WebPushReminderSent>();

    /// <summary>
    /// Vaulted legacy comments that could not be migrated into the domain model. Mapped into a
    /// separate "archive" schema so it stays visibly outside the domain model it accompanies.
    /// </summary>
    public DbSet<LegacyComment> LegacyComments => Set<LegacyComment>();

    /// <summary>
    /// Saves the changes. A media link that is added or removed can alter whether anonymous viewers see its
    /// media item, so the save marks the item for placement in the same transaction as the link change.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var stubs = MarkPlacementOfLinkChanges();
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        finally
        {
            DetachStubs(stubs);
        }
    }

    /// <summary>
    /// Saves the changes asynchronously and marks the media items of added and removed media links for
    /// placement in the same transaction.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var stubs = MarkPlacementOfLinkChanges();
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        finally
        {
            DetachStubs(stubs);
        }
    }

    // A media item that is not tracked is attached as a stub that carries only the placement mark, so the
    // save writes nothing else of it.
    private List<MediaItem> MarkPlacementOfLinkChanges()
    {
        ChangeTracker.DetectChanges();
        var changedLinks = ChangeTracker.Entries<MediaLink>().Where(e => e.State is EntityState.Added or EntityState.Deleted).ToList();
        if (changedLinks.Count == 0)
        {
            return [];
        }

        var tracked = ChangeTracker.Entries<MediaItem>().Where(e => e.Entity.Id != 0).ToDictionary(e => e.Entity.Id, e => e);
        var stubs = new List<MediaItem>();
        var marked = new HashSet<long>();
        foreach (var link in changedLinks)
        {
            var item = link.Reference(l => l.MediaItem).CurrentValue;
            if (item is not null)
            {
                Mark(item);
            }
            else if (link.Entity.MediaItemId != 0 && marked.Add(link.Entity.MediaItemId))
            {
                if (tracked.TryGetValue(link.Entity.MediaItemId, out var entry))
                {
                    if (entry.State != EntityState.Deleted)
                    {
                        Mark(entry.Entity);
                    }
                }
                else
                {
                    var stub = new MediaItem { Id = link.Entity.MediaItemId };
                    MediaItems.Attach(stub);
                    Mark(stub);
                    stubs.Add(stub);
                }
            }
        }

        return stubs;

        void Mark(MediaItem item)
        {
            var entry = Entry(item);
            if (entry.State == EntityState.Deleted)
            {
                return;
            }

            item.PlacementPending = true;
            item.PlacementAttempts = 0;
            item.PlacementNextAttemptAt = null;
            if (entry.State is EntityState.Unchanged or EntityState.Modified)
            {
                entry.Property(i => i.PlacementPending).IsModified = true;
                entry.Property(i => i.PlacementAttempts).IsModified = true;
                entry.Property(i => i.PlacementNextAttemptAt).IsModified = true;
            }
        }
    }

    private void DetachStubs(List<MediaItem> stubs)
    {
        foreach (var stub in stubs)
        {
            Entry(stub).State = EntityState.Detached;
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CichlidsDbContext).Assembly);
    }
}
