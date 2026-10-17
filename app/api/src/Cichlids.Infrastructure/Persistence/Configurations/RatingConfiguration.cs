using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class RatingConfiguration : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Stars).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.LegacyCommentId).IsUnique();

        // A comment has at most one rating. A hard delete of the comment keeps the rating without a comment.
        builder.HasIndex(x => x.CommentId).IsUnique();
        builder.HasOne<Comment>()
            .WithMany()
            .HasForeignKey(x => x.CommentId)
            .OnDelete(DeleteBehavior.SetNull);

        // A member has one rating per target. Anonymous ratings have no profile and stay unconstrained.
        builder.HasIndex(x => new { x.ProfileId, x.StoryId })
            .IsUnique()
            .HasDatabaseName("ux_rating_profile_story")
            .HasFilter("profile_id IS NOT NULL AND story_id IS NOT NULL");
        builder.HasIndex(x => new { x.ProfileId, x.TankId })
            .IsUnique()
            .HasDatabaseName("ux_rating_profile_tank")
            .HasFilter("profile_id IS NOT NULL AND tank_id IS NOT NULL");

        builder.HasOne<Story>()
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Tank>()
            .WithMany()
            .HasForeignKey(x => x.TankId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.ProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("rating", tb =>
        {
            tb.HasCheckConstraint(
                "ck_rating_exactly_one_target",
                "(story_id IS NULL) <> (tank_id IS NULL)");
            tb.HasCheckConstraint(
                "ck_rating_stars_range",
                "stars BETWEEN 1 AND 5");
        });
    }
}
