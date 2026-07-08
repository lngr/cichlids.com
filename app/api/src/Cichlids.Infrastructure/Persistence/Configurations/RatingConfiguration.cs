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

        builder.HasOne<Post>()
            .WithMany()
            .HasForeignKey(x => x.PostId)
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
                "(post_id IS NULL) <> (tank_id IS NULL)");
            tb.HasCheckConstraint(
                "ck_rating_stars_range",
                "stars BETWEEN 1 AND 5");
        });
    }
}
