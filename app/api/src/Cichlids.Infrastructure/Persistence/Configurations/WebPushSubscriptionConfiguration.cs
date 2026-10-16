using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class WebPushSubscriptionConfiguration : IEntityTypeConfiguration<WebPushSubscription>
{
    public void Configure(EntityTypeBuilder<WebPushSubscription> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Endpoint).IsRequired();
        builder.Property(x => x.P256dh).IsRequired();
        builder.Property(x => x.Auth).IsRequired();
        builder.Property(x => x.TimeZone).IsRequired().HasMaxLength(64);
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.Endpoint).IsUnique();
        builder.HasIndex(x => x.ProfileId);
        builder.ToTable("web_push_subscription", table =>
            table.HasCheckConstraint("ck_web_push_subscription_failures", "failure_count >= 0"));
    }
}
