using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class MediaVariantConfiguration : IEntityTypeConfiguration<MediaVariant>
{
    public void Configure(EntityTypeBuilder<MediaVariant> builder)
    {
        builder.ToTable("media_variant");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Label).IsRequired();
        builder.Property(x => x.StorageKey).IsRequired();

        builder.HasIndex(x => x.StorageKey).IsUnique();
        builder.HasIndex(x => new { x.MediaItemId, x.Label }).IsUnique();

        builder.HasOne<MediaItem>()
            .WithMany(x => x.Variants)
            .HasForeignKey(x => x.MediaItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
