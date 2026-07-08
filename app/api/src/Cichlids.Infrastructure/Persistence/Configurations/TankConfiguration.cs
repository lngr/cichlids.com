using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class TankConfiguration : IEntityTypeConfiguration<Tank>
{
    public void Configure(EntityTypeBuilder<Tank> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).IsRequired();
        builder.Property(x => x.Category).HasConversion<SnakeCaseEnumConverter<TankCategory>>();
        builder.Property(x => x.DimensionUnit).HasConversion<SnakeCaseEnumConverter<DimensionUnit>>();
        builder.Property(x => x.State).HasConversion<SnakeCaseEnumConverter<TankState>>().IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.LegacyId).IsUnique();

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.ProfileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<MediaItem>()
            .WithMany()
            .HasForeignKey(x => x.MainMediaId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => x.DeletedByProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable("tank", tb =>
        {
            tb.HasCheckConstraint(
                "ck_tank_category",
                "category IS NULL OR category IN ('tanganyika', 'malawi', 'american', 'african', 'community', 'central_american', 'south_american')");
            tb.HasCheckConstraint(
                "ck_tank_dimension_unit",
                "dimension_unit IS NULL OR dimension_unit IN ('cm', 'inch')");
            tb.HasCheckConstraint(
                "ck_tank_state",
                "state IN ('draft', 'published')");
        });
    }
}
