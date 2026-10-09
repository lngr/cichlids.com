using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class DiscussionPostConfiguration : IEntityTypeConfiguration<DiscussionPost>
{
    public void Configure(EntityTypeBuilder<DiscussionPost> builder)
    {
        builder.ToTable("discussion_post");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Body).IsRequired();
        builder.Property(x => x.Sort).IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.LegacyId).IsUnique();

        builder.HasOne<DiscussionThread>()
            .WithMany(x => x.Posts)
            .HasForeignKey(x => x.ThreadId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.AuthorProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
