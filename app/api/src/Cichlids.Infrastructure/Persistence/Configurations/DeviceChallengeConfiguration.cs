using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class DeviceChallengeConfiguration : IEntityTypeConfiguration<DeviceChallenge>
{
    public void Configure(EntityTypeBuilder<DeviceChallenge> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChallengeHash).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.HasIndex(x => x.ChallengeHash).IsUnique();
        builder.HasIndex(x => x.ExpiresAt);
        builder.ToTable("device_challenge");
    }
}
