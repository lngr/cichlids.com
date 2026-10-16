using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public sealed class TankVisibilityConsentConfiguration : IEntityTypeConfiguration<TankVisibilityConsent>
{
    public void Configure(EntityTypeBuilder<TankVisibilityConsent> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Context).IsRequired();
        builder.HasOne<Tank>().WithMany().HasForeignKey(x => x.TankId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.TankId, x.AnsweredAt, x.Id });
        builder.ToTable("tank_visibility_consent", table =>
            table.HasCheckConstraint("ck_tank_visibility_consent_context", "context IN ('creation', 'settings')"));
    }
}
