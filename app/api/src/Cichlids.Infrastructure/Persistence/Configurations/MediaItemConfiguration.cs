using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class MediaItemConfiguration : IEntityTypeConfiguration<MediaItem>
{
    public void Configure(EntityTypeBuilder<MediaItem> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Kind).HasConversion<SnakeCaseEnumConverter<MediaKind>>().IsRequired();
        builder.Property(x => x.StorageKey).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.LegacyId).IsUnique();
        builder.HasIndex(x => x.StorageKey).IsUnique();

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.OwnerProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("media_item", tb => tb.HasCheckConstraint(
            "ck_media_item_kind",
            "kind IN ('photo', 'video')"));
    }
}
