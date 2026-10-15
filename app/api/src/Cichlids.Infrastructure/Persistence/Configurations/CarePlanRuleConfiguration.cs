using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class CarePlanRuleConfiguration : IEntityTypeConfiguration<CarePlanRule>
{
    public void Configure(EntityTypeBuilder<CarePlanRule> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<SnakeCaseEnumConverter<CareKind>>();
        builder.Property(x => x.ReasonCode).IsRequired();
        builder.Property(x => x.ReasonParametersJson).HasColumnType("jsonb");
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TankId, x.Kind }).IsUnique();
        builder.ToTable("care_plan_rule", table =>
        {
            table.HasCheckConstraint("ck_care_plan_kind", "kind IN ('water_change', 'measurement')");
            table.HasCheckConstraint("ck_care_plan_rhythm", "rhythm_days BETWEEN 1 AND 90");
            table.HasCheckConstraint("ck_care_plan_reason", "reason_code ~ '^[a-z][a-z0-9_]{0,63}$'");
            table.HasCheckConstraint("ck_care_plan_reason_parameters",
                "reason_parameters_json IS NULL OR jsonb_typeof(reason_parameters_json) = 'object'");
        });
    }
}
