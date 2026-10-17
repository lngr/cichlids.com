using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class InhabitantEventConfiguration : IEntityTypeConfiguration<InhabitantEvent>
{
    /// <summary>
    /// The longest length of a fish in centimetres that an event accepts.
    /// </summary>
    public const decimal MaxLengthCm = 300m;

    /// <summary>
    /// The highest generation number of the offspring of a spawning.
    /// </summary>
    public const int MaxGeneration = 99;

    public void Configure(EntityTypeBuilder<InhabitantEvent> builder)
    {
        builder.ToTable("inhabitant_event", table =>
        {
            table.HasCheckConstraint("ck_inhabitant_event_kind", "kind IN ('growth', 'spawning', 'moved', 'died')");
            table.HasCheckConstraint("ck_inhabitant_event_growth", $$"""
                (kind = 'growth') = (length_cm IS NOT NULL AND length_input_value IS NOT NULL AND length_input_unit IS NOT NULL)
                AND (length_cm IS NULL OR (length_cm > 0 AND length_cm <= {{MaxLengthCm}}))
                AND (length_input_unit IS NULL OR length_input_unit IN ('cm', 'inch'))
                AND (length_input_value IS NULL OR length_input_value > 0)
                """);
            table.HasCheckConstraint("ck_inhabitant_event_spawning", $$"""
                (kind = 'spawning') = (offspring_hybrid IS NOT NULL)
                AND (kind <> 'spawning' OR (offspring_hybrid AND offspring_generation IS NULL)
                    OR (NOT offspring_hybrid AND offspring_generation BETWEEN 1 AND {{MaxGeneration}}))
                AND (kind = 'spawning' OR offspring_generation IS NULL)
                """);
            table.HasCheckConstraint("ck_inhabitant_event_move",
                "(kind = 'moved') = (from_tank_id IS NOT NULL AND to_tank_id IS NOT NULL) AND (from_tank_id IS NULL OR from_tank_id <> to_tank_id)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<SnakeCaseEnumConverter<InhabitantEventKind>>().IsRequired();
        builder.Property(x => x.LengthCm).HasColumnType("numeric(7,3)");
        builder.Property(x => x.LengthInputValue).HasColumnType("numeric(9,4)");
        builder.Property(x => x.LengthInputUnit).HasConversion<SnakeCaseEnumConverter<DimensionUnit>>();
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");

        // The events belong to the fish and survive its moves between tanks.
        builder.HasOne<Inhabitant>().WithMany().HasForeignKey(x => x.InhabitantId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.FromTankId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.ToTankId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.InhabitantId, x.OccurredOn });
        // A fish dies once.
        builder.HasIndex(x => x.InhabitantId).IsUnique().HasFilter("kind = 'died'").HasDatabaseName("ux_inhabitant_event_died");
        builder.HasIndex(x => x.FromTankId);
        builder.HasIndex(x => x.ToTankId);
    }
}
