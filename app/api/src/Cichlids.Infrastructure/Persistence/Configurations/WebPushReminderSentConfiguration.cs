using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class WebPushReminderSentConfiguration : IEntityTypeConfiguration<WebPushReminderSent>
{
    public void Configure(EntityTypeBuilder<WebPushReminderSent> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasConversion<SnakeCaseEnumConverter<CareKind>>();
        builder.HasOne<WebPushSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.SubscriptionId, x.TankId, x.Kind, x.DueDate, x.FireDate }).IsUnique();
        builder.HasIndex(x => x.FireDate);
        builder.ToTable("web_push_reminder_sent", table =>
            table.HasCheckConstraint("ck_web_push_reminder_sent_kind", "kind IN ('water_change', 'measurement')"));
    }
}
