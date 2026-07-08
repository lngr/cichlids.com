using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body).IsRequired();
        builder.Property(x => x.Score).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.LegacyId).IsUnique();

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
            .HasForeignKey(x => x.AuthorProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.DeletedByProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("comment", tb =>
        {
            tb.HasCheckConstraint(
                "ck_comment_exactly_one_target",
                "(post_id IS NULL) <> (tank_id IS NULL)");
            tb.HasCheckConstraint(
                "ck_comment_body_not_empty",
                "body <> ''");
        });
    }
}
