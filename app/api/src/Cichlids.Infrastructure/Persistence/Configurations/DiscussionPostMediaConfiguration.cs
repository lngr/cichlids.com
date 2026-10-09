using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class DiscussionPostMediaConfiguration : IEntityTypeConfiguration<DiscussionPostMedia>
{
    public void Configure(EntityTypeBuilder<DiscussionPostMedia> builder)
    {
        builder.ToTable("discussion_post_media");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Sort).IsRequired().HasDefaultValue(0);

        builder.HasIndex(x => new { x.DiscussionPostId, x.MediaItemId }).IsUnique();

        builder.HasOne<DiscussionPost>()
            .WithMany(x => x.Media)
            .HasForeignKey(x => x.DiscussionPostId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<MediaItem>()
            .WithMany()
            .HasForeignKey(x => x.MediaItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
