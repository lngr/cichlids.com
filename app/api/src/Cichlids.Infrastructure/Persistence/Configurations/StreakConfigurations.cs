using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class VacationConfiguration : IEntityTypeConfiguration<Vacation>
{
    public void Configure(EntityTypeBuilder<Vacation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ProfileId, x.StartDate });
        // One active vacation per member.
        builder.HasIndex(x => x.ProfileId).IsUnique().HasDatabaseName("ux_vacation_active").HasFilter("end_date IS NULL");
        builder.ToTable("vacation", table =>
            table.HasCheckConstraint("ck_vacation_dates", "end_date IS NULL OR end_date >= start_date"));
    }
}

public sealed class StreakFreezeConfiguration : IEntityTypeConfiguration<StreakFreeze>
{
    public void Configure(EntityTypeBuilder<StreakFreeze> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CareEntry>().WithMany().HasForeignKey(x => x.CareEntryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.CareEntryId).IsUnique();
        builder.HasIndex(x => new { x.TankId, x.TankYear });
        builder.ToTable("streak_freeze", table =>
        {
            table.HasCheckConstraint("ck_streak_freeze_period", "period_end >= period_start");
            table.HasCheckConstraint("ck_streak_freeze_tank_year", "tank_year >= 1");
        });
    }
}

public sealed class CarePlanRuleChangeConfiguration : IEntityTypeConfiguration<CarePlanRuleChange>
{
    public void Configure(EntityTypeBuilder<CarePlanRuleChange> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<SnakeCaseEnumConverter<CareKind>>();
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TankId, x.Kind, x.At });
        builder.ToTable("care_plan_rule_change", table =>
        {
            table.HasCheckConstraint("ck_care_plan_rule_change_kind", "kind IN ('water_change', 'measurement')");
            table.HasCheckConstraint("ck_care_plan_rule_change_rhythm", "rhythm_days BETWEEN 1 AND 90");
        });
    }
}

public sealed class TankDecommissioningConfiguration : IEntityTypeConfiguration<TankDecommissioning>
{
    public void Configure(EntityTypeBuilder<TankDecommissioning> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Scope).HasConversion<SnakeCaseEnumConverter<DecommissionScope>>().IsRequired();
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(x => x.StartMediaItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(x => x.EndMediaItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.TankId, x.StartedAt });
        // One active decommissioning per tank.
        builder.HasIndex(x => x.TankId).IsUnique().HasDatabaseName("ux_tank_decommissioning_active").HasFilter("ended_at IS NULL");
        builder.ToTable("tank_decommissioning", table =>
        {
            table.HasCheckConstraint("ck_tank_decommissioning_scope", "scope IN ('all_care', 'water_change')");
            table.HasCheckConstraint("ck_tank_decommissioning_end",
                "(ended_at IS NULL AND end_date IS NULL AND end_media_item_id IS NULL) "
                + "OR (ended_at IS NOT NULL AND end_date IS NOT NULL AND end_media_item_id IS NOT NULL AND end_date >= start_date)");
        });
    }
}
