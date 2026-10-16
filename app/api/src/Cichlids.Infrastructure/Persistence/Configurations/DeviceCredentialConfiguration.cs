using Cichlids.Domain.Entities;
using Cichlids.Domain.Enums;
using Cichlids.Infrastructure.Persistence.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class DeviceCredentialConfiguration : IEntityTypeConfiguration<DeviceCredential>
{
    public void Configure(EntityTypeBuilder<DeviceCredential> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PublicKey).IsRequired();
        builder.Property(x => x.Algorithm).IsRequired();
        builder.Property(x => x.State).HasConversion<SnakeCaseEnumConverter<DeviceCredentialState>>().IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.LastSignedRequestAt).IsRequired();
        builder.HasOne<Profile>().WithMany().HasForeignKey(x => x.ProfileId).IsRequired().OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.PublicKey).IsUnique();
        builder.HasIndex(x => x.ProfileId);
        builder.HasIndex(x => x.LastSignedRequestAt);
        builder.ToTable("device_credential", table =>
        {
            table.HasCheckConstraint("ck_device_credential_state", "state IN ('active', 'retired')");
            table.HasCheckConstraint("ck_device_credential_algorithm", "algorithm IN ('ES256')");
        });
    }
}
