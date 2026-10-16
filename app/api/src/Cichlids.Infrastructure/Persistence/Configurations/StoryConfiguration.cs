using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class StoryConfiguration : IEntityTypeConfiguration<Story>
{
    public void Configure(EntityTypeBuilder<Story> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Topic).HasConversion<SnakeCaseEnumConverter<StoryTopic>>().IsRequired();
        builder.Property(x => x.State).HasConversion<SnakeCaseEnumConverter<StoryState>>().IsRequired();
        builder.Property(x => x.ViewCount).IsRequired().HasDefaultValue(0L);
        builder.Property(x => x.RatingCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.LegacyId).IsUnique();

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.AuthorProfileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.DeletedByProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        // The cover is one of the item links of the story. Removing that link leaves the story with the
        // first item as its cover.
        builder.HasOne<MediaLink>()
            .WithMany()
            .HasForeignKey(x => x.CoverMediaLinkId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.ToTable("story", tb =>
        {
            tb.HasCheckConstraint(
                "ck_story_topic",
                "topic IN ('cichlids', 'tanks', 'offtopic', 'contest', 'unknown')");
            tb.HasCheckConstraint(
                "ck_story_state",
                "state IN ('draft', 'published', 'archived')");
        });
    }
}
