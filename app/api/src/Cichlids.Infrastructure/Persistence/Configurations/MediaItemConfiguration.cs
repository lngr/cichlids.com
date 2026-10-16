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
        builder.Property(x => x.CapturedInApp).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.ReceivedAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.PlacedPublic).HasDefaultValue(true).ValueGeneratedNever().IsRequired();
        builder.Property(x => x.PlacementPending).HasDefaultValue(false).ValueGeneratedNever().IsRequired();
        builder.Property(x => x.PlacementAttempts).HasDefaultValue(0).ValueGeneratedNever().IsRequired();
        builder.Property(x => x.TakenAtLocal).HasColumnType("timestamp without time zone");
        builder.Ignore(x => x.TakenAt);

        builder.HasIndex(x => x.LegacyId).IsUnique();
        builder.HasIndex(x => x.StorageKey).IsUnique();
        builder.HasIndex(x => x.PlacementNextAttemptAt).HasFilter("placement_pending").HasDatabaseName("ix_media_item_placement_due");

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.OwnerProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("media_item", tb =>
        {
            tb.HasCheckConstraint("ck_media_item_kind", "kind IN ('photo', 'video')");
            tb.HasCheckConstraint(
                "ck_media_item_taken_at_offset",
                "taken_at_offset_minutes IS NULL OR (taken_at_local IS NOT NULL AND taken_at_offset_minutes BETWEEN -840 AND 840)");
        });
    }
}
