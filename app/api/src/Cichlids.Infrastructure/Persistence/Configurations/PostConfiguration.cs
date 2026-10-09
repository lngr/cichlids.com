using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).HasConversion<SnakeCaseEnumConverter<PostKind>>().IsRequired();
        builder.Property(x => x.Topic).HasConversion<SnakeCaseEnumConverter<PostTopic>>().IsRequired();
        builder.Property(x => x.State).HasConversion<SnakeCaseEnumConverter<PostState>>().IsRequired();
        builder.Property(x => x.ViewCount).IsRequired().HasDefaultValue(0L);
        builder.Property(x => x.RatingCount).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.LegacyId).IsUnique();

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.AuthorProfileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Tank>()
            .WithMany()
            .HasForeignKey(x => x.TankId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.DeletedByProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("post", tb =>
        {
            tb.HasCheckConstraint(
                "ck_post_kind",
                "kind IN ('single', 'story')");
            tb.HasCheckConstraint(
                "ck_post_topic",
                "topic IN ('cichlids', 'tanks', 'offtopic', 'contest', 'unknown')");
            tb.HasCheckConstraint(
                "ck_post_state",
                "state IN ('draft', 'published', 'archived')");
        });
    }
}
