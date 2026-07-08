using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class TankMediaConfiguration : IEntityTypeConfiguration<TankMedia>
{
    public void Configure(EntityTypeBuilder<TankMedia> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Section).HasConversion<SnakeCaseEnumConverter<TankMediaSection>>().IsRequired();
        builder.Property(x => x.Sort).IsRequired().HasDefaultValue(0);

        builder.HasIndex(x => new { x.TankId, x.MediaItemId, x.Section }).IsUnique();

        builder.HasOne<Tank>()
            .WithMany(x => x.Media)
            .HasForeignKey(x => x.TankId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<MediaItem>()
            .WithMany()
            .HasForeignKey(x => x.MediaItemId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.ToTable("tank_media", tb => tb.HasCheckConstraint(
            "ck_tank_media_section",
            "section IN ('showcase', 'decoration', 'technic')"));
    }
}
