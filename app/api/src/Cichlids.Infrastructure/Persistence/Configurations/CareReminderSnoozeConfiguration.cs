using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class CareReminderSnoozeConfiguration : IEntityTypeConfiguration<CareReminderSnooze>
{
    public void Configure(EntityTypeBuilder<CareReminderSnooze> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<SnakeCaseEnumConverter<CareKind>>();
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.ProfileId, x.TankId, x.Kind, x.DueDate }).IsUnique();
        builder.ToTable("care_reminder_snooze", table =>
            table.HasCheckConstraint("ck_care_reminder_snooze_kind", "kind IN ('water_change', 'measurement')"));
    }
}
