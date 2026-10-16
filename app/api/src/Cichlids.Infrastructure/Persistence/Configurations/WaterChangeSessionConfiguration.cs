using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class WaterChangeSessionConfiguration : IEntityTypeConfiguration<WaterChangeSession>
{
    public void Configure(EntityTypeBuilder<WaterChangeSession> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
        // One open session per tank.
        builder.HasIndex(x => x.TankId).IsUnique().HasDatabaseName("ux_water_change_session_open").HasFilter("finished_at IS NULL");
        builder.HasIndex(x => new { x.TankId, x.FinishedAt });
        builder.ToTable("water_change_session", table =>
        {
            table.HasCheckConstraint("ck_water_change_session_finish", """
                (finished_at IS NULL AND duration_seconds IS NULL)
                OR (finished_at IS NOT NULL AND duration_seconds IS NOT NULL AND duration_seconds >= 0)
                """);
        });
    }
}

public sealed class WaterChangeSessionStepConfiguration : IEntityTypeConfiguration<WaterChangeSessionStep>
{
    public void Configure(EntityTypeBuilder<WaterChangeSessionStep> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Step).HasConversion<SnakeCaseEnumConverter<WaterChangeStep>>();
        builder.Property(x => x.State).HasConversion<SnakeCaseEnumConverter<WaterChangeStepState>>();
        builder.HasIndex(x => new { x.SessionId, x.Step }).IsUnique();
        builder.ToTable("water_change_session_step", table =>
            table.HasCheckConstraint("ck_water_change_session_step_state", "state IN ('done', 'skipped')"));
    }
}
