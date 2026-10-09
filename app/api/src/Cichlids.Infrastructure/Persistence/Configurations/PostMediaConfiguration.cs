using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class PostMediaConfiguration : IEntityTypeConfiguration<PostMedia>
{
    public void Configure(EntityTypeBuilder<PostMedia> builder)
    {
        builder.ToTable("post_media");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Sort).IsRequired().HasDefaultValue(0);

        builder.HasIndex(x => new { x.PostId, x.MediaItemId }).IsUnique();

        builder.HasOne<Post>()
            .WithMany(x => x.Media)
            .HasForeignKey(x => x.PostId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<MediaItem>()
            .WithMany()
            .HasForeignKey(x => x.MediaItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
