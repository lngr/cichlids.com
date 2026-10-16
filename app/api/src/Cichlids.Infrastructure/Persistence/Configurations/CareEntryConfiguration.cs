using Cichlids.Domain;
using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class CareEntryConfiguration : IEntityTypeConfiguration<CareEntry>
{
    public void Configure(EntityTypeBuilder<CareEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<SnakeCaseEnumConverter<CareKind>>();
        builder.Property(x => x.AmountInputUnit).HasConversion<SnakeCaseEnumConverter<CareAmountUnit>>();
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.AuthorProfileId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.OnlyMe).HasDefaultValue(false).ValueGeneratedNever().IsRequired();
        builder.Property(x => x.Evidence).HasConversion<SnakeCaseEnumConverter<WaterChangeEvidenceState>>();
        builder.Property(x => x.EvidenceMissing).HasColumnType("text[]").HasDefaultValueSql("'{}'");
        builder.HasOne<WaterChangeSession>().WithMany().HasForeignKey(x => x.WaterChangeSessionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => x.WaterChangeSessionId).IsUnique().HasFilter("water_change_session_id IS NOT NULL");
        builder.HasMany(x => x.Values).WithOne().HasForeignKey(x => x.CareEntryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Todos).WithOne().HasForeignKey(x => x.CareEntryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(x => new { x.TankId, x.OccurredAt, x.Id }).IsDescending(false, true, true);
        builder.ToTable("care_entry", table =>
        {
            table.HasCheckConstraint("ck_care_kind", "kind IN ('water_change', 'measurement')");
            table.HasCheckConstraint("ck_care_evidence", "evidence IS NULL OR (evidence IN ('none', 'partial', 'verified') AND kind = 'water_change' AND water_change_session_id IS NOT NULL AND duration_seconds >= 0)");
            table.HasCheckConstraint("ck_care_note", "note IS NULL OR char_length(note) BETWEEN 1 AND 2000");
            table.HasCheckConstraint("ck_care_time", "occurred_at <= recorded_at + interval '2 minutes'");
            table.HasCheckConstraint("ck_care_amount", """
                (amount_input_value IS NULL AND amount_input_unit IS NULL AND amount_liters IS NULL)
                OR (amount_input_value IS NOT NULL AND amount_input_value > 0 AND amount_input_value <= 1000000
                    AND amount_input_unit IS NOT NULL AND (
                        (amount_input_unit = 'percent' AND amount_input_value <= 100 AND amount_liters IS NULL)
                        OR (amount_input_unit = 'liter' AND amount_liters IS NOT NULL AND amount_liters = amount_input_value)
                        OR (amount_input_unit = 'us_gallon' AND amount_liters IS NOT NULL AND amount_liters > 0)))
                """);
        });
    }
}
