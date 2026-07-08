using Cichlids.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cichlids.Infrastructure.Persistence.Configurations;

public class ProfileIdentityConfiguration : IEntityTypeConfiguration<ProfileIdentity>
{
    public void Configure(EntityTypeBuilder<ProfileIdentity> builder)
    {
        builder.ToTable("profile_identity");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).IsRequired();
        builder.Property(x => x.Subject).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();

        builder.HasOne<Profile>()
            .WithMany(x => x.Identities)
            .HasForeignKey(x => x.ProfileId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);
    }
}
