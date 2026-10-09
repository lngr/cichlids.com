using Cichlids.Domain.Archive;
using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Cichlids.Infrastructure.Persistence;

/// <summary>
/// The single EF Core model for the application schema, shared by API write paths and the
/// legacy ETL. Table and column mapping follows the accepted relational domain schema; every
/// mapping detail beyond naming lives in the <see cref="Configurations"/> classes applied here.
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
    public DbSet<Tank> Tanks => Set<Tank>();
    public DbSet<Inhabitant> Inhabitants => Set<Inhabitant>();
    public DbSet<TankMedia> TankMedia => Set<TankMedia>();
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();
    public DbSet<MediaVariant> MediaVariants => Set<MediaVariant>();
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<PostMedia> PostMedia => Set<PostMedia>();
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
    public DbSet<DiscussionPostMedia> DiscussionPostMedia => Set<DiscussionPostMedia>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();

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
